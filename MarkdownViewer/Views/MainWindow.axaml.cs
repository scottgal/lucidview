using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MarkdownViewer.Models;
using MarkdownViewer.Services;
using System.Windows.Input;

namespace MarkdownViewer.Views;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly MarkdownService _markdownService;
    private readonly NavigationService _navigationService;
    private readonly ThemeService _themeService;
    private readonly SearchService _searchService;
    private string? _currentFilePath;
    private string _rawContent = string.Empty;
    private List<HeadingItem> _headings = [];
    private double _zoomLevel = 1.0;
    private List<SearchResult> _searchResults = [];
    private int _currentSearchIndex = -1;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _markdownService = new MarkdownService();
        _navigationService = new NavigationService();
        _themeService = new ThemeService(Application.Current!);
        _searchService = new SearchService();

        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;

        DataContext = this;

        // Apply saved theme
        ApplyTheme(_settings.Theme);
        UpdateThemeComboBox(_settings.Theme);

        // Apply nav panel state
        NavPanel.IsVisible = _settings.ShowNavigationPanel;
        NavPanelToggle.IsChecked = _settings.ShowNavigationPanel;
        NavPanelCheck.IsChecked = _settings.ShowNavigationPanel;

        // Drag and drop
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        PopulateRecentFiles();
        Closing += OnWindowClosing;

        // Command line argument
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1]))
        {
            _ = LoadFile(args[1]);
        }
    }

    #region Commands

    public ICommand OpenFileCommand => new RelayCommand(async () => await OpenFile());
    public ICommand OpenUrlCommand => new RelayCommand(async () => await OpenUrl());
    public ICommand OpenSettingsCommand => new RelayCommand(async () => await OpenSettings());
    public ICommand ToggleFullScreenCommand => new RelayCommand(ToggleFullScreen);
    public ICommand ToggleNavPanelCommand => new RelayCommand(ToggleNavPanel);
    public ICommand ZoomInCommand => new RelayCommand(ZoomIn);
    public ICommand ZoomOutCommand => new RelayCommand(ZoomOut);
    public ICommand ResetZoomCommand => new RelayCommand(ResetZoom);
    public ICommand ExitCommand => new RelayCommand(() => Close());
    public ICommand NavigateToHeadingCommand => new RelayCommand<HeadingItem>(NavigateToHeading);
    public ICommand ToggleSearchCommand => new RelayCommand(ToggleSearch);
    public ICommand CloseSearchCommand => new RelayCommand(CloseSearch);
    public ICommand OpenHelpCommand => new RelayCommand(async () => await OpenHelp());

    #endregion

    #region File Operations

    private async Task OpenFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Markdown File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Markdown Files") { Patterns = ["*.md", "*.markdown", "*.mdown", "*.mkd", "*.txt"] },
                new FilePickerFileType("All Files") { Patterns = ["*.*"] }
            ]
        });

        if (files.Count > 0)
        {
            await LoadFile(files[0].Path.LocalPath);
        }
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
            Title = $"{Path.GetFileName(path)} - Markdown Viewer";
            _settings.AddRecentFile(path);
            PopulateRecentFiles();

            var fileInfo = new FileInfo(path);
            FileInfoText.Text = $"{fileInfo.Length:N0} bytes";
            StatusText.Text = "Ready";
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

        if (!string.IsNullOrWhiteSpace(result))
        {
            await LoadFromUrl(result);
        }
    }

    private async Task LoadFromUrl(string url)
    {
        try
        {
            StatusText.Text = "Downloading...";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MarkdownViewer/1.0");
            var content = await httpClient.GetStringAsync(url);

            var uri = new Uri(url);
            var baseUrl = $"{uri.Scheme}://{uri.Host}{string.Join("", uri.Segments.Take(uri.Segments.Length - 1))}";
            _markdownService.SetBaseUrl(baseUrl);

            await DisplayMarkdown(content);

            _currentFilePath = url;
            Title = $"{uri.Segments.LastOrDefault()?.TrimEnd('/') ?? "Remote"} - Markdown Viewer";
            FileInfoText.Text = "Remote file";
            StatusText.Text = "Ready";
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

        // Process and display markdown
        var processed = _markdownService.ProcessMarkdown(content);
        MarkdownViewer.Markdown = processed;
        RawTextBlock.Text = content;

        WelcomePanel.IsVisible = false;
        ContentGrid.IsVisible = true;

        // Reset to preview tab
        PreviewTab.IsChecked = true;
        RenderedScroller.IsVisible = true;
        RawScroller.IsVisible = false;

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

    #endregion

    #region Navigation

    private void ToggleNavPanel()
    {
        var isVisible = !NavPanel.IsVisible;
        NavPanel.IsVisible = isVisible;
        NavPanelToggle.IsChecked = isVisible;
        NavPanelCheck.IsChecked = isVisible;
        _settings.ShowNavigationPanel = isVisible;
        _settings.Save();
    }

    private void OnNavPanelToggle(object? sender, RoutedEventArgs e)
    {
        var isVisible = NavPanelToggle.IsChecked == true;
        NavPanel.IsVisible = isVisible;
        NavPanelCheck.IsChecked = isVisible;
        _settings.ShowNavigationPanel = isVisible;
        _settings.Save();
    }

    private void NavigateToHeading(HeadingItem? heading)
    {
        if (heading == null) return;

        // Switch to preview tab if on raw
        PreviewTab.IsChecked = true;
        RenderedScroller.IsVisible = true;
        RawScroller.IsVisible = false;

        // TODO: Implement scroll to heading
        // This would require finding the visual element for the heading
        // and scrolling to it. For now, just ensure we're on preview.
    }

    #endregion

    #region Theme

    private void ApplyTheme(AppTheme theme)
    {
        _themeService.ApplyTheme(theme);
        _settings.Theme = theme;
    }

    private void UpdateThemeComboBox(AppTheme theme)
    {
        ThemeComboBox.SelectedIndex = theme switch
        {
            AppTheme.Light => 0,
            AppTheme.Dark => 1,
            AppTheme.VSCode => 2,
            AppTheme.GitHub => 3,
            _ => 1
        };
    }

    private void OnThemeSelected(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string themeName)
        {
            if (Enum.TryParse<AppTheme>(themeName, out var theme))
            {
                ApplyTheme(theme);
                UpdateThemeComboBox(theme);
                _settings.Save();
            }
        }
    }

    private void OnThemeComboChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Guard against initialization-time calls
        if (_themeService == null) return;

        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string themeName)
        {
            if (Enum.TryParse<AppTheme>(themeName, out var theme))
            {
                ApplyTheme(theme);
                _settings.Save();
            }
        }
    }

    #endregion

    #region Settings

    private async Task OpenSettings()
    {
        var dialog = new SettingsDialog(_settings);
        await dialog.ShowDialog(this);
        ApplyTheme(_settings.Theme);
        UpdateThemeComboBox(_settings.Theme);
    }

    #endregion

    #region View

    private void OnTabChanged(object? sender, RoutedEventArgs e)
    {
        var isPreview = PreviewTab.IsChecked == true;
        RenderedScroller.IsVisible = isPreview;
        RawScroller.IsVisible = !isPreview;
    }

    private void ToggleFullScreen()
    {
        WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
    }

    private void ZoomIn()
    {
        _zoomLevel = Math.Min(3.0, _zoomLevel + 0.1);
        ApplyZoom();
    }

    private void ZoomOut()
    {
        _zoomLevel = Math.Max(0.5, _zoomLevel - 0.1);
        ApplyZoom();
    }

    private void ResetZoom()
    {
        _zoomLevel = 1.0;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        MarkdownViewer.RenderTransform = new Avalonia.Media.ScaleTransform(_zoomLevel, _zoomLevel);
        ZoomText.Text = $"{_zoomLevel * 100:F0}%";
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

    private void OnSearchPrevious(object? sender, RoutedEventArgs e) => SearchPrevious();
    private void OnSearchNext(object? sender, RoutedEventArgs e) => SearchNext();
    private void OnCloseSearch(object? sender, RoutedEventArgs e) => CloseSearch();

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

        if (_searchResults.Count == 0)
        {
            SearchResultsText.Text = "No matches";
        }
    }

    private void HighlightCurrentResult()
    {
        if (_currentSearchIndex < 0 || _currentSearchIndex >= _searchResults.Count)
            return;

        var result = _searchResults[_currentSearchIndex];
        SearchResultsText.Text = $"{_currentSearchIndex + 1} of {_searchResults.Count}";

        // Switch to raw view to show line-based search results
        RawTab.IsChecked = true;
        RenderedScroller.IsVisible = false;
        RawScroller.IsVisible = true;

        // Scroll to the line containing the result
        var lines = _rawContent.Split('\n');
        if (result.Line < lines.Length)
        {
            // Calculate approximate scroll position based on line number
            var lineHeight = 18.0; // Approximate line height for monospace text
            var scrollOffset = result.Line * lineHeight;
            RawScroller.Offset = new Vector(0, Math.Max(0, scrollOffset - 100));
        }
    }

    private async Task OpenHelp()
    {
        // Try to find README.md in the application directory
        var exePath = AppContext.BaseDirectory;
        var readmePath = Path.Combine(exePath, "README.md");

        if (File.Exists(readmePath))
        {
            await LoadFile(readmePath);
        }
        else
        {
            // Try development location
            var devPath = Path.Combine(exePath, "..", "..", "..", "..", "README.md");
            if (File.Exists(devPath))
            {
                await LoadFile(Path.GetFullPath(devPath));
            }
            else
            {
                StatusText.Text = "README.md not found";
            }
        }
    }

    #endregion

    #region Drag and Drop

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            DropOverlay.IsVisible = true;
        }
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
                if (ext is ".md" or ".markdown" or ".mdown" or ".mkd" or ".txt")
                {
                    await LoadFile(path);
                }
            }
        }
    }

    #endregion

    #region Event Handlers

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _settings.WindowWidth = (int)Width;
        _settings.WindowHeight = (int)Height;
        _settings.Save();
    }

    private async void OnAboutClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "About Markdown Viewer",
            Width = 350,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Avalonia.Media.Brushes.Transparent
        };

        var content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        content.Children.Add(new TextBlock
        {
            Text = "Markdown Viewer",
            FontSize = 20,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        });

        content.Children.Add(new TextBlock
        {
            Text = "Version 1.0.0",
            Foreground = Avalonia.Media.Brushes.Gray,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        });

        content.Children.Add(new TextBlock
        {
            Text = "A lightweight cross-platform markdown viewer",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        });

        content.Children.Add(new TextBlock
        {
            Text = "Built with Avalonia UI",
            Foreground = Avalonia.Media.Brushes.Gray,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        });

        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(24, 8)
        };
        okButton.Click += (_, _) => dialog.Close();
        content.Children.Add(okButton);

        dialog.Content = content;
        await dialog.ShowDialog(this);
    }

    private void PopulateRecentFiles()
    {
        RecentFilesMenu.Items.Clear();

        if (_settings.RecentFiles.Count == 0)
        {
            var emptyItem = new MenuItem { Header = "(No recent files)", IsEnabled = false };
            RecentFilesMenu.Items.Add(emptyItem);
            return;
        }

        foreach (var recent in _settings.RecentFiles.Take(10))
        {
            var item = new MenuItem { Header = recent.DisplayName, Tag = recent.Path };
            item.Click += async (s, _) =>
            {
                if (s is MenuItem mi && mi.Tag is string path)
                {
                    if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        await LoadFromUrl(path);
                    else
                        await LoadFile(path);
                }
            };
            RecentFilesMenu.Items.Add(item);
        }

        RecentFilesMenu.Items.Add(new Separator());
        var clearItem = new MenuItem { Header = "Clear Recent Files" };
        clearItem.Click += (_, _) =>
        {
            _settings.RecentFiles.Clear();
            _settings.Save();
            PopulateRecentFiles();
        };
        RecentFilesMenu.Items.Add(clearItem);
    }

    #endregion
}

public class RelayCommand : ICommand
{
    private readonly Action? _execute;
    private readonly Func<Task>? _executeAsync;

    public RelayCommand(Action execute) => _execute = execute;
    public RelayCommand(Func<Task> executeAsync) => _executeAsync = executeAsync;

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;

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

    public RelayCommand(Action<T?> execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        _execute((T?)parameter);
    }
}
