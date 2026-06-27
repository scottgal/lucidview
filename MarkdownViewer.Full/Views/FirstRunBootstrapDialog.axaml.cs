using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MarkdownViewer.Models;
using MarkdownViewer.Services;

namespace MarkdownViewer.Views;

public partial class FirstRunBootstrapDialog : Window
{
    public FirstRunBootstrapDialog() { AvaloniaXamlLoader.Load(this); }

    private void OnSkip(object? sender, RoutedEventArgs e)
    {
        MarkAsRun();
        Close();
    }

    private void OnDefer(object? sender, RoutedEventArgs e)
    {
        MarkAsRun();
        Close();
    }

    private async void OnDownload(object? sender, RoutedEventArgs e)
    {
        DownloadBtn.IsEnabled = false;
        SkipBtn.IsEnabled = false;
        DeferBtn.IsEnabled = false;
        try
        {
            StatusText.Text = "Installing Chromium…";
            await ModelBootstrap.EnsureBrowsersAsync(
                new Progress<string>(s => Dispatcher.UIThread.Post(() => StatusText.Text = s)),
                CancellationToken.None);

            StatusText.Text = "Downloading model (~400 MB)…";
            await ModelBootstrap.EnsureModelAsync(progress: null, CancellationToken.None);

            StatusText.Text = "Ready.";
            MarkAsRun();
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed: {ex.Message}";
            DownloadBtn.IsEnabled = true;
            SkipBtn.IsEnabled = true;
            DeferBtn.IsEnabled = true;
        }
    }

    private static void MarkAsRun()
    {
        var settings = AppSettingsFull.Load();
        settings.HasRunBefore = true;
        settings.Save();
    }
}
