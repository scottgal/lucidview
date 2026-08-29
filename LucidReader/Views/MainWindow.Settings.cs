namespace LucidReader.Views;

public partial class MainWindow
{
    /// <summary>
    /// Opens the global settings dialog. The two concurrency settings
    /// (MaxConcurrentFetches, MaxConcurrentDownloads) are fixed when
    /// FeedRefreshService and OfflineDownloader are constructed at startup -
    /// EphemeralWorkCoordinator fixes its concurrency at construction, and
    /// rebuilding both coordinators while work is in flight would be a far
    /// larger change than this setting justifies - so a change to either one
    /// only takes effect the next time lucidREADER starts. Saying so here
    /// rather than letting the setting silently do nothing.
    /// </summary>
    public async Task ShowSettingsDialogAsync()
    {
        var dialog = new SettingsDialog(_services.Settings, _services.Retention);
        await dialog.ShowDialog(this);

        if (dialog.Result is not { } updated) return;

        await _services.UpdateSettingsAsync(updated);

        StatusMessage = updated.MaxConcurrentFetches != _services.ConfiguredFetchConcurrency ||
                         updated.MaxConcurrentDownloads != _services.ConfiguredDownloadConcurrency
            ? "Settings saved. The concurrency changes take effect next time lucidREADER starts."
            : "Settings saved.";
    }
}
