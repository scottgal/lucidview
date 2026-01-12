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
    private string? _currentFilePath;
    private string _rawContent = string.Empty;
    private List<HeadingItem> _headings = [];
    private double _zoomLevel = 1.0;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _markdownService = new MarkdownService();
        _navigationService = new NavigationService();
        _themeService = new ThemeService(Application.Current!);

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
