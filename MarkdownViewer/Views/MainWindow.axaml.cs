using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LiveMarkdown.Avalonia;
using MarkdownViewer.Models;
using MarkdownViewer.Services;
using SkiaSharp;
using VisualExtensions = Avalonia.VisualTree.VisualExtensions;

namespace MarkdownViewer.Views;

public partial class MainWindow : Window
{
    private readonly ObservableStringBuilder _markdownBuilder = new();
    private readonly MarkdownService _markdownService;
    private readonly NavigationService _navigationService;
    private readonly PaginationService _paginationService;
    private readonly SearchService _searchService;
    private readonly AppSettings _settings;
    private readonly ThemeService _themeService;
    private string? _currentFilePath;
    private int _currentSearchIndex = -1;
    private int _fontSize = 16;
    private List<HeadingItem> _headings = [];
    private bool _isSidePanelOpen;
    private string _rawContent = string.Empty;
    private List<SearchResult> _searchResults = [];
    private double _zoomLevel = 1.0;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _markdownService = new MarkdownService();

        // Initialize LiveMarkdown renderer
        MdViewer.MarkdownBuilder = _markdownBuilder;
        MdViewer.ImageBasePath = _markdownService.TempDirectory;
        _navigationService = new NavigationService();
        _themeService = new ThemeService(Application.Current!);
        _searchService = new SearchService();
        _paginationService = new PaginationService();

        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
        _fontSize = _settings.FontSize > 0 ? (int)_settings.FontSize : 16;

        DataContext = this;

        // Set window icon
        SetWindowIcon();

        // Apply saved theme
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

        // Command line argument
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1])) _ = LoadFile(args[1]);
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
            using var lucidPaint = new SKPaint
            {
                Color = new SKColor(0xDD, 0xDD, 0xDD),
                TextSize = 36,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.BoldItalic)
            };
            canvas.Save();
            canvas.Skew(-0.15f, 0);
            canvas.DrawText("l", 14, 46, lucidPaint);
            canvas.Restore();

            // Draw "V" in white (bold)
            using var viewPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 36,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
            };
            canvas.DrawText("V", 30, 46, viewPaint);

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

    public ICommand OpenFileCommand => new RelayCommand(async () => await OpenFile());
    public ICommand OpenUrlCommand => new RelayCommand(async () => await OpenUrl());
    public ICommand OpenSettingsCommand => new RelayCommand(async () => await OpenSettings());
    public ICommand ToggleFullScreenCommand => new RelayCommand(ToggleFullScreen);
    public ICommand ToggleSidePanelCommand => new RelayCommand(ToggleSidePanel);
    public ICommand ToggleSearchCommand => new RelayCommand(ToggleSearch);
    public ICommand EscapeCommand => new RelayCommand(OnEscape);
    public ICommand FontSizeIncreaseCommand => new RelayCommand(IncreaseFontSize);
    public ICommand FontSizeDecreaseCommand => new RelayCommand(DecreaseFontSize);
    public ICommand PrintCommand => new RelayCommand(async () => await Print());
    public ICommand OpenHelpCommand => new RelayCommand(async () => await OpenHelp());

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
        if (_isSidePanelOpen)
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

            await DisplayMarkdown(content);

            _currentFilePath = path;
            Title = $"{Path.GetFileName(path)} - lucidVIEW";
            EnableFontControls(true);
            _settings.AddRecentFile(path);
            UpdateRecentFiles();

            var fileInfo = new FileInfo(path);
            var wordCount = CountWords(content);

            StatusText.Text = path;
            FileDateText.Text = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            WordCountText.Text = $"{wordCount:N0} words";
            FileInfoText.Text = $"{fileInfo.Length:N0} bytes";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
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
            var content = await httpClient.GetStringAsync(url);

            var uri = new Uri(url);
            var baseUrl = $"{uri.Scheme}://{uri.Host}{string.Join("", uri.Segments.Take(uri.Segments.Length - 1))}";
            _markdownService.SetBaseUrl(baseUrl);

            await DisplayMarkdown(content);

            _currentFilePath = url;
            Title = $"{uri.Segments.LastOrDefault()?.TrimEnd('/') ?? "Remote"} - lucidVIEW";
            EnableFontControls(true);
            var wordCount = CountWords(content);

            StatusText.Text = url;
            FileDateText.Text = "Remote";
            WordCountText.Text = $"{wordCount:N0} words";
            FileInfoText.Text = $"{content.Length:N0} chars";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private Task DisplayMarkdown(string content)
    {
        _rawContent = content;

        // Extract headings for navigation
        _headings = _navigationService.ExtractHeadings(content);
        var flatHeadings = FlattenHeadings(_headings);
        NavTreeView.ItemsSource = flatHeadings;

        // Extract and display metadata (categories, publication date)
        var metadata = _markdownService.ExtractMetadata(content);
        DisplayMetadata(metadata);

        // Process and display markdown using LiveMarkdown's ObservableStringBuilder
        var processed = _markdownService.ProcessMarkdown(content);
        _markdownBuilder.Clear();
        _markdownBuilder.Append(processed);
        RawTextBlock.Text = content;

        WelcomePanel.IsVisible = false;
        ContentGrid.IsVisible = true;

        // Update TOC
        UpdateToc();

        // Reset to preview tab
        PreviewTab.IsChecked = true;
        RenderedPanel.IsVisible = true;
        RawScroller.IsVisible = false;

        // Calculate pages after layout (estimate based on content length)
        var estimatedHeight = content.Split('\n').Length * 24.0; // rough estimate
        _paginationService.CalculatePages(estimatedHeight);
        // Pagination removed

        return Task.CompletedTask;
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
        var isDark = theme != AppTheme.Light;
        _markdownService.SetDarkMode(isDark);

        // Refresh current document to regenerate mermaid diagrams with new theme colors
        if (!string.IsNullOrEmpty(_rawContent)) _ = DisplayMarkdown(_rawContent);
    }

    private void UpdatePanelOverlay(AppTheme theme)
    {
        // Light themes get dark overlay, dark themes get light overlay
        var isLightTheme = theme == AppTheme.Light;
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

    private void EnableFontControls(bool enabled)
    {
        FontDecreaseBtn.IsEnabled = enabled;
        FontIncreaseBtn.IsEnabled = enabled;
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
        await dialog.ShowDialog(this);
        ApplyTheme(_settings.Theme);
        UpdateThemeCardSelection(_settings.Theme);
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
            StatusText.Text = "No document to print";
            return;
        }

        try
        {
            StatusText.Text = "Preparing document for print...";

            // Generate HTML for printing (cross-platform approach)
            var html = GeneratePrintHtml(_rawContent);

            // Save to temp file
            var tempPath = Path.Combine(Path.GetTempPath(), $"lucidview_print_{Guid.NewGuid():N}.html");
            await File.WriteAllTextAsync(tempPath, html);

            // Open in default browser for printing
            var psi = new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            };
            Process.Start(psi);

            StatusText.Text = "Document opened in browser - use Ctrl+P to print";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Print error: {ex.Message}";
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
    <script>
        // Auto-open print dialog
        window.onload = function() {{
            window.print();
        }};
    </script>
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

    public event EventHandler? CanExecuteChanged;

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

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return true;
    }

    public void Execute(object? parameter)
    {
        _execute((T?)parameter);
    }
}