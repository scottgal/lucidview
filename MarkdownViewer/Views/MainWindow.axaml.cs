using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using LiveMarkdown.Avalonia;
using MarkdownViewer.Controls;
using MarkdownViewer.Models;
using MarkdownViewer.Services;
using MermaidSharp.Rendering;
using Microsoft.Playwright;
using SkiaSharp;
using VisualExtensions = Avalonia.VisualTree.VisualExtensions;

namespace MarkdownViewer.Views;

public partial class MainWindow : Window
{
    private readonly ObservableStringBuilder _markdownBuilder = new();
    private readonly ImageCacheService _imageCacheService;
    private readonly MarkdownService _markdownService;
    private readonly NavigationService _navigationService;
    private readonly PaginationService _paginationService;
    private readonly SearchService _searchService;
    private readonly AppSettings _settings;
    private readonly ThemeService _themeService;
    private string? _currentFilePath;
    private int _currentSearchIndex = -1;
    private int _fontSize = 16;
    private List<Style> _codeBlockStyles = [];
    private Style? _fontStyle;
    private List<HeadingItem> _headings = [];
    private bool _isSidePanelOpen;
    private string _rawContent = string.Empty;
    private List<SearchResult> _searchResults = [];

    /// <summary>
    /// Known harmless errors from third-party libraries that should not be shown to users
    /// </summary>
    private static readonly string[] IgnorableErrors =
    [
        "Unsupported IBinding implementation",
        "StaticBinding",
        "Markdown.Avalonia.Extensions"
    ];

    /// <summary>
    /// Check if an exception is a known harmless library error
    /// </summary>
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

        // Initialize LiveMarkdown renderer
        MdViewer.MarkdownBuilder = _markdownBuilder;
        MdViewer.ImageBasePath = _markdownService.TempDirectory;
        MdViewer.LinkClick += OnLinkClick;
        _navigationService = new NavigationService();
        _themeService = new ThemeService(Application.Current!);
        _searchService = new SearchService();
        _paginationService = new PaginationService();

        // Restore saved size, clamped to sensible defaults
        Width = _settings.WindowWidth is > 0 and < 10000 ? _settings.WindowWidth : 1100;
        Height = _settings.WindowHeight is > 0 and < 10000 ? _settings.WindowHeight : 750;
        _fontSize = _settings.FontSize > 0 ? (int)_settings.FontSize : 16;

        DataContext = this;

        // Apply saved theme (needed before showing content)
        ApplyTheme(_settings.Theme);
        UpdateThemeCardSelection(_settings.Theme);

        // Drag and drop
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Mouse wheel zoom (intercept even handled events for Ctrl+wheel)
        RenderedScroller.AddHandler(PointerWheelChangedEvent, OnMarkdownPointerWheelChanged, RoutingStrategies.Tunnel,
            true);

        UpdateRecentFiles();
        UpdateFontSizeDisplay();
        Closing += OnWindowClosing;

        // Command line argument - load file immediately
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1])) _ = LoadFile(args[1]);

        // Defer non-critical startup work until after window is shown
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            SetWindowIcon();
            ApplyTypography();
        }, Avalonia.Threading.DispatcherPriority.Background);
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
        _settings.WindowWidth = (int)Width;
        _settings.WindowHeight = (int)Height;
        _settings.Save();
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
    private ICommand? _openUrlCommand;
    private ICommand? _openSettingsCommand;
    private ICommand? _toggleFullScreenCommand;
    private ICommand? _toggleSidePanelCommand;
    private ICommand? _toggleSearchCommand;
    private ICommand? _escapeCommand;
    private ICommand? _fontSizeIncreaseCommand;
    private ICommand? _fontSizeDecreaseCommand;
    private ICommand? _printCommand;
    private ICommand? _openHelpCommand;
    private ICommand? _debugScreenshotCommand;
    private ICommand? _exitCommand;

    public ICommand OpenFileCommand => _openFileCommand ??= new RelayCommand(async () => await OpenFile());
    public ICommand OpenUrlCommand => _openUrlCommand ??= new RelayCommand(async () => await OpenUrl());
    public ICommand OpenSettingsCommand => _openSettingsCommand ??= new RelayCommand(async () => await OpenSettings());
    public ICommand ToggleFullScreenCommand => _toggleFullScreenCommand ??= new RelayCommand(ToggleFullScreen);
    public ICommand ToggleSidePanelCommand => _toggleSidePanelCommand ??= new RelayCommand(ToggleSidePanel);
    public ICommand ToggleSearchCommand => _toggleSearchCommand ??= new RelayCommand(ToggleSearch);
    public ICommand EscapeCommand => _escapeCommand ??= new RelayCommand(OnEscape);
    public ICommand FontSizeIncreaseCommand => _fontSizeIncreaseCommand ??= new RelayCommand(IncreaseFontSize);
    public ICommand FontSizeDecreaseCommand => _fontSizeDecreaseCommand ??= new RelayCommand(DecreaseFontSize);
    public ICommand PrintCommand => _printCommand ??= new RelayCommand(async () => await Print());
    public ICommand OpenHelpCommand => _openHelpCommand ??= new RelayCommand(async () => await OpenHelp());
    public ICommand DebugScreenshotCommand => _debugScreenshotCommand ??= new RelayCommand(async () => await DebugScreenshot());
    public ICommand ExitCommand => _exitCommand ??= new RelayCommand(() => Close());

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

    #region File Operations

    private void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = OpenFile();
    }

    private void OnOpenUrl(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = OpenUrl();
    }

    private void OnMostlyLucidClick(object? sender, RoutedEventArgs e)
    {
        OpenBrowserUrl("https://www.mostlylucid.net");
    }

    private void OnLinkClick(object? sender, LinkClickedEventArgs e)
    {
        var href = e.HRef;
        if (href == null) return;

        var url = href.ToString();
        if (string.IsNullOrWhiteSpace(url)) return;

        // Anchor links: scroll within document
        if (url.StartsWith('#'))
        {
            var anchorText = Uri.UnescapeDataString(url[1..]).Replace("-", " ");
            var heading = _headings
                .SelectMany(h => FlattenHeadings([h]))
                .FirstOrDefault(h => h.Text.Equals(anchorText, StringComparison.OrdinalIgnoreCase)
                    || h.Text.Replace(" ", "-").Equals(url[1..], StringComparison.OrdinalIgnoreCase));
            if (heading != null)
            {
                ScrollToHeading(heading);
            }
            e.Handled = true;
            return;
        }

        // HTTP(S) links: open in default browser
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            (href.IsAbsoluteUri && href.Scheme is "http" or "https"))
        {
            OpenBrowserUrl(href.IsAbsoluteUri ? href.AbsoluteUri : url);
            e.Handled = true;
            return;
        }

        // Resolve relative paths against current file's directory
        var resolvedPath = TryResolveLocalPath(url);
        if (resolvedPath != null)
        {
            var ext = Path.GetExtension(resolvedPath).ToLowerInvariant();
            if (ext is ".md" or ".markdown" or ".mdown" or ".mkd" or ".txt")
            {
                _ = LoadFile(resolvedPath);
                e.Handled = true;
                return;
            }

            // Other local files: open with default app
            OpenBrowserUrl(resolvedPath);
            e.Handled = true;
            return;
        }

        // file:// URIs
        if (href.IsAbsoluteUri && href.Scheme == "file")
        {
            var localPath = href.LocalPath;
            var ext = Path.GetExtension(localPath).ToLowerInvariant();
            if (ext is ".md" or ".markdown" or ".mdown" or ".mkd" or ".txt")
            {
                _ = LoadFile(localPath);
                e.Handled = true;
                return;
            }
        }

        // Fallback: try to open in browser
        OpenBrowserUrl(url);
        e.Handled = true;
    }

    private string? TryResolveLocalPath(string relativePath)
    {
        if (_currentFilePath == null) return null;

        var dir = Path.GetDirectoryName(_currentFilePath);
        if (dir == null) return null;

        // Handle relative paths like ./other.md or ../other.md or other.md
        var candidate = Path.GetFullPath(Path.Combine(dir, relativePath));
        return File.Exists(candidate) ? candidate : null;
    }

    private void OpenBrowserUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private async Task OpenFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Markdown File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Markdown Files")
                    { Patterns = ["*.md", "*.markdown", "*.mdown", "*.mkd", "*.txt"] },
                new FilePickerFileType("All Files") { Patterns = ["*.*"] }
            ]
        });

        if (files.Count > 0) await LoadFile(files[0].Path.LocalPath);
    }

    private async Task LoadFile(string path)
    {
        try
        {
            StatusText.Text = $"Loading {Path.GetFileName(path)}...";

            var content = await File.ReadAllTextAsync(path);
            var basePath = Path.GetDirectoryName(path);
            _markdownService.SetBasePath(basePath);

            // Display content immediately for fast response
            await DisplayMarkdown(content);

            _currentFilePath = path;
            Title = $"{Path.GetFileName(path)} - lucidVIEW";
            EnableDocumentControls(true);
            _settings.AddRecentFile(path);
            UpdateRecentFiles();

            var fileInfo = new FileInfo(path);
            var wordCount = CountWords(content);

            StatusText.Text = path;
            FileDateText.Text = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            WordCountText.Text = $"{wordCount:N0} words";
            FileInfoText.Text = $"{fileInfo.Length:N0} bytes";

            // Cache remote images in background, then refresh
            var imageUrls = _markdownService.ExtractImageUrls(content);
            if (imageUrls.Count > 0)
            {
                _ = CacheImagesAndRefreshAsync(content, imageUrls);
            }
        }
        catch (Exception ex) when (!IsIgnorableError(ex))
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        catch
        {
            // Ignore known library errors (e.g., StaticBinding from Markdown.Avalonia)
        }
    }

    /// <summary>
    /// Cache images in background and refresh display when done
    /// </summary>
    private async Task CacheImagesAndRefreshAsync(string content, List<string> imageUrls)
    {
        try
        {
            await _imageCacheService.PreCacheImagesAsync(imageUrls);
            // Refresh markdown to use cached images
            await DisplayMarkdown(content);
        }
        catch (Exception ex) when (IsIgnorableError(ex))
        {
            // Ignore known library errors
        }
        catch
        {
            // Ignore caching errors - original URLs still work
        }
    }

    private async Task OpenUrl()
    {
        var dialog = new InputDialog("Open URL", "Enter the URL of a markdown file:");
        var result = await dialog.ShowDialog<string?>(this);

        if (!string.IsNullOrWhiteSpace(result)) await LoadFromUrl(result);
    }

    private async Task LoadFromUrl(string url)
    {
        try
        {
            StatusText.Text = "Downloading...";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("lucidVIEW/1.0");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/markdown");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/x-markdown;q=0.95");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/plain;q=0.9");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/*;q=0.8");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("*/*;q=0.5");
            var content = await httpClient.GetStringAsync(url);

            var uri = new Uri(url);
            var baseUrl = $"{uri.Scheme}://{uri.Host}{string.Join("", uri.Segments.Take(uri.Segments.Length - 1))}";
            _markdownService.SetBaseUrl(baseUrl);

            // Display content immediately for fast response
            await DisplayMarkdown(content);

            _currentFilePath = url;
            Title = $"{uri.Segments.LastOrDefault()?.TrimEnd('/') ?? "Remote"} - lucidVIEW";
            EnableDocumentControls(true);
            var wordCount = CountWords(content);

            StatusText.Text = url;
            FileDateText.Text = "Remote";
            WordCountText.Text = $"{wordCount:N0} words";
            FileInfoText.Text = $"{content.Length:N0} chars";

            // Cache remote images in background, then refresh
            var imageUrls = _markdownService.ExtractImageUrls(content);
            if (imageUrls.Count > 0)
            {
                _ = CacheImagesAndRefreshAsync(content, imageUrls);
            }
        }
        catch (Exception ex) when (!IsIgnorableError(ex))
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        catch
        {
            // Ignore known library errors
        }
    }

    private async Task DisplayMarkdown(string content)
    {
        _rawContent = content;

        // Extract headings for navigation
        _headings = _navigationService.ExtractHeadings(content);

        // Extract and display metadata (categories, publication date)
        var metadata = _markdownService.ExtractMetadata(content);
        DisplayMetadata(metadata);

        // Phase 1: Fast path — text processing + cached mermaid diagrams
        var (processed, pendingDiagrams) = _markdownService.ProcessMarkdownFast(content);

        // Show content immediately (with placeholders for uncached diagrams)
        _markdownBuilder.Clear();
        _markdownBuilder.Append(processed);
        RawTextBlock.Text = content;

        WelcomePanel.IsVisible = false;
        ContentGrid.IsVisible = true;
        UpdateToc();

        PreviewTab.IsChecked = true;
        RenderedPanel.IsVisible = true;
        RawScroller.IsVisible = false;

        var estimatedHeight = content.Split('\n').Length * 24.0;
        _paginationService.CalculatePages(estimatedHeight);

        // Replace flowchart marker text with native FlowchartCanvas controls
        // Defer to allow the visual tree to build first
        Avalonia.Threading.Dispatcher.UIThread.Post(ReplaceDiagramMarkers, Avalonia.Threading.DispatcherPriority.Loaded);

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

                // Save scroll position and refresh
                var scrollOffset = RenderedScroller.Offset;
                _markdownBuilder.Clear();
                _markdownBuilder.Append(updated);
                RenderedScroller.Offset = scrollOffset;

                // Re-run flowchart replacement after pending diagrams update
                Avalonia.Threading.Dispatcher.UIThread.Post(ReplaceDiagramMarkers, Avalonia.Threading.DispatcherPriority.Loaded);
            }
            catch (OperationCanceledException)
            {
                // New file/theme switch cancelled this batch — that's fine
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

    #endregion

    #region Recent Files

    private void UpdateRecentFiles()
    {
        RecentFilesList.ItemsSource = _settings.RecentFiles.Take(10).ToList();
    }

    private async void OnRecentFileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path)
        {
            CloseSidePanel();
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                await LoadFromUrl(path);
            else
                await LoadFile(path);
        }
    }

    #endregion

    #region Theme

    private void ApplyTheme(AppTheme theme)
    {
        _themeService.ApplyTheme(theme);
        _settings.Theme = theme;
        UpdatePanelOverlay(theme);

        // Update markdown service for theme-aware mermaid rendering
        var isDark = theme != AppTheme.Light && theme != AppTheme.MostlyLucidLight;
        var themeDefinition = ThemeColors.GetTheme(theme);
        _markdownService.SetThemeColors(isDark, themeDefinition.Text, themeDefinition.Background);

        // Invalidate mermaid cache since theme colors changed
        _markdownService.InvalidateMermaidCache();

        // Refresh current document to regenerate mermaid diagrams with new theme colors
        if (!string.IsNullOrEmpty(_rawContent)) _ = DisplayMarkdown(_rawContent);
    }

    private void UpdatePanelOverlay(AppTheme theme)
    {
        // Light themes get dark overlay, dark themes get light overlay
        var isLightTheme = theme == AppTheme.Light || theme == AppTheme.MostlyLucidLight;
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
        }
    }

    private void OnThemeCardClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string themeName)
            if (Enum.TryParse<AppTheme>(themeName, out var theme))
            {
                ApplyTheme(theme);
                UpdateThemeCardSelection(theme);
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

        var codeFont = new FontFamily(_settings.CodeFontFamily);
        RawTextBlock.FontFamily = codeFont;
        RawTextBlock.FontSize = _settings.CodeFontSize;
        ApplyCodeBlockStyle(codeFont, _settings.CodeFontSize);

        _fontSize = _settings.FontSize > 0 ? (int)_settings.FontSize : 16;
        ApplyFontSize();

        // Force content refresh if we have content loaded
        if (!string.IsNullOrEmpty(_rawContent))
        {
            // Clear and re-append to trigger re-render with new font
            var content = _rawContent;
            _markdownBuilder.Clear();
            var processed = _markdownService.ProcessMarkdown(content);
            _markdownBuilder.Append(processed);
        }
    }

    private void ApplyCodeBlockStyle(FontFamily codeFont, double codeFontSize)
    {
        // Remove existing styles
        foreach (var style in _codeBlockStyles)
        {
            MdViewer.Styles.Remove(style);
        }
        _codeBlockStyles.Clear();

        // Use a simpler monospace font for language labels (sans-serif style)
        var labelFont = new FontFamily("Segoe UI, Arial, sans-serif");

        // Style 1: CodeBlock container
        var codeBlockStyle = new Style(x => x.OfType<CodeBlock>())
        {
            Setters =
            {
                new Setter(TextElement.FontFamilyProperty, codeFont),
                new Setter(TextElement.FontSizeProperty, codeFontSize)
            }
        };

        // Style 2: ALL TextBlocks inside CodeBlock - covers language label AND code
        var textBlockInCodeStyle = new Style(x => x.OfType<CodeBlock>().Descendant().OfType<TextBlock>())
        {
            Setters =
            {
                new Setter(TextBlock.FontFamilyProperty, codeFont),
                new Setter(TextBlock.FontSizeProperty, codeFontSize)
            }
        };

        // Style 3: SelectableTextBlock inside CodeBlock (for selectable code)
        var selectableInCodeStyle = new Style(x => x.OfType<CodeBlock>().Descendant().OfType<SelectableTextBlock>())
        {
            Setters =
            {
                new Setter(SelectableTextBlock.FontFamilyProperty, codeFont),
                new Setter(SelectableTextBlock.FontSizeProperty, codeFontSize)
            }
        };

        // Style 4: Border child TextBlock (language label) - use sans-serif not monospace
        var languageLabelStyle = new Style(x => x.OfType<CodeBlock>().Child().OfType<Border>().Descendant().OfType<TextBlock>())
        {
            Setters =
            {
                new Setter(TextBlock.FontFamilyProperty, labelFont),
                new Setter(TextBlock.FontSizeProperty, 12.0),
                new Setter(TextBlock.FontStyleProperty, FontStyle.Normal),
                new Setter(TextBlock.FontWeightProperty, FontWeight.Normal)
            }
        };

        // Style 5: InlineUIContainer for inline code (`code`)
        var inlineContainerStyle = new Style(x => x.OfType<InlineUIContainer>().Descendant().OfType<TextBlock>())
        {
            Setters =
            {
                new Setter(TextBlock.FontFamilyProperty, codeFont)
            }
        };

        // Add all styles and track them - ORDER MATTERS for specificity
        _codeBlockStyles.Add(codeBlockStyle);
        _codeBlockStyles.Add(textBlockInCodeStyle);
        _codeBlockStyles.Add(selectableInCodeStyle);
        _codeBlockStyles.Add(languageLabelStyle);  // More specific, added last
        _codeBlockStyles.Add(inlineContainerStyle);

        foreach (var style in _codeBlockStyles)
        {
            MdViewer.Styles.Add(style);
        }
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
            UpdateThemeCardSelection(_settings.Theme);
            ApplyTypography();
        };

        await dialog.ShowDialog(this);

        // Final apply in case dialog was closed without save
        ApplyTheme(_settings.Theme);
        UpdateThemeCardSelection(_settings.Theme);
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

    #region Page Navigation

    private void OnPreviousPage(object? sender, RoutedEventArgs e)
    {
        if (_paginationService.PreviousPage()) ScrollToCurrentPage();
        // Pagination removed
    }

    private void OnNextPage(object? sender, RoutedEventArgs e)
    {
        if (_paginationService.NextPage()) ScrollToCurrentPage();
        // Pagination removed
    }

    private void ScrollToCurrentPage()
    {
        var offset = _paginationService.GetScrollOffsetForPage(_paginationService.CurrentPage);
        RenderedScroller.Offset = new Vector(0, offset);
    }

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

    #endregion

    #region Context Menu & Clipboard

    private async void OnCopyText(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard != null && !string.IsNullOrEmpty(_rawContent)) await clipboard.SetTextAsync(_rawContent);
        }
        catch
        {
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
                var html = ConvertMarkdownToHtml(_markdownService.ProcessMarkdown(_rawContent));
                await clipboard.SetTextAsync(html);
            }
        }
        catch
        {
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

        // Check all mermaid diagrams — if there's at least one, show the options
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

        _searchResults = _searchService.Search(_rawContent, query);
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

    #region Print

    private void OnPrint(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = Print();
    }

    private async Task Print()
    {
        if (string.IsNullOrEmpty(_rawContent))
        {
            StatusText.Text = "No document to export";
            return;
        }

        try
        {
            var suggestedName = Path.GetFileNameWithoutExtension(_currentFilePath ?? "document");
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export PDF",
                SuggestedFileName = $"{suggestedName}.pdf",
                DefaultExtension = "pdf",
                FileTypeChoices =
                [
                    new FilePickerFileType("PDF Document") { Patterns = ["*.pdf"] }
                ]
            });

            if (file is null)
            {
                StatusText.Text = "PDF export canceled";
                return;
            }

            var outputPath = file.Path.LocalPath;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                StatusText.Text = "Unable to resolve output path";
                return;
            }

            StatusText.Text = "Exporting PDF...";

            // Generate HTML for printing (cross-platform approach)
            var html = GeneratePrintHtml(_rawContent);

            // Render via headless Chromium and write PDF.
            var tempPath = Path.Combine(Path.GetTempPath(), $"lucidview_export_{Guid.NewGuid():N}.html");
            await File.WriteAllTextAsync(tempPath, html);

            try
            {
                await ExportPdfFromHtmlAsync(tempPath, outputPath);
                StatusText.Text = $"PDF saved: {outputPath}";
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }
        catch (PlaywrightException ex) when (IsMissingPlaywrightBrowser(ex))
        {
            StatusText.Text = "Chromium missing for PDF export. Run playwright install chromium.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"PDF export error: {ex.Message}";
        }
    }

    private static async Task ExportPdfFromHtmlAsync(string htmlPath, string outputPath)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        var page = await browser.NewPageAsync();
        var htmlUri = new Uri(htmlPath);

        await page.GotoAsync(htmlUri.AbsoluteUri, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });

        await page.PdfAsync(new PagePdfOptions
        {
            Path = outputPath,
            Format = "A4",
            PrintBackground = true,
            Margin = new Margin
            {
                Top = "12mm",
                Right = "12mm",
                Bottom = "12mm",
                Left = "12mm"
            }
        });
    }

    private static bool IsMissingPlaywrightBrowser(PlaywrightException ex)
    {
        return ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("download new browsers", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("playwright install", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignore cleanup failures in temp directory.
        }
    }

    private string GeneratePrintHtml(string markdown)
    {
        var processed = _markdownService.ProcessMarkdown(markdown);

        // Get theme colors for print
        var isDark = _settings.Theme != AppTheme.Light;
        var bgColor = isDark ? "#1e1e1e" : "#ffffff";
        var textColor = isDark ? "#d4d4d4" : "#1a1a1a";
        var codeColor = isDark ? "#2d2d2d" : "#f5f5f5";

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>{Path.GetFileName(_currentFilePath ?? "Document")} - lucidVIEW</title>
    <style>
        @media print {{
            body {{ background: white !important; color: black !important; }}
            pre, code {{ background: #f5f5f5 !important; }}
        }}
        @media screen {{
            body {{ background: {bgColor}; color: {textColor}; }}
            pre, code {{ background: {codeColor}; }}
        }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif;
            font-size: {_fontSize}px;
            line-height: 1.6;
            max-width: 800px;
            margin: 0 auto;
            padding: 40px;
        }}
        h1, h2, h3, h4, h5, h6 {{ margin-top: 1.5em; margin-bottom: 0.5em; }}
        h1 {{ font-size: 2em; border-bottom: 1px solid #ccc; padding-bottom: 0.3em; }}
        h2 {{ font-size: 1.5em; border-bottom: 1px solid #eee; padding-bottom: 0.3em; }}
        pre {{ padding: 16px; border-radius: 6px; overflow-x: auto; }}
        code {{ padding: 2px 6px; border-radius: 3px; font-family: 'Cascadia Code', 'JetBrains Mono', Consolas, monospace; }}
        pre code {{ padding: 0; }}
        blockquote {{ border-left: 4px solid #58a6ff; margin: 1em 0; padding-left: 1em; color: #666; }}
        table {{ border-collapse: collapse; width: 100%; margin: 1em 0; }}
        th, td {{ border: 1px solid #ddd; padding: 8px 12px; text-align: left; }}
        th {{ background: #f5f5f5; }}
        img {{ max-width: 100%; height: auto; }}
        a {{ color: #58a6ff; }}
        .print-header {{
            text-align: center;
            margin-bottom: 2em;
            padding-bottom: 1em;
            border-bottom: 2px solid #58a6ff;
        }}
        .print-header h1 {{ border: none; margin: 0; }}
        .print-footer {{
            margin-top: 2em;
            padding-top: 1em;
            border-top: 1px solid #ccc;
            text-align: center;
            font-size: 0.8em;
            color: #666;
        }}
        @page {{ margin: 1in; }}
    </style>
</head>
<body>
    <div class=""print-header"">
        <h1>{HttpUtility.HtmlEncode(Path.GetFileName(_currentFilePath ?? "Document"))}</h1>
        <p>Printed from lucidVIEW</p>
    </div>
    <article>
        {ConvertMarkdownToHtml(processed)}
    </article>
    <div class=""print-footer"">
        <p>Generated by lucidVIEW - {DateTime.Now:yyyy-MM-dd HH:mm}</p>
    </div>
</body>
</html>";
    }

    private static string ConvertMarkdownToHtml(string markdown)
    {
        // Basic markdown to HTML conversion for print
        // The rendered markdown is already processed by Markdown.Avalonia
        // This is a simple fallback for HTML output
        var html = markdown;

        // Simple conversions (Markdown.Avalonia handles the actual rendering)
        // This produces reasonable HTML for the print view
        html = Regex.Replace(html, @"^### (.+)$", "<h3>$1</h3>", RegexOptions.Multiline);
        html = Regex.Replace(html, @"^## (.+)$", "<h2>$1</h2>", RegexOptions.Multiline);
        html = Regex.Replace(html, @"^# (.+)$", "<h1>$1</h1>", RegexOptions.Multiline);
        html = Regex.Replace(html, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        html = Regex.Replace(html, @"\*(.+?)\*", "<em>$1</em>");
        html = Regex.Replace(html, @"`(.+?)`", "<code>$1</code>");
        html = Regex.Replace(html, @"^- (.+)$", "<li>$1</li>", RegexOptions.Multiline);
        html = Regex.Replace(html, @"\[([^\]]+)\]\(([^)]+)\)", "<a href=\"$2\">$1</a>");

        // Convert line breaks to paragraphs
        var paragraphs = html.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        html = string.Join("\n", paragraphs.Select(p =>
        {
            if (p.StartsWith("<h") || p.StartsWith("<li") || p.StartsWith("<pre") || p.StartsWith("<ul") ||
                p.StartsWith("<ol"))
                return p;
            return $"<p>{p.Replace("\n", "<br>")}</p>";
        }));

        return html;
    }

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

    #endregion

    #region Mermaid Diagram Export

    private void OnRenderMermaid(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = ShowMermaidExportDialog();
    }

    private async Task ShowMermaidExportDialog()
    {
        var diagrams = _markdownService.MermaidDiagrams;
        if (diagrams.Count == 0)
        {
            StatusText.Text = "No mermaid diagrams in current document";
            return;
        }

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

        if (file is null) return;

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

    private async Task ExportAllMermaidDiagrams(List<string> diagrams)
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

    /// <summary>
    /// Save a specific mermaid diagram by its image path (called from right-click context).
    /// </summary>
    private async Task SaveDiagramAs(string mermaidCode, string format)
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

    /// <summary>
    /// Open a diagram in a popup viewer window at full size.
    /// </summary>
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

    /// <summary>
    /// Walk the visual tree to find flowchart marker text blocks and replace them
    /// with native FlowchartCanvas controls. The markers are text like
    /// "\u200B\u200BFLOWCHART:flowchart-0" inserted by MarkdownService.
    /// </summary>
    private void ReplaceDiagramMarkers()
    {
        var flowchartLayouts = _markdownService.FlowchartLayouts;
        var diagramDocs = _markdownService.DiagramDocuments;
        if (flowchartLayouts.Count == 0 && diagramDocs.Count == 0) return;

        var markers = new List<(TextBlock TextBlock, string Prefix, string Key)>();
        FindDiagramMarkers(MdViewer, markers);

        Debug.WriteLine($"[DiagramCanvas] Found {markers.Count} markers, {flowchartLayouts.Count} flowcharts, {diagramDocs.Count} diagrams");

        foreach (var (textBlock, prefix, key) in markers)
        {
            Control? replacement = null;

            if (prefix == MarkdownService.FlowchartMarkerPrefix)
            {
                var layout = _markdownService.GetFlowchartLayout(key);
                if (layout is null)
                {
                    Debug.WriteLine($"[DiagramCanvas] No flowchart layout for key '{key}'");
                    continue;
                }

                Debug.WriteLine($"[DiagramCanvas] Replacing flowchart '{key}' — {layout.Width:F0}x{layout.Height:F0}");

                var canvas = new FlowchartCanvas
                {
                    Layout = layout,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };

                canvas.LinkClicked += (_, link) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
                    }
                    catch
                    {
                        // Ignore failed link navigation
                    }
                };

                replacement = canvas;
            }
            else if (prefix == MarkdownService.DiagramMarkerPrefix)
            {
                if (!diagramDocs.TryGetValue(key, out var doc))
                {
                    Debug.WriteLine($"[DiagramCanvas] No document for key '{key}'");
                    continue;
                }

                Debug.WriteLine($"[DiagramCanvas] Replacing diagram '{key}' — {doc.Width:F0}x{doc.Height:F0}");

                var themeTextColor = ThemeColors.GetTheme(_settings.Theme).Text;
                IBrush? textBrush = null;
                if (Color.TryParse(themeTextColor, out var parsedColor))
                    textBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(parsedColor);

                replacement = new DiagramCanvas
                {
                    Document = doc,
                    DefaultTextBrush = textBrush,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };
            }

            if (replacement is null) continue;

            // Add right-click context menu with Save PNG/SVG options
            var mermaidCode = _markdownService.MermaidDiagrams.GetValueOrDefault(key);
            if (mermaidCode is not null)
            {
                var contextMenu = new ContextMenu();
                var savePng = new MenuItem { Header = "Save Diagram as PNG..." };
                savePng.Click += (_, _) => _ = SaveDiagramAs(mermaidCode, "png");
                var saveSvg = new MenuItem { Header = "Save Diagram as SVG..." };
                saveSvg.Click += (_, _) => _ = SaveDiagramAs(mermaidCode, "svg");
                contextMenu.Items.Add(savePng);
                contextMenu.Items.Add(saveSvg);
                replacement.ContextMenu = contextMenu;
            }

            ReplaceControlInVisualTree(textBlock, replacement);
        }
    }

    /// <summary>
    /// Walk the visual tree to find TextBlock/SelectableTextBlock controls that contain
    /// diagram marker text (FLOWCHART: or DIAGRAM:). LiveMarkdown renders paragraph text
    /// as Run elements inside MarkdownTextBlock (a SelectableTextBlock subclass), so we check Inlines.
    /// </summary>
    private static void FindDiagramMarkers(Visual parent, List<(TextBlock, string Prefix, string Key)> results)
    {
        foreach (var child in VisualExtensions.GetVisualChildren(parent))
        {
            if (child is TextBlock textBlock)
            {
                var match = ExtractDiagramMarkerKey(textBlock);
                if (match is not null)
                {
                    results.Add((textBlock, match.Value.Prefix, match.Value.Key));
                    continue; // Don't recurse into matched element
                }
            }

            if (child is Visual visual)
            {
                FindDiagramMarkers(visual, results);
            }
        }
    }

    /// <summary>
    /// Extract a diagram marker key from a TextBlock by checking both
    /// the Text property (simple TextBlock) and Inlines (LiveMarkdown's
    /// MarkdownTextBlock uses Run elements in Inlines).
    /// Returns the matching prefix and key, or null if not a marker.
    /// </summary>
    private static (string Prefix, string Key)? ExtractDiagramMarkerKey(TextBlock textBlock)
    {
        var text = textBlock.Text;

        if (string.IsNullOrEmpty(text) && textBlock.Inlines is { Count: > 0 } inlines)
        {
            text = string.Concat(inlines.OfType<Run>().Select(r => r.Text ?? ""));
        }

        if (string.IsNullOrEmpty(text)) return null;

        // Try flowchart prefix first
        var idx = text.IndexOf(MarkdownService.FlowchartMarkerPrefix, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var key = text[(idx + MarkdownService.FlowchartMarkerPrefix.Length)..].Trim();
            return string.IsNullOrEmpty(key) ? null : (MarkdownService.FlowchartMarkerPrefix, key);
        }

        // Try diagram prefix
        idx = text.IndexOf(MarkdownService.DiagramMarkerPrefix, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var key = text[(idx + MarkdownService.DiagramMarkerPrefix.Length)..].Trim();
            return string.IsNullOrEmpty(key) ? null : (MarkdownService.DiagramMarkerPrefix, key);
        }

        return null;
    }

    private static void ReplaceControlInVisualTree(Control target, Control replacement)
    {
        // Walk up the tree to find a Panel parent where we can do the swap
        Visual? current = target;
        while (current is not null)
        {
            var parent = VisualExtensions.GetVisualParent(current);
            if (parent is null) break;

            if (parent is Panel panel && current is Control ctrl)
            {
                var index = panel.Children.IndexOf(ctrl);
                if (index >= 0)
                {
                    panel.Children[index] = replacement;
                    // Force parent to re-measure since the new child has different size
                    panel.InvalidateMeasure();
                    panel.InvalidateArrange();
                    return;
                }
            }
            else if (parent is ContentControl contentControl && contentControl.Content == current)
            {
                contentControl.Content = replacement;
                return;
            }
            else if (parent is Decorator decorator && decorator.Child == current as Control)
            {
                decorator.Child = replacement;
                return;
            }

            current = parent;
        }
    }

    /// <summary>
    /// Debug: Capture a screenshot of the window + dump the visual tree to files.
    /// Triggered by Ctrl+F12. Files saved to AppData/MarkdownViewer/debug/.
    /// </summary>
    private async Task DebugScreenshot()
    {
        try
        {
            var debugDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MarkdownViewer", "debug");
            Directory.CreateDirectory(debugDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Screenshot
            var pixelSize = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            if (pixelSize.Width > 0 && pixelSize.Height > 0)
            {
                var bitmap = new RenderTargetBitmap(pixelSize);
                bitmap.Render(this);
                var screenshotPath = Path.Combine(debugDir, $"screenshot_{timestamp}.png");
                bitmap.Save(screenshotPath);
                Debug.WriteLine($"[Debug] Screenshot saved: {screenshotPath}");
            }

            // Visual tree dump
            var treePath = Path.Combine(debugDir, $"vtree_{timestamp}.txt");
            var sb = new System.Text.StringBuilder();
            DumpVisualTreeToString(MdViewer, 0, 12, sb);
            await File.WriteAllTextAsync(treePath, sb.ToString());
            Debug.WriteLine($"[Debug] Visual tree saved: {treePath}");

            // Flowchart layout info
            var infoPath = Path.Combine(debugDir, $"flowchart_info_{timestamp}.txt");
            var infoSb = new System.Text.StringBuilder();
            infoSb.AppendLine($"FlowchartLayouts count: {_markdownService.FlowchartLayouts.Count}");
            foreach (var (key, layout) in _markdownService.FlowchartLayouts)
            {
                infoSb.AppendLine($"  Key='{key}' Nodes={layout.Model.Nodes.Count} Size={layout.Width:F0}x{layout.Height:F0}");
            }
            await File.WriteAllTextAsync(infoPath, infoSb.ToString());

            Debug.WriteLine($"[Debug] All debug files saved to: {debugDir}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Debug] Screenshot failed: {ex.Message}");
        }
    }

    private static void DumpVisualTreeToString(Visual parent, int depth, int maxDepth, System.Text.StringBuilder sb)
    {
        if (depth > maxDepth) return;
        var indent = new string(' ', depth * 2);
        foreach (var child in VisualExtensions.GetVisualChildren(parent))
        {
            var typeName = child.GetType().Name;
            var extra = "";
            if (child is TextBlock tb)
            {
                var text = tb.Text ?? "(null Text)";
                if (tb.Text is null && tb.Inlines is { Count: > 0 } inl)
                {
                    var inlineTexts = string.Concat(inl.OfType<Run>().Select(r => r.Text ?? ""));
                    text = $"(Inlines[{inl.Count}]: {inlineTexts})";
                }
                // Show hex for invisible chars
                var hexPrefix = text.Length > 0 ? $" hex[0..3]={string.Join(" ", text.Take(3).Select(c => $"U+{(int)c:X4}"))}" : "";
                extra = $" Text=\"{(text.Length > 100 ? text[..100] + "..." : text)}\"{hexPrefix}";
            }
            else if (child is Image img)
            {
                extra = $" Source={img.Source?.GetType().Name} W={img.Width} H={img.Height}";
            }
            else if (child is Control ctrl)
            {
                extra = $" W={ctrl.Bounds.Width:F0} H={ctrl.Bounds.Height:F0}";
            }

            sb.AppendLine($"{indent}{typeName}{extra}");

            if (child is Visual v)
            {
                DumpVisualTreeToString(v, depth + 1, maxDepth, sb);
            }
        }
    }

    private static Control? FindHeadingElement(Visual parent, string headingText)
    {
        // Search for TextBlock containing the heading text
        // Use contains check since LiveMarkdown may format headings differently
        foreach (var child in VisualExtensions.GetVisualChildren(parent))
        {
            if (child is TextBlock textBlock)
            {
                var text = textBlock.Text?.Trim() ?? "";
                // Check for exact match or if text contains the heading (for formatted headings)
                if (!string.IsNullOrEmpty(text) &&
                    (text.Equals(headingText, StringComparison.OrdinalIgnoreCase) ||
                     text.Contains(headingText, StringComparison.OrdinalIgnoreCase)))
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

        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles()?.ToList();
            if (files?.Count > 0)
            {
                var path = files[0].Path.LocalPath;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is ".md" or ".markdown" or ".mdown" or ".mkd" or ".txt") await LoadFile(path);
            }
        }
    }
#pragma warning restore CS0618

    #endregion
}

public class RelayCommand : ICommand
{
    private readonly Action? _execute;
    private readonly Func<Task>? _executeAsync;

    public RelayCommand(Action execute)
    {
        _execute = execute;
    }

    public RelayCommand(Func<Task> executeAsync)
    {
        _executeAsync = executeAsync;
    }

#pragma warning disable CS0067 // Event is never used - required by ICommand interface
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    public bool CanExecute(object? parameter)
    {
        return true;
    }

    public async void Execute(object? parameter)
    {
        if (_executeAsync != null)
            await _executeAsync();
        else
            _execute?.Invoke();
    }
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;

    public RelayCommand(Action<T?> execute)
    {
        _execute = execute;
    }

#pragma warning disable CS0067 // Event is never used - required by ICommand interface
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    public bool CanExecute(object? parameter)
    {
        return true;
    }

    public void Execute(object? parameter)
    {
        _execute((T?)parameter);
    }
}
