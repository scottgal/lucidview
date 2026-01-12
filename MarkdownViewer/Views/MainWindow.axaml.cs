using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using MarkdownViewer.Models;
using MarkdownViewer.Services;
using System.Windows.Input;

namespace MarkdownViewer.Views;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly MarkdownService _markdownService;
    private string? _currentFilePath;
    private string _rawContent = string.Empty;
    private double _zoomLevel = 1.0;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _markdownService = new MarkdownService();

        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
        ApplyTheme(_settings.IsDarkMode);

        DataContext = this;

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
    public ICommand ZoomInCommand => new RelayCommand(ZoomIn);
    public ICommand ZoomOutCommand => new RelayCommand(ZoomOut);
    public ICommand ResetZoomCommand => new RelayCommand(ResetZoom);
    public ICommand ExitCommand => new RelayCommand(() => Close());

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
            StatusText.Text = $"Downloading...";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MarkdownViewer/1.0");
            var content = await httpClient.GetStringAsync(url);

            // Set base URL for relative images
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
        var processed = _markdownService.ProcessMarkdown(content);
        MarkdownViewer.Markdown = processed;
        RawTextBlock.Text = content;

        WelcomePanel.IsVisible = false;
        ContentPanel.IsVisible = true;

        // Reset to preview tab
        PreviewTab.IsChecked = true;
        RenderedScroller.IsVisible = true;
        RawScroller.IsVisible = false;

        return Task.CompletedTask;
    }

    private void OnTabChanged(object? sender, RoutedEventArgs e)
    {
        var isPreview = PreviewTab.IsChecked == true;
        RenderedScroller.IsVisible = isPreview;
        RawScroller.IsVisible = !isPreview;
    }

    #endregion

    #region Settings

    private async Task OpenSettings()
    {
        var dialog = new SettingsDialog(_settings);
        await dialog.ShowDialog(this);
        ApplyTheme(_settings.IsDarkMode);
    }

    private void ApplyTheme(bool isDark)
    {
        var app = Application.Current as App;
        app?.SetTheme(isDark ? ThemeVariant.Dark : ThemeVariant.Light);
        ThemeToggle.IsChecked = isDark;
        DarkModeCheck.IsChecked = isDark;
        ThemeIcon.Text = isDark ? "Dark" : "Light";
    }

    #endregion

    #region View

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

    private void OnThemeToggleClick(object? sender, RoutedEventArgs e)
    {
        var isDark = ThemeToggle.IsChecked == true;
        _settings.IsDarkMode = isDark;
        ApplyTheme(isDark);
        _settings.Save();
    }

    private void OnDarkModeClick(object? sender, RoutedEventArgs e)
    {
        var isDark = !(DarkModeCheck.IsChecked == true);
        _settings.IsDarkMode = isDark;
        ApplyTheme(isDark);
        _settings.Save();
    }

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

public class RelayCommand(Action? execute = null, Func<Task>? executeAsync = null) : ICommand
{
    public RelayCommand(Action execute) : this(execute, null) { }
    public RelayCommand(Func<Task> executeAsync) : this(null, executeAsync) { }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter)
    {
        if (executeAsync != null)
            await executeAsync();
        else
            execute?.Invoke();
    }
}
