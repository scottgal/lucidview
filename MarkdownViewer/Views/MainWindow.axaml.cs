using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using LiveMarkdown.Avalonia;
using MarkdownViewer.Models;
using MarkdownViewer.Plugins;
using MarkdownViewer.Services;
using MermaidSharp.Rendering;
using SkiaSharp;
using VisualExtensions = Avalonia.VisualTree.VisualExtensions;

namespace MarkdownViewer.Views;

public partial class MainWindow : Window
{
    private readonly ImageCacheService _imageCacheService;
    private readonly MarkdownService _markdownService;
    private readonly IHtmlToMarkdownService _htmlToMarkdownService =
#if FULL
        MarkdownViewer.Services.FullServices.Get<IHtmlToMarkdownService>();
#else
        new MarkdownViewer.Services.HtmlToMarkdownService();
#endif
    private readonly AppSettings _settings;
    private readonly ThemeService _themeService;
    private readonly DiagramRendererPluginHost _diagramPluginHost;
    private readonly SessionHistory _sessionHistory = new();
    private bool _suppressHistoryPush;
    private IActivatableLifetime? _activatableLifetime;
    private string? _currentFilePath;
    private int _currentSearchIndex = -1;
    private int _fontSize = 16;
    private List<Style> _codeBlockStyles = [];
    private List<Style> _lineMetricsStyles = [];
    private Style? _fontStyle;
    private List<HeadingItem> _headings = [];
    private bool _isSidePanelOpen;
    private string _rawContent = string.Empty;
    private List<SearchResult> _searchResults = [];
    private AppTheme _effectiveTheme = AppTheme.Dark;
    private Avalonia.Threading.DispatcherTimer? _diagramReplacementTimer;
    private int _diagramReplacementAttempts;
    private const int MaxDiagramReplacementAttempts = 8;

    // Markdown.Avalonia's StaticBinding/IBinding implementation throws these
    // at runtime; they reach our catch blocks as plain Exception so we filter
    // by message rather than type.
    private static readonly string[] IgnorableErrors =
    [
        "Unsupported IBinding implementation",
        "StaticBinding",
        "Markdown.Avalonia.Extensions"
    ];

    private static bool IsIgnorableError(Exception ex)
    {
        var message = ex.Message;
        return IgnorableErrors.Any(e => message.Contains(e, StringComparison.OrdinalIgnoreCase));
    }

    public MainWindow()
    {
        InitializeComponent();

        // Window chrome: extend into title bar with system caption buttons
        SystemDecorations = SystemDecorations.Full;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
        ExtendClientAreaTitleBarHeightHint = 80;

        _settings = AppSettings.Load();
        _markdownService = new MarkdownService();
        _imageCacheService = new ImageCacheService();
        _markdownService.SetImageCacheService(_imageCacheService);

        // ImageBasePath = temp root so file:// images cover both diagram
        // (lucidview-mermaid) and cache (lucidview-images) subdirectories.
        MdViewer.MarkdownBuilder = new ObservableStringBuilder();
        MdViewer.ImageBasePath = Path.GetTempPath();
        MdViewer.LinkClick += OnLinkClick;
        _themeService = new ThemeService(Application.Current!) { CustomTheme = _settings.CustomTheme };
        _diagramPluginHost = new DiagramRendererPluginHost(
        [
            new AvaloniaNativeDiagramRendererPlugin(
                _markdownService,
                ResolveDiagramTextBrush,
                SaveDiagramAs,
                OpenBrowserUrl,
                ScrollToDiagram)
        ]);

        Application.Current!.PropertyChanged += OnApplicationPropertyChanged;

        // Restore saved size, clamped to sensible defaults
        Width = _settings.WindowWidth is > 0 and < 10000 ? _settings.WindowWidth : 1100;
        Height = _settings.WindowHeight is > 0 and < 10000 ? _settings.WindowHeight : 750;
        _fontSize = _settings.FontSize > 0 ? (int)_settings.FontSize : 16;

        DataContext = this;

        // Apply saved theme (needed before showing content)
        ApplyTheme(_settings.Theme);

        // Drag and drop
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Mouse wheel zoom (intercept even handled events for Ctrl+wheel)
        RenderedScroller.AddHandler(PointerWheelChangedEvent, OnMarkdownPointerWheelChanged, RoutingStrategies.Tunnel,
            true);

        UpdateRecentFiles();
        UpdateFontSizeDisplay();
        ApplyContentMaxWidth();
        ApplyRulerVisibility();
        Closing += OnWindowClosing;
        // Re-clamp the column width when the window is resized so shrinking
        // the window doesn't push the right ruler handle off-screen.
        SizeChanged += (_, _) => ApplyContentMaxWidth();

        // Accepts a positional arg:
        //   lucidVIEW path/to/document.md → LoadFile
        //   lucidVIEW file:///abs/path.md → LoadFile via Uri.LocalPath
        //   lucidVIEW https://example.com → LoadWebPage (markdown passes through, HTML converts)
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            var arg = args[1];
            if (Uri.TryCreate(arg, UriKind.Absolute, out var argUri))
            {
                if (argUri.Scheme is "http" or "https")
                    _ = LoadWebPage(arg);
                else if (argUri.Scheme == "file" && File.Exists(argUri.LocalPath))
                    _ = LoadFile(argUri.LocalPath);
                else if (File.Exists(arg))
                    _ = LoadFile(arg);
            }
            else if (File.Exists(arg))
            {
                _ = LoadFile(arg);
            }
        }

        // macOS Apple Events / Finder Open-With activations arrive via the
        // application lifetime, which outlives the window — caching the
        // reference here lets OnWindowClosing unsubscribe cleanly.
        if (Application.Current?.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            _activatableLifetime = activatable;
            activatable.Activated += OnActivated;
        }

        // Defer non-critical startup work until after window is shown
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            SetWindowIcon();
            ApplyTypography();
        }, Avalonia.Threading.DispatcherPriority.Background);

#if FULL
        this.Title = "lucidVIEW-FULL";  // Override lean default so identity is visible
        FullDiagnosticsSeparator.IsVisible = true;
        FullDiagnosticsMenu.IsVisible = true;

        // `--shot URL OUTPUT.png` mode: don't grab focus, don't show first-run
        // dialog, auto-navigate, wait, screenshot, exit. Detected by FullProgram
        // before Avalonia init and stashed on static fields.
        if (MarkdownViewer.FullProgram.AutoShotUrl is { } shotUrl
            && MarkdownViewer.FullProgram.AutoShotOutput is { } shotOut)
        {
            ShowActivated = false;
            ShowInTaskbar = false;
            // Sizing must be set BEFORE Open so the visual tree lays out at the
            // requested DIPs. macOS clamps off-screen Positions on the visible
            // monitor edge — moving the window after Open is the only reliable
            // way to keep it out of the way.
            Width = 1470;
            Height = 900;
            Opened += async (_, _) =>
            {
                Position = new Avalonia.PixelPoint(-3000, -3000);
                await RunAutoShotAsync(shotUrl, shotOut);
            };
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                var settings = MarkdownViewer.Models.AppSettingsFull.Load();
                if (!settings.HasRunBefore)
                    await new MarkdownViewer.Views.FirstRunBootstrapDialog().ShowDialog(this);
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        // FULL is busier than lean — hide the redundant fit/1:1 buttons so the
        // scale slider has room next to the pipeline indicator. Lean keeps them.
        FitWidthToggle.IsVisible = false;
        FitHeightToggle.IsVisible = false;
        ResetZoomBtn.IsVisible = false;

        // Scan-mode toggle (RagFull ↔ Sitemap). Lean leaves the button hidden;
        // FULL surfaces it because the Sitemap profile is a dogfood feature for
        // the browser-mode use case (title + nav + breadcrumb extraction).
        ScanModeToggleBtn.IsVisible = true;

        // Pipeline-stage indicator (replaces the single-text ExtractionStatusText).
        ExtractionStagesPanel.IsVisible = true;
        var telemetry = MarkdownViewer.Services.FullServices.Get<MarkdownViewer.Services.ExtractionTelemetry>();
        telemetry.StageChanged += evt => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            ApplyStageEvent(evt));
        telemetry.Recorded += info => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // The final Record fires after extraction completes — render is
            // about to happen, so light up the Render segment.
            StageRenderText.Text = $"render {info.BlockCount} blocks · {info.OutputCharacterCount / 1024}K";
            StageRenderText.Opacity = 1.0;
        });
        KeyDown += (s, e) =>
        {
            if (e.Key == Avalonia.Input.Key.F2) ShowExtractionDetails();
        };
#endif
    }

    #region Mouse Wheel Zoom & Scroll

    private void OnMarkdownPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl + Mouse wheel to zoom
            var delta = e.Delta.Y > 0 ? 10 : -10;
            var newValue = Math.Clamp(ZoomSlider.Value + delta, 50, 200);
            ZoomSlider.Value = newValue;
            e.Handled = true;
        }
        else
        {
            // Regular mouse wheel scrolls the document
            var scrollAmount = e.Delta.Y * 50; // 50px per wheel notch
            var newOffset = RenderedScroller.Offset.Y - scrollAmount;
            newOffset = Math.Clamp(newOffset, 0,
                Math.Max(0, RenderedScroller.Extent.Height - RenderedScroller.Viewport.Height));
            RenderedScroller.Offset = new Vector(0, newOffset);
            e.Handled = true;
        }
    }

    #endregion

    #region Window Events

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (Application.Current is not null)
            Application.Current.PropertyChanged -= OnApplicationPropertyChanged;

        if (_activatableLifetime is not null)
        {
            _activatableLifetime.Activated -= OnActivated;
            _activatableLifetime = null;
        }

        if (_diagramReplacementTimer is not null)
        {
            _diagramReplacementTimer.Stop();
            _diagramReplacementTimer.Tick -= OnDiagramReplacementTick;
            _diagramReplacementTimer = null;
        }

        _settings.WindowWidth = (int)Width;
        _settings.WindowHeight = (int)Height;
        _settings.Save();
    }

    private void OnApplicationPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!string.Equals(e.Property.Name, nameof(Application.ActualThemeVariant), StringComparison.Ordinal))
            return;

        if (_themeService.RequestedTheme != AppTheme.Auto)
            return;

        var refreshedTheme = _themeService.RefreshAutoTheme();
        if (refreshedTheme == _effectiveTheme)
            return;

        _effectiveTheme = refreshedTheme;
        ApplyEffectiveTheme(_effectiveTheme);
    }

    #endregion

    #region Window Icon

    private void SetWindowIcon()
    {
        try
        {
            // Generate a simple icon programmatically using SkiaSharp
            using var surface = SKSurface.Create(new SKImageInfo(64, 64));
            var canvas = surface.Canvas;

            // Dark background with rounded corners
            using var bgPaint = new SKPaint { Color = new SKColor(26, 26, 46), IsAntialias = true };
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(0, 0, 64, 64), 8), bgPaint);

            // Draw "l" in gray (italic approximation via skew)
            using var lucidTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.BoldItalic);
            using var lucidFont = new SKFont(lucidTypeface, 36);
            using var lucidPaint = new SKPaint { Color = new SKColor(0xDD, 0xDD, 0xDD), IsAntialias = true };
            canvas.Save();
            canvas.Skew(-0.15f, 0);
            canvas.DrawText("l", 14, 46, lucidFont, lucidPaint);
            canvas.Restore();

            // Draw "V" in white (bold)
            using var viewTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
            using var viewFont = new SKFont(viewTypeface, 36);
            using var viewPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText("V", 30, 46, viewFont, viewPaint);

            // Convert to Avalonia bitmap
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());

            Icon = new WindowIcon(stream);
        }
        catch
        {
            // Ignore icon errors - not critical
        }
    }

    #endregion

    #region Commands

    private ICommand? _openFileCommand;
    private ICommand? _openWebPageCommand;
    private ICommand? _focusAddressBarCommand;
    private ICommand? _goBackCommand;
    private ICommand? _goForwardCommand;
    private ICommand? _reloadCommand;
    private ICommand? _openInExternalBrowserCommand;
    private ICommand? _saveAsMarkdownCommand;
    private ICommand? _openSettingsCommand;
    private ICommand? _toggleFullScreenCommand;
    private ICommand? _toggleSidePanelCommand;
    private ICommand? _toggleSearchCommand;
    private ICommand? _escapeCommand;
    private ICommand? _fontSizeIncreaseCommand;
    private ICommand? _fontSizeDecreaseCommand;
    private ICommand? _printCommand;
    private ICommand? _exportPdfCommand;
    private ICommand? _openHelpCommand;
    private ICommand? _openUserManualCommand;
    private ICommand? _debugScreenshotCommand;
    private ICommand? _exitCommand;
    private ICommand? _navigateCommand;

    public ICommand OpenFileCommand => _openFileCommand ??= new RelayCommand(async () => await OpenFile());
    public ICommand OpenWebPageCommand => _openWebPageCommand ??= new RelayCommand(async () => await OpenWebPage());
    public ICommand FocusAddressBarCommand => _focusAddressBarCommand ??= new RelayCommand(FocusAddressBar);
    public ICommand GoBackCommand => _goBackCommand ??= new RelayCommand(async () => await GoBack());
    public ICommand GoForwardCommand => _goForwardCommand ??= new RelayCommand(async () => await GoForward());
    public ICommand ReloadCommand => _reloadCommand ??= new RelayCommand(async () => await Reload());
    public ICommand OpenInExternalBrowserCommand => _openInExternalBrowserCommand ??= new RelayCommand(OpenCurrentInExternalBrowser);
    public ICommand SaveAsMarkdownCommand => _saveAsMarkdownCommand ??= new RelayCommand(async () => await SaveAsMarkdown());
    public ICommand OpenSettingsCommand => _openSettingsCommand ??= new RelayCommand(async () => await OpenSettings());
    public ICommand ToggleFullScreenCommand => _toggleFullScreenCommand ??= new RelayCommand(ToggleFullScreen);
    public ICommand ToggleSidePanelCommand => _toggleSidePanelCommand ??= new RelayCommand(ToggleSidePanel);
    public ICommand ToggleSearchCommand => _toggleSearchCommand ??= new RelayCommand(ToggleSearch);
    public ICommand EscapeCommand => _escapeCommand ??= new RelayCommand(OnEscape);
    public ICommand FontSizeIncreaseCommand => _fontSizeIncreaseCommand ??= new RelayCommand(IncreaseFontSize);
    public ICommand FontSizeDecreaseCommand => _fontSizeDecreaseCommand ??= new RelayCommand(DecreaseFontSize);
    public ICommand PrintCommand => _printCommand ??= new RelayCommand(async () => await PrintToPrinter());
    public ICommand ExportPdfCommand => _exportPdfCommand ??= new RelayCommand(async () => await ExportPdf());
    public ICommand OpenHelpCommand => _openHelpCommand ??= new RelayCommand(async () => await OpenHelp());
    public ICommand OpenUserManualCommand => _openUserManualCommand ??= new RelayCommand(async () => await OpenUserManual());
    public ICommand DebugScreenshotCommand => _debugScreenshotCommand ??= new RelayCommand(async () => await DebugScreenshot());
    public ICommand ExitCommand => _exitCommand ??= new RelayCommand(() => Close());

    // Reflected at runtime by Mostlylucid.Avalonia.UITesting; do not remove.
    public ICommand NavigateCommand => _navigateCommand ??= new RelayCommand<string>(path =>
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            _ = LoadFile(path);
    });

    #endregion

    #region Side Panel

    private void ToggleSidePanel()
    {
        _isSidePanelOpen = !_isSidePanelOpen;
        SidePanel.IsVisible = _isSidePanelOpen;
        SidePanelOverlay.IsVisible = _isSidePanelOpen;
    }

    private void OnToggleSidePanel(object? sender, RoutedEventArgs e)
    {
        ToggleSidePanel();
    }

    private void OnCloseSidePanel(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
    }

    private void OnOverlayClick(object? sender, PointerPressedEventArgs e)
    {
        CloseSidePanel();
    }

    private void CloseSidePanel()
    {
        _isSidePanelOpen = false;
        SidePanel.IsVisible = false;
        SidePanelOverlay.IsVisible = false;
    }

    private void OnEscape()
    {
        if (WindowState == WindowState.FullScreen)
            WindowState = WindowState.Normal;
        else if (_isSidePanelOpen)
            CloseSidePanel();
        else if (SearchPanel.IsVisible)
            CloseSearch();
    }

    #endregion

    private async Task DisplayMarkdown(string content)
    {
        _rawContent = content;

        // Extract headings for navigation
        _headings = NavigationService.ExtractHeadings(content);

        // Extract and display metadata (categories, publication date)
        var metadata = MarkdownService.ExtractMetadata(content);
        DisplayMetadata(metadata);

        // Phase 1: Fast path - text processing + cached mermaid diagrams
        var (processed, pendingDiagrams) = _markdownService.ProcessMarkdownFast(content);

        // Assign a brand-new ObservableStringBuilder each render. LiveMarkdown's
        // image cache survives Clear+Append on the same instance, so http-loaded
        // shields stayed huge even after the markdown source was rewritten to
        // local paths. A new instance is the only thing that forces LiveMarkdown
        // to drop its cached image bitmaps and re-parse from scratch.
        var newBuilder = new LiveMarkdown.Avalonia.ObservableStringBuilder();
        newBuilder.Append(processed);
        MdViewer.MarkdownBuilder = newBuilder;
        RawTextBlock.Text = content;

        WelcomePanel.IsVisible = false;
        ContentGrid.IsVisible = true;
        UpdateToc();

        PreviewTab.IsChecked = true;
        RenderedPanel.IsVisible = true;
        RawScroller.IsVisible = false;

        // Replace marker text with native diagram controls once the visual tree is ready.
        ScheduleDiagramMarkerReplacement();

        // Promote any GIF Image controls to AnimatedImage so they actually play.
        // Deferred until layout has produced the visual tree.
        SchedulePromoteAnimatedImages(content);

        // Apply natural display sizes to cached images (shields are rendered
        // at 2× for hi-DPI crispness — without this they'd render at the
        // physical pixel count and appear double-sized).
        ScheduleConstrainCachedImages(processed);

        // Phase 2: Render uncached diagrams in background, then swap in results
        if (pendingDiagrams.Count > 0)
        {
            var ct = _markdownService.BeginNewRenderBatch();
            try
            {
                var replacements = await _markdownService.RenderPendingDiagramsAsync(pendingDiagrams, ct);

                // Apply replacements to the displayed content
                var updated = processed;
                foreach (var (placeholder, replacement) in replacements)
                {
                    updated = updated.Replace(placeholder, replacement);
                }

                // Save scroll position and refresh — assign a new builder
                // (same reason as the first phase: LiveMarkdown's image cache
                // doesn't invalidate on Clear+Append against the same instance).
                var scrollOffset = RenderedScroller.Offset;
                var diagramBuilder = new LiveMarkdown.Avalonia.ObservableStringBuilder();
                diagramBuilder.Append(updated);
                MdViewer.MarkdownBuilder = diagramBuilder;
                RenderedScroller.Offset = scrollOffset;

                // Re-run marker replacement after pending diagrams update.
                ScheduleDiagramMarkerReplacement();
                ScheduleConstrainCachedImages(updated);
            }
            catch (OperationCanceledException)
            {
                // New file/theme switch cancelled this batch - that's fine
            }
        }
    }

    private static List<HeadingItem> FlattenHeadings(List<HeadingItem> headings)
    {
        var result = new List<HeadingItem>();
        foreach (var heading in headings)
        {
            result.Add(heading);
            result.AddRange(FlattenHeadings(heading.Children));
        }

        return result;
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        // Split on whitespace and count non-empty entries
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private void DisplayMetadata(DocumentMetadata metadata)
    {
        if (!metadata.HasMetadata)
        {
            MetadataPanel.IsVisible = false;
            return;
        }

        MetadataPanel.IsVisible = true;

        // Display categories
        if (metadata.Categories.Count > 0)
            CategoriesControl.ItemsSource = metadata.Categories;
        else
            CategoriesControl.ItemsSource = null;

        // Display publication date
        if (metadata.PublicationDate.HasValue)
        {
            MetadataDateLabel.IsVisible = true;
            MetadataDateText.Text = metadata.PublicationDate.Value.ToString("MMMM d, yyyy");
        }
        else
        {
            MetadataDateLabel.IsVisible = false;
            MetadataDateText.Text = "";
        }
    }

    #region Recent Files

    private void UpdateRecentFiles()
    {
        RecentFilesList.ItemsSource = _settings.RecentFiles.Take(10).ToList();
    }

    private async void OnRecentFileClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                CloseSidePanel();
                if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    await LoadWebPage(path);
                else
                    await LoadFile(path);
            }
        }
        catch (Exception ex) when (!IsIgnorableError(ex))
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    #endregion

    #region Theme

    private void ApplyTheme(AppTheme theme)
    {
        _effectiveTheme = _themeService.ApplyTheme(theme);
        _settings.Theme = theme;
        ApplyEffectiveTheme(_effectiveTheme);
    }

    private void ApplyEffectiveTheme(AppTheme effectiveTheme)
    {
        UpdatePanelOverlay(effectiveTheme);

        var themeDefinition = ThemeColors.GetTheme(effectiveTheme);
        _markdownService.SetThemeColors(themeDefinition);

        UpdateThemeCardSelection(effectiveTheme);

        // Refresh current document to regenerate mermaid diagrams with new theme colors
        if (!string.IsNullOrEmpty(_rawContent)) _ = DisplayMarkdown(_rawContent);
    }

    private static void UpdatePanelOverlay(AppTheme theme)
    {
        // Light themes get dark overlay, dark themes get light overlay
        var isLightTheme = IsLightTheme(theme);
        var overlayColor = isLightTheme ? "#60000000" : "#40ffffff";

        if (Application.Current?.Resources != null)
            Application.Current.Resources["PanelOverlay"] = new SolidColorBrush(
                Color.Parse(overlayColor));
    }

    private void UpdateThemeCardSelection(AppTheme theme)
    {
        // Remove selected class from all
        ThemeLightCard.Classes.Remove("selected");
        ThemeDarkCard.Classes.Remove("selected");
        ThemeVSCodeCard.Classes.Remove("selected");
        ThemeGitHubCard.Classes.Remove("selected");
        ThemeMostlyLucidDarkCard.Classes.Remove("selected");
        ThemeMostlyLucidLightCard.Classes.Remove("selected");
        ThemePrideCard.Classes.Remove("selected");
        ThemeCustomCard.Classes.Remove("selected");

        // Custom theme card is only visible if the user defined one in settings.json
        ThemeCustomCard.IsVisible = _settings.CustomTheme != null;

        // Add selected class to current theme
        switch (theme)
        {
            case AppTheme.Light:
                ThemeLightCard.Classes.Add("selected");
                break;
            case AppTheme.Dark:
                ThemeDarkCard.Classes.Add("selected");
                break;
            case AppTheme.VSCode:
                ThemeVSCodeCard.Classes.Add("selected");
                break;
            case AppTheme.GitHub:
                ThemeGitHubCard.Classes.Add("selected");
                break;
            case AppTheme.MostlyLucidDark:
                ThemeMostlyLucidDarkCard.Classes.Add("selected");
                break;
            case AppTheme.MostlyLucidLight:
                ThemeMostlyLucidLightCard.Classes.Add("selected");
                break;
            case AppTheme.Pride:
                ThemePrideCard.Classes.Add("selected");
                break;
            case AppTheme.Custom:
                ThemeCustomCard.Classes.Add("selected");
                break;
        }
    }

    private void OnThemeCardClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string themeName)
            if (Enum.TryParse<AppTheme>(themeName, out var theme))
            {
                ApplyTheme(theme);
                _settings.Save();
            }
    }

    #endregion

    #region Font Size

    private void IncreaseFontSize()
    {
        _fontSize = Math.Min(32, _fontSize + 2);
        ApplyFontSize();
    }

    private void DecreaseFontSize()
    {
        _fontSize = Math.Max(10, _fontSize - 2);
        ApplyFontSize();
    }

    private void OnFontSizeIncrease(object? sender, RoutedEventArgs e)
    {
        IncreaseFontSize();
    }

    private void OnFontSizeDecrease(object? sender, RoutedEventArgs e)
    {
        DecreaseFontSize();
    }

    private void ApplyFontSize()
    {
        // Apply font size via LayoutTransformControl for proper layout handling
        var scale = _fontSize / 16.0;
        MarkdownLayoutTransform.LayoutTransform = new ScaleTransform(scale, scale);

        _settings.FontSize = _fontSize;
        _settings.Save();
        UpdateFontSizeDisplay();
        RefreshRulerForScaleChange();
    }

    private void UpdateFontSizeDisplay()
    {
        FontSizeText.Text = $"{_fontSize}px";
    }

    private void EnableDocumentControls(bool enabled)
    {
        FontDecreaseBtn.IsEnabled = enabled;
        FontIncreaseBtn.IsEnabled = enabled;
        TocButton.IsEnabled = enabled;
        ZoomControlsPanel.IsEnabled = enabled;
        ViewTabsPanel.IsEnabled = enabled;
        SearchButton.IsEnabled = enabled;
    }

    #endregion

    #region Typography

    private void ApplyTypography()
    {
        // Apply font family via Style since MarkdownRenderer doesn't have a direct FontFamily property
        if (_fontStyle != null)
            MdViewer.Styles.Remove(_fontStyle);

        var fontFamily = new FontFamily(_settings.FontFamily);
        _fontStyle = new Style(x => x.OfType<LiveMarkdown.Avalonia.MarkdownRenderer>())
        {
            Setters =
            {
                new Setter(TextElement.FontFamilyProperty, fontFamily)
            }
        };
        MdViewer.Styles.Add(_fontStyle);

        ApplyLineMetricsStyles();

        var codeFont = new FontFamily(_settings.CodeFontFamily);
        RawTextBlock.FontFamily = codeFont;
        RawTextBlock.FontSize = _settings.CodeFontSize;
        ApplyCodeBlockStyle(codeFont, _settings.CodeFontSize);

        _fontSize = _settings.FontSize > 0 ? (int)_settings.FontSize : 16;
        ApplyFontSize();

        if (!string.IsNullOrEmpty(_rawContent))
            _ = DisplayMarkdown(_rawContent);
    }

    private void ApplyCodeBlockStyle(FontFamily codeFont, double codeFontSize)
    {
        foreach (var style in _codeBlockStyles)
            MdViewer.Styles.Remove(style);
        _codeBlockStyles.Clear();

        var labelFont = new FontFamily("Segoe UI, Arial, sans-serif");

        // LiveMarkdown.Avalonia renders fenced code as a nested MarkdownTextBlock
        // whose template part is named PART_CodeTextBlock; the language tag is
        // PART_LanguageTextBlock. Set FontFamily/FontSize on the parts directly —
        // selecting by name reaches them regardless of parent control type.
        var codeBlockStyle = new Style(x => x.Name("PART_CodeTextBlock"))
        {
            Setters =
            {
                new Setter(TextElement.FontFamilyProperty, codeFont),
                new Setter(TextElement.FontSizeProperty, codeFontSize)
            }
        };

        var codeTextBlockStyle = new Style(x => x.Name("PART_CodeTextBlock").Descendant().OfType<TextBlock>())
        {
            Setters =
            {
                new Setter(TextBlock.FontFamilyProperty, codeFont),
                new Setter(TextBlock.FontSizeProperty, codeFontSize)
            }
        };

        var codeSelectableStyle = new Style(x => x.Name("PART_CodeTextBlock").Descendant().OfType<SelectableTextBlock>())
        {
            Setters =
            {
                new Setter(SelectableTextBlock.FontFamilyProperty, codeFont),
                new Setter(SelectableTextBlock.FontSizeProperty, codeFontSize)
            }
        };

        var languageLabelStyle = new Style(x => x.Name("PART_LanguageTextBlock"))
        {
            Setters =
            {
                new Setter(TextBlock.FontFamilyProperty, labelFont),
                new Setter(TextBlock.FontSizeProperty, 12.0),
                new Setter(TextBlock.FontStyleProperty, FontStyle.Normal),
                new Setter(TextBlock.FontWeightProperty, FontWeight.Normal)
            }
        };

        // Inline code (`backtick`) is wrapped in InlineUIContainer { Classes
        // = { "Code" } } → Border → MarkdownTextBlock. Avalonia matches the
        // OfType<T> selector EXACTLY (not by inheritance) so we have to
        // target MarkdownTextBlock by its concrete type, not SelectableTextBlock.
        var inlineCodeStyle = new Style(x => x
            .OfType<InlineUIContainer>().Class("Code")
            .Descendant().OfType<MarkdownTextBlock>())
        {
            Setters =
            {
                new Setter(MarkdownTextBlock.FontFamilyProperty, codeFont)
            }
        };

        _codeBlockStyles.Add(codeBlockStyle);
        _codeBlockStyles.Add(codeTextBlockStyle);
        _codeBlockStyles.Add(codeSelectableStyle);
        _codeBlockStyles.Add(languageLabelStyle);
        _codeBlockStyles.Add(inlineCodeStyle);

        foreach (var s in _codeBlockStyles)
            MdViewer.Styles.Add(s);
    }

    private void ApplyLineMetricsStyles()
    {
        foreach (var style in _lineMetricsStyles)
            MdViewer.Styles.Remove(style);
        _lineMetricsStyles.Clear();

        // LineHeight is a multiplier of the resolved font size. A value <= 1.0
        // means "use the font's natural line metrics" (don't fight the typeface).
        // Letter spacing is in DIPs added between glyphs; 0 = natural kerning.
        var lineHeight = _settings.LineHeight;
        var letterSpacing = _settings.LetterSpacing;
        var hasLineHeight = lineHeight > 1.0;
        var hasLetterSpacing = Math.Abs(letterSpacing) > 0.01;
        if (!hasLineHeight && !hasLetterSpacing) return;

        // LiveMarkdown emits paragraphs as MarkdownTextBlock (subclasses TextBlock)
        // and selectable runs as SelectableTextBlock. Avalonia's OfType<T> matches
        // exactly, not subclasses, so we have to enumerate all three. Code blocks
        // re-set their own font via PART_CodeTextBlock styles after this — that's
        // fine; we don't want body line-height bleeding into code anyway.
        void AddStyle(Func<Selector?, Selector> selector)
        {
            var style = new Style(selector);
            if (hasLineHeight) style.Setters.Add(new Setter(TextBlock.LineHeightProperty, _settings.FontSize * lineHeight));
            if (hasLetterSpacing) style.Setters.Add(new Setter(TextBlock.LetterSpacingProperty, letterSpacing));
            _lineMetricsStyles.Add(style);
        }

        AddStyle(x => x.OfType<LiveMarkdown.Avalonia.MarkdownRenderer>().Descendant().OfType<TextBlock>());
        AddStyle(x => x.OfType<LiveMarkdown.Avalonia.MarkdownRenderer>().Descendant().OfType<SelectableTextBlock>());
        AddStyle(x => x.OfType<LiveMarkdown.Avalonia.MarkdownRenderer>().Descendant().OfType<MarkdownTextBlock>());

        foreach (var s in _lineMetricsStyles)
            MdViewer.Styles.Add(s);
    }

    #endregion

    #region Settings

    private void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = OpenSettings();
    }

    private async Task OpenSettings()
    {
        var dialog = new SettingsDialog(_settings);

        // Subscribe to live settings changes
        dialog.SettingsChanged += () =>
        {
            ApplyTheme(_settings.Theme);
            ApplyTypography();
        };

        await dialog.ShowDialog(this);

        // Final apply in case dialog was closed without save
        ApplyTheme(_settings.Theme);
        ApplyTypography();
    }

    #endregion

    #region View

    private void OnTabChanged(object? sender, RoutedEventArgs e)
    {
        var isPreview = PreviewTab.IsChecked == true;
        RenderedPanel.IsVisible = isPreview;
        RawScroller.IsVisible = !isPreview;
    }

    private void ToggleFullScreen()
    {
        WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
    }


    #endregion

    private void UpdateToc()
    {
        // Flatten hierarchical headings to display all levels
        var flatHeadings = FlattenHeadings(_headings);

        // Update TOC items control with proper indentation by level
        TocItemsControl.ItemsSource = flatHeadings.Select(h => new TocItem
        {
            Text = h.Text,
            Margin = new Thickness((h.Level - 1) * 16, 4, 0, 4),
            Heading = h
        }).ToList();
    }

    private void OnTocSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TocItemsControl.SelectedItem is TocItem item)
        {
            ScrollToHeading(item.Heading);
            // Keep TOC open for navigation - don't close
            // Clear selection so same item can be clicked again
            TocItemsControl.SelectedItem = null;
        }
    }

    private class TocItem
    {
        public string Text { get; set; } = "";
        public Thickness Margin { get; set; }
        public HeadingItem Heading { get; set; } = null!;
    }

    private void OnFitModeToggle(object? sender, RoutedEventArgs e)
    {
        if (sender == FitWidthToggle)
        {
            FitWidthToggle.IsChecked = true;
            FitHeightToggle.IsChecked = false;
            ApplyFitWidth();
        }
        else if (sender == FitHeightToggle)
        {
            FitWidthToggle.IsChecked = false;
            FitHeightToggle.IsChecked = true;
            ApplyFitHeight();
        }
    }

    private void ApplyFitWidth()
    {
        // Reset to width-based scaling (default behavior)
        var scale = _fontSize / 16.0;
        MarkdownLayoutTransform.LayoutTransform = new ScaleTransform(scale, scale);
        ZoomSlider.Value = _fontSize / 16.0 * 100;
        UpdateZoomPercentText();
        RefreshRulerForScaleChange();
    }

    private void ApplyFitHeight()
    {
        // Scale to fit viewport height
        if (RenderedScroller.Viewport.Height > 0 && MdViewer.Bounds.Height > 0)
        {
            var viewportHeight = RenderedScroller.Viewport.Height;
            var contentHeight = MdViewer.Bounds.Height;
            var scale = Math.Min(2.0, Math.Max(0.5, viewportHeight / contentHeight));
            MarkdownLayoutTransform.LayoutTransform = new ScaleTransform(scale, scale);
            ZoomSlider.Value = scale * 100;
            UpdateZoomPercentText();
            RefreshRulerForScaleChange();
        }
    }

    private void OnZoomSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (MarkdownLayoutTransform == null) return;

        var scale = e.NewValue / 100.0;
        MarkdownLayoutTransform.LayoutTransform = new ScaleTransform(scale, scale);
        UpdateZoomPercentText();

        // Deselect fit mode toggles when manually adjusting
        if (Math.Abs(e.NewValue - _fontSize / 16.0 * 100) > 1)
        {
            FitWidthToggle.IsChecked = false;
            FitHeightToggle.IsChecked = false;
        }

        RefreshRulerForScaleChange();
    }

    private void OnResetZoom(object? sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = 100;
        FitWidthToggle.IsChecked = true;
        FitHeightToggle.IsChecked = false;
    }

    private void UpdateZoomPercentText()
    {
        if (ZoomPercentText != null) ZoomPercentText.Text = $"{(int)ZoomSlider.Value}%";
    }

    #region Context Menu & Clipboard

    private async void OnCopyText(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard != null && !string.IsNullOrEmpty(_rawContent)) await clipboard.SetTextAsync(_rawContent);
        }
        catch (Exception ex) when (!IsIgnorableError(ex))
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e)
    {
        // Markdown.Avalonia doesn't support text selection natively
        // Switch to raw view for selection
        RawTab.IsChecked = true;
        RenderedPanel.IsVisible = false;
        RawScroller.IsVisible = true;
    }

    private async void OnCopyAsHtml(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard != null && !string.IsNullOrEmpty(_rawContent))
            {
                var html = Markdig.Markdown.ToHtml(_rawContent);
                await clipboard.SetTextAsync(html);
            }
        }
        catch (Exception ex) when (!IsIgnorableError(ex))
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    // Track which mermaid source the context menu applies to
    private string? _contextMenuMermaidCode;
    private string? _contextMenuImagePath;

    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Try to find if the right-click is over a mermaid diagram image
        _contextMenuMermaidCode = null;
        _contextMenuImagePath = null;

        // Check all mermaid diagrams - if there's at least one, show the options
        var diagrams = _markdownService.MermaidDiagrams;
        var hasDiagrams = diagrams.Count > 0;

        DiagramMenuSeparator.IsVisible = hasDiagrams;
        DiagramSavePngItem.IsVisible = hasDiagrams;
        DiagramSaveSvgItem.IsVisible = hasDiagrams;
        DiagramViewItem.IsVisible = hasDiagrams;

        if (hasDiagrams)
        {
            // Use the first diagram as default (for single-diagram docs this is perfect)
            var first = diagrams.First();
            _contextMenuImagePath = first.Key;
            _contextMenuMermaidCode = first.Value;
        }
    }

    private void OnSaveDiagramPng(object? sender, RoutedEventArgs e)
    {
        if (_contextMenuMermaidCode != null)
            _ = SaveDiagramAs(_contextMenuMermaidCode, "png");
    }

    private void OnSaveDiagramSvg(object? sender, RoutedEventArgs e)
    {
        if (_contextMenuMermaidCode != null)
            _ = SaveDiagramAs(_contextMenuMermaidCode, "svg");
    }

    private void OnViewDiagramFullSize(object? sender, RoutedEventArgs e)
    {
        if (_contextMenuImagePath != null)
            ShowDiagramPopup(_contextMenuImagePath, _contextMenuMermaidCode);
    }

    #endregion

    #region TOC Panel

    private bool _isTocOpen;

    private void OnToggleToc(object? sender, RoutedEventArgs e)
    {
        _isTocOpen = !_isTocOpen;
        TocPanel.IsVisible = _isTocOpen;
    }

    private void OnCloseToc(object? sender, RoutedEventArgs e)
    {
        _isTocOpen = false;
        TocPanel.IsVisible = false;
    }

    #endregion

    #region Search

    private void ToggleSearch()
    {
        if (!ContentGrid.IsVisible) return;

        SearchPanel.IsVisible = !SearchPanel.IsVisible;
        if (SearchPanel.IsVisible)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
        else
        {
            ClearSearch();
        }
    }

    private void OnToggleSearch(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        ToggleSearch();
    }

    private void CloseSearch()
    {
        SearchPanel.IsVisible = false;
        ClearSearch();
    }

    private void ClearSearch()
    {
        _searchResults.Clear();
        _currentSearchIndex = -1;
        SearchResultsText.Text = "";
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                SearchPrevious();
            else
                SearchNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseSearch();
            e.Handled = true;
        }
    }

    private void OnSearchPrevious(object? sender, RoutedEventArgs e)
    {
        SearchPrevious();
    }

    private void OnSearchNext(object? sender, RoutedEventArgs e)
    {
        SearchNext();
    }

    private void OnCloseSearch(object? sender, RoutedEventArgs e)
    {
        CloseSearch();
    }

    private void SearchNext()
    {
        PerformSearch();
        if (_searchResults.Count == 0) return;

        _currentSearchIndex = (_currentSearchIndex + 1) % _searchResults.Count;
        HighlightCurrentResult();
    }

    private void SearchPrevious()
    {
        PerformSearch();
        if (_searchResults.Count == 0) return;

        _currentSearchIndex = _currentSearchIndex <= 0
            ? _searchResults.Count - 1
            : _currentSearchIndex - 1;
        HighlightCurrentResult();
    }

    private void PerformSearch()
    {
        var query = SearchBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            ClearSearch();
            return;
        }

        _searchResults = SearchService.Search(_rawContent, query);
        _currentSearchIndex = -1;

        if (_searchResults.Count == 0) SearchResultsText.Text = "No matches";
    }

    private void HighlightCurrentResult()
    {
        if (_currentSearchIndex < 0 || _currentSearchIndex >= _searchResults.Count)
            return;

        var result = _searchResults[_currentSearchIndex];
        SearchResultsText.Text = $"{_currentSearchIndex + 1} of {_searchResults.Count}";

        // Switch to raw view to show line-based search results
        RawTab.IsChecked = true;
        RenderedPanel.IsVisible = false;
        RawScroller.IsVisible = true;

        // Scroll to the line containing the result
        var lines = _rawContent.Split('\n');
        if (result.Line < lines.Length)
        {
            var lineHeight = 18.0;
            var scrollOffset = result.Line * lineHeight;
            RawScroller.Offset = new Vector(0, Math.Max(0, scrollOffset - 100));
        }
    }

    #endregion

    #region FULL Diagnostics

#if FULL
    private async void OnReDownloadModel(object? sender, RoutedEventArgs e)
    {
        StatusText.Text = "Downloading model…";
        await MarkdownViewer.Services.ModelBootstrap.EnsureModelAsync(null, CancellationToken.None);
        StatusText.Text = "Model ready.";
    }

    private async void OnReinstallBrowsers(object? sender, RoutedEventArgs e)
    {
        StatusText.Text = "Installing browsers…";
        await MarkdownViewer.Services.ModelBootstrap.EnsureBrowsersAsync(null, CancellationToken.None);
        StatusText.Text = "Browsers ready.";
    }

    private async void OnShowDoctor(object? sender, RoutedEventArgs e)
    {
        var report = MarkdownViewer.Services.ModelBootstrap.Doctor();
        var content = $"""
            Model: {report.ModelPath}
            Present: {report.ModelPresent} ({report.ModelSizeBytes / 1024 / 1024} MB)

            Browsers: {report.BrowsersPath}
            Present: {report.BrowsersPresent}

            Ready: {report.Ready}
            """;
        await new MarkdownViewer.Views.InputDialog(
            "lucidVIEW-FULL — doctor", content)
            .ShowDialog(this);
    }
    /// <summary>
    /// Toggle FULL's extraction mode (Read=RagFull, Scan=Sitemap) and re-load
    /// the current URL so the new profile takes effect.
    /// </summary>
    private async void OnToggleScanMode(object? sender, RoutedEventArgs e)
    {
        MarkdownViewer.Services.HtmlToMarkdownServiceFull.CurrentMode =
            ScanModeToggleBtn.IsChecked == true
                ? MarkdownViewer.Services.HtmlToMarkdownServiceFull.Mode.Scan
                : MarkdownViewer.Services.HtmlToMarkdownServiceFull.Mode.Read;

        // Re-load the current page so the new mode is applied. If no URL was
        // loaded (e.g. local file), this is a no-op.
        if (_currentFilePath is { } current
            && Uri.TryCreate(current, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            _suppressHistoryPush = true;
            try { await LoadWebPage(current); }
            finally { _suppressHistoryPush = false; }
        }
    }
#else
    private void OnReDownloadModel(object? sender, RoutedEventArgs e) { }
    private void OnReinstallBrowsers(object? sender, RoutedEventArgs e) { }
    private void OnShowDoctor(object? sender, RoutedEventArgs e) { }
    private void OnToggleScanMode(object? sender, RoutedEventArgs e) { }
#endif

    #endregion

    #region FULL Extraction Telemetry

#if FULL
    /// <summary>
    /// Pipeline-stage indicator. Each segment lives in the status bar at 0.4
    /// opacity until its stage fires. When a stage starts (Started=true) the
    /// segment goes italic + 1.0 opacity; when it completes (Started=false)
    /// the text updates with the detail value and stays at 1.0.
    ///
    /// Triggered by <c>ExtractionTelemetry.StageChanged</c> which is emitted
    /// by <c>HtmlToMarkdownServiceFull.ConvertAsync</c> for Fetch+Match and
    /// by <c>FullServices</c>'s templates-dir FileSystemWatcher for Llm.
    /// Render fires from the existing <c>Recorded</c> handler.
    /// </summary>
    private void ApplyStageEvent(MarkdownViewer.Services.StageEvent evt)
    {
        var (block, baseLabel) = evt.Stage switch
        {
            MarkdownViewer.Services.ExtractionStage.Fetch  => (StageFetchText,  "fetch"),
            MarkdownViewer.Services.ExtractionStage.Match  => (StageMatchText,  "match"),
            MarkdownViewer.Services.ExtractionStage.Llm    => (StageLlmText,    "llm"),
            MarkdownViewer.Services.ExtractionStage.Render => (StageRenderText, "render"),
            _ => ((Avalonia.Controls.TextBlock?)null, "")
        };
        if (block is null) return;

        block.Opacity = 1.0;
        if (evt.Started)
        {
            block.FontStyle = Avalonia.Media.FontStyle.Italic;
            block.Text = baseLabel + (evt.Detail is null ? "…" : $" {evt.Detail}…");
        }
        else
        {
            block.FontStyle = Avalonia.Media.FontStyle.Normal;
            var ms = evt.Duration.TotalMilliseconds;
            var msPart = ms > 0 ? $" · {ms:F0}ms" : "";
            block.Text = $"{baseLabel} {evt.Detail ?? "ok"}{msPart}";
        }
    }

    private void OnExtractionStatusClicked(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        => ShowExtractionDetails();

    /// <summary>
    /// `--shot URL OUTPUT.png` flow: load the URL via the existing LoadWebPage
    /// path, wait the configured ms (default 30 s) for image cache + re-render,
    /// capture the window via the same harness API the UI tests use, then
    /// Shutdown. The window is positioned off-screen + ShowActivated=false so
    /// it never steals focus on the user's actual workspace.
    /// </summary>
    private async Task RunAutoShotAsync(string url, string outPath)
    {
        try
        {
            await LoadWebPage(url);
            await Task.Delay(MarkdownViewer.FullProgram.AutoShotWaitMs);
            await Mostlylucid.Avalonia.UITesting.Players.ScreenshotCapture.CaptureWindowAsync(this, outPath);
            Console.WriteLine($"shot saved: {outPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"shot failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
    }

    private void ShowExtractionDetails()
    {
        try
        {
            _ = new MarkdownViewer.Views.ExtractionDetailsPanel().ShowDialog(this);
        }
        catch (Exception ex)
        {
            var crashPath = Path.Combine(MarkdownViewer.AppPaths.LocalState, "crash.log");
            File.AppendAllText(crashPath,
                $"[{DateTime.Now:O}] ShowExtractionDetails NRE: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\n\n");
            throw;
        }
    }
#else
    private void OnExtractionStatusClicked(object? sender, Avalonia.Input.PointerPressedEventArgs e) { }
#endif

    #endregion

    #region Help

    private void OnOpenHelp(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = OpenHelp();
    }

    private async Task OpenHelp()
    {
        var exePath = AppContext.BaseDirectory;
        var readmePath = Path.Combine(exePath, "README.md");

        if (File.Exists(readmePath))
        {
            await LoadFile(readmePath);
        }
        else
        {
            var devPath = Path.Combine(exePath, "..", "..", "..", "..", "README.md");
            if (File.Exists(devPath))
                await LoadFile(Path.GetFullPath(devPath));
            else
                StatusText.Text = "README.md not found";
        }
    }

    private void OnOpenUserManual(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = OpenUserManual();
    }

    private async Task OpenUserManual()
    {
        var exePath = AppContext.BaseDirectory;
        var manualPath = Path.Combine(exePath, "manual", "user-manual.md");

        if (File.Exists(manualPath))
        {
            await LoadFile(manualPath);
            return;
        }

        // Dev fallback — when running from `dotnet run`, the manual lives in the source tree.
        var devPath = Path.Combine(exePath, "..", "..", "..", "Assets", "manual", "user-manual.md");
        if (File.Exists(devPath))
        {
            await LoadFile(Path.GetFullPath(devPath));
            return;
        }

        StatusText.Text = "User manual not found";
    }

    #endregion

    #region Mermaid Diagram Export

    private async void OnRenderMermaid(object? sender, RoutedEventArgs e)
    {
        try
        {
            CloseSidePanel();
            await ShowMermaidExportDialog();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Diagram export error: {ex.Message}";
        }
    }

    private async Task ShowMermaidExportDialog()
    {
        var diagrams = _markdownService.MermaidDiagrams;
        if (diagrams.Count == 0)
        {
            StatusText.Text = "No mermaid diagrams in current document";
            return;
        }
        // Surface immediate feedback BEFORE opening the picker so the user
        // sees confirmation that the click registered, even if the picker
        // takes a moment to render or appears as a sheet on a different
        // screen. Without this, the click feels like a dead button.
        StatusText.Text = $"Opening export dialog ({diagrams.Count} diagram{(diagrams.Count == 1 ? "" : "s")})...";

        // If only one diagram, export it directly
        if (diagrams.Count == 1)
        {
            var (_, mermaidCode) = diagrams.First();
            await ExportMermaidDiagram(mermaidCode);
            return;
        }

        // Multiple diagrams — export all, or let user choose via numbered file names
        var diagramList = diagrams.Values.ToList();
        await ExportAllMermaidDiagrams(diagramList);
    }

    private async Task ExportMermaidDiagram(string mermaidCode)
    {
        if (_filePickerOpen)
        {
            StatusText.Text = "Another file picker is already open";
            return;
        }
        _filePickerOpen = true;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Diagram",
                SuggestedFileName = "diagram",
                DefaultExtension = "svg",
                FileTypeChoices =
                [
                    new FilePickerFileType("SVG Image") { Patterns = ["*.svg"] },
                    new FilePickerFileType("PNG Image") { Patterns = ["*.png"] }
                ]
            });

            if (file is null)
            {
                StatusText.Text = "Diagram export canceled";
                return;
            }

            var outputPath = file.Path.LocalPath;
            try
            {
                if (outputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    var pngBytes = _markdownService.ExportMermaidToPngBytes(mermaidCode, 3f);
                    await File.WriteAllBytesAsync(outputPath, pngBytes);
                }
                else
                {
                    var svg = _markdownService.ExportMermaidToSvg(mermaidCode);
                    await File.WriteAllTextAsync(outputPath, svg);
                }
                StatusText.Text = $"Diagram saved: {Path.GetFileName(outputPath)}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Export error: {ex.Message}";
            }
        }
        finally
        {
            _filePickerOpen = false;
        }
    }

    private async Task ExportAllMermaidDiagrams(List<string> diagrams)
    {
        if (_filePickerOpen) return;
        _filePickerOpen = true;
        try
        {
        var folder = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Export All Diagrams — Choose Folder" });

        if (folder.Count == 0) return;

        var outputDir = folder[0].Path.LocalPath;
        var exported = 0;
        for (var i = 0; i < diagrams.Count; i++)
        {
            try
            {
                var svgPath = Path.Combine(outputDir, $"diagram-{i + 1}.svg");
                var svg = _markdownService.ExportMermaidToSvg(diagrams[i]);
                await File.WriteAllTextAsync(svgPath, svg);

                var pngPath = Path.Combine(outputDir, $"diagram-{i + 1}.png");
                var pngBytes = _markdownService.ExportMermaidToPngBytes(diagrams[i], 3f);
                await File.WriteAllBytesAsync(pngPath, pngBytes);

                exported++;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error exporting diagram {i + 1}: {ex.Message}";
            }
        }

        StatusText.Text = $"Exported {exported} diagrams (SVG + PNG) to {outputDir}";
        }
        finally
        {
            _filePickerOpen = false;
        }
    }

    private async Task SaveDiagramAs(string mermaidCode, string format)
    {
        if (_filePickerOpen) return;
        _filePickerOpen = true;
        try
        {
        var ext = format.ToLowerInvariant();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Save Diagram as {ext.ToUpperInvariant()}",
            SuggestedFileName = $"diagram.{ext}",
            DefaultExtension = ext,
            FileTypeChoices =
            [
                ext == "svg"
                    ? new FilePickerFileType("SVG Image") { Patterns = ["*.svg"] }
                    : new FilePickerFileType("PNG Image") { Patterns = ["*.png"] }
            ]
        });

        if (file is null) return;

        var outputPath = file.Path.LocalPath;
        try
        {
            if (ext == "png")
            {
                var pngBytes = _markdownService.ExportMermaidToPngBytes(mermaidCode, 3f);
                await File.WriteAllBytesAsync(outputPath, pngBytes);
            }
            else
            {
                var svg = _markdownService.ExportMermaidToSvg(mermaidCode);
                await File.WriteAllTextAsync(outputPath, svg);
            }
            StatusText.Text = $"Diagram saved: {Path.GetFileName(outputPath)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export error: {ex.Message}";
        }
        }
        finally
        {
            _filePickerOpen = false;
        }
    }

    private void ShowDiagramPopup(string imagePath, string? mermaidCode)
    {
        var popup = new Window
        {
            Title = "Diagram Viewer — lucidVIEW",
            Width = 900,
            Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = this.Background
        };

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        var image = new Avalonia.Controls.Image
        {
            Stretch = Avalonia.Media.Stretch.None,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        try
        {
            var bitmap = new Avalonia.Media.Imaging.Bitmap(imagePath);
            image.Source = bitmap;
        }
        catch
        {
            return;
        }

        // Context menu with save options
        var contextMenu = new ContextMenu();
        var savePng = new MenuItem { Header = "Save as PNG..." };
        var saveSvg = new MenuItem { Header = "Save as SVG..." };
        savePng.Click += (_, _) =>
        {
            if (mermaidCode != null) _ = SaveDiagramAs(mermaidCode, "png");
        };
        saveSvg.Click += (_, _) =>
        {
            if (mermaidCode != null) _ = SaveDiagramAs(mermaidCode, "svg");
        };
        contextMenu.Items.Add(savePng);
        contextMenu.Items.Add(saveSvg);

        // Zoom controls
        var zoomLevel = 1.0;
        var layoutTransform = new LayoutTransformControl { Child = image };
        scrollViewer.Content = layoutTransform;

        popup.KeyDown += (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Escape) popup.Close();
            if (args.Key == Avalonia.Input.Key.Add || args.Key == Avalonia.Input.Key.OemPlus)
            {
                zoomLevel = Math.Min(zoomLevel + 0.25, 5.0);
                layoutTransform.LayoutTransform = new ScaleTransform(zoomLevel, zoomLevel);
            }
            if (args.Key == Avalonia.Input.Key.Subtract || args.Key == Avalonia.Input.Key.OemMinus)
            {
                zoomLevel = Math.Max(zoomLevel - 0.25, 0.25);
                layoutTransform.LayoutTransform = new ScaleTransform(zoomLevel, zoomLevel);
            }
            if (args.Key == Avalonia.Input.Key.D0 || args.Key == Avalonia.Input.Key.NumPad0)
            {
                zoomLevel = 1.0;
                layoutTransform.LayoutTransform = null;
            }
        };

        var dockPanel = new DockPanel();

        // Bottom bar with zoom info and save buttons
        var bottomBar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(8)
        };

        if (mermaidCode != null)
        {
            var savePngBtn = new Button { Content = "Save PNG", Margin = new Thickness(4) };
            var saveSvgBtn = new Button { Content = "Save SVG", Margin = new Thickness(4) };
            savePngBtn.Click += (_, _) => _ = SaveDiagramAs(mermaidCode, "png");
            saveSvgBtn.Click += (_, _) => _ = SaveDiagramAs(mermaidCode, "svg");
            bottomBar.Children.Add(savePngBtn);
            bottomBar.Children.Add(saveSvgBtn);
        }

        var zoomLabel = new Avalonia.Controls.TextBlock
        {
            Text = "Zoom: +/- keys, 0 to reset, Esc to close",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            Foreground = Avalonia.Media.Brushes.Gray,
            FontSize = 12
        };
        bottomBar.Children.Add(zoomLabel);

        DockPanel.SetDock(bottomBar, Avalonia.Controls.Dock.Bottom);
        dockPanel.Children.Add(bottomBar);
        dockPanel.Children.Add(scrollViewer);

        image.ContextMenu = contextMenu;
        popup.Content = dockPanel;
        popup.Show(this);
    }

    #endregion

    #region Heading Navigation

    private void OnHeadingClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is HeadingItem heading)
        {
            // Switch to preview tab
            PreviewTab.IsChecked = true;
            RenderedPanel.IsVisible = true;
            RawScroller.IsVisible = false;

            // Scroll to heading by searching for it in the rendered content
            ScrollToHeading(heading);
            CloseSidePanel();
        }
    }

    private void ScrollToHeading(HeadingItem heading)
    {
        // First try to find the heading element in the visual tree
        var headingElement = FindHeadingElement(MdViewer, heading.Text);
        if (headingElement != null)
        {
            // Get the position of the element relative to the scroll viewer
            var transform = headingElement.TransformToVisual(RenderedScroller);
            if (transform != null)
            {
                var point = transform.Value.Transform(new Point(0, 0));
                var newOffset = RenderedScroller.Offset.Y + point.Y - 20; // 20px padding from top
                RenderedScroller.Offset = new Vector(0, Math.Max(0, newOffset));
                return;
            }
        }

        // Fallback: estimate scroll position based on line number
        // Calculate approximate position using total content height and line ratio
        var totalLines = _rawContent.Split('\n').Length;
        if (totalLines > 0)
        {
            var lineRatio = (double)heading.Line / totalLines;
            var maxScroll = Math.Max(0, RenderedScroller.Extent.Height - RenderedScroller.Viewport.Height);
            var targetOffset = lineRatio * maxScroll;
            RenderedScroller.Offset = new Vector(0, targetOffset);
        }
    }

    private void ReplaceDiagramMarkers()
    {
        _diagramPluginHost.ReplaceDiagramMarkers(MdViewer);
    }

    private void ScheduleDiagramMarkerReplacement()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ReplaceDiagramMarkers();

            _diagramReplacementAttempts = 0;
            _diagramReplacementTimer ??= new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };

            _diagramReplacementTimer.Tick -= OnDiagramReplacementTick;
            _diagramReplacementTimer.Tick += OnDiagramReplacementTick;
            _diagramReplacementTimer.Stop();
            _diagramReplacementTimer.Start();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnDiagramReplacementTick(object? sender, EventArgs e)
    {
        ReplaceDiagramMarkers();
        _diagramReplacementAttempts++;

        if (_diagramReplacementAttempts < MaxDiagramReplacementAttempts)
            return;

        if (_diagramReplacementTimer is not null)
        {
            _diagramReplacementTimer.Stop();
            _diagramReplacementTimer.Tick -= OnDiagramReplacementTick;
        }
    }

    private void ScrollToDiagram(string diagramKey)
    {
        if (!_markdownService.DiagramDocuments.TryGetValue(diagramKey, out var targetDoc))
            return;

        var targetCanvas = FindVisualDescendant<MarkdownViewer.Controls.DiagramCanvas>(
            MdViewer, dc => dc.Document == targetDoc);
        if (targetCanvas is null) return;

        var transform = targetCanvas.TransformToVisual(RenderedScroller);
        if (transform is null) return;

        var point = transform.Value.Transform(new Point(0, 0));
        RenderedScroller.Offset = new Vector(0, Math.Max(0, RenderedScroller.Offset.Y + point.Y - 20));
    }

    private static T? FindVisualDescendant<T>(Visual root, Func<T, bool> predicate) where T : Visual
    {
        foreach (var child in VisualExtensions.GetVisualChildren(root))
        {
            if (child is T match && predicate(match))
                return match;

            if (child is Visual visual)
            {
                var result = FindVisualDescendant(visual, predicate);
                if (result is not null)
                    return result;
            }
        }
        return null;
    }

    private IBrush? ResolveDiagramTextBrush()
    {
        var themeTextColor = ThemeColors.GetTheme(_effectiveTheme).Text;
        if (Color.TryParse(themeTextColor, out var parsedColor))
            return new Avalonia.Media.Immutable.ImmutableSolidColorBrush(parsedColor);
        return null;
    }

    private static bool IsLightTheme(AppTheme theme) =>
        theme == AppTheme.Light || theme == AppTheme.MostlyLucidLight;

    private static Control? FindHeadingElement(Visual parent, string headingText)
    {
        foreach (var child in VisualExtensions.GetVisualChildren(parent))
        {
            if (child is TextBlock textBlock)
            {
                var text = textBlock.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(text) &&
                    text.Equals(headingText, StringComparison.OrdinalIgnoreCase))
                    return textBlock;
            }

            if (child is Visual visual)
            {
                var result = FindHeadingElement(visual, headingText);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    #endregion

    #region Drag and Drop

#pragma warning disable CS0618 // DragEventArgs.Data is obsolete - new DataTransfer API has different interface
    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files)) DropOverlay.IsVisible = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        try
        {
            if (!e.Data.Contains(DataFormats.Files)) return;

            var files = e.Data.GetFiles()?.ToList();
            if (files is null || files.Count == 0) return;

            var path = files[0].Path.LocalPath;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".md" or ".markdown" or ".mdown" or ".mkd" or ".txt") await LoadFile(path);
        }
        catch (Exception ex) when (!IsIgnorableError(ex))
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }
#pragma warning restore CS0618

    #endregion

    #region File Activation (macOS / Linux Open With)

    private void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (!IsLoaded) return;
        if (e is not FileActivatedEventArgs fileArgs || fileArgs.Files is null) return;

        foreach (var storageFile in fileArgs.Files)
        {
            var path = storageFile?.Path?.LocalPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadFile(path));
                break;
            }
        }
    }

    #endregion

}
