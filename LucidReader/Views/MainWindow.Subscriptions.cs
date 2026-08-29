using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Opml;
using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// Subscription management (Task 13): adding a feed by address, and OPML
/// import and export. Kept in its own partial rather than added to
/// MainWindow.Actions.cs so the file that owns commands and keyboard
/// navigation does not also own file pickers and autodiscovery.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Autodiscovery is built per dialog rather than held as a field, but it
    /// is built over ReaderServices' own HttpClient, so the reader keeps a
    /// single connection pool rather than opening a second one per dialog.
    /// </summary>
    public async Task ShowAddFeedDialogAsync()
    {
        var folders = await _services.Folders.GetAllAsync();
        var dialog = new AddFeedDialog(new FeedAutodiscovery(_services.Http), folders);
        await dialog.ShowDialog(this);

        if (dialog.Selected.Count == 0) return;

        var added = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var discovered in dialog.Selected)
        {
            // feeds.feed_url has a unique index, so a duplicate would throw.
            // Checking first turns "already subscribed" into a reported count
            // rather than an exception, which is the point of this check; the
            // try/catch below still covers the case where the row appears
            // between this read and the write.
            if (await _services.Feeds.GetByUrlAsync(discovered.FeedUrl) is not null)
            {
                skipped++;
                continue;
            }

            try
            {
                var id = await _services.Feeds.AddAsync(new Feed
                {
                    FeedUrl = discovered.FeedUrl,
                    Title = discovered.Title,
                    IconPath = discovered.IconUrl,
                    FolderId = dialog.SelectedFolderId
                });
                added++;

                // Fetch straight away, so the feed is not an empty row in the
                // sidebar until the scheduler's next tick.
                _services.Refresh.TryQueue(id, isManual: true);
            }
            catch (Exception)
            {
                failed++;
            }
        }

        await LoadFeedTreeAsync();
        StatusMessage = AddFeedInput.DescribeAdded(added, skipped, failed);
    }

    public async Task ImportOpmlAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import subscriptions from OPML",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("OPML") { Patterns = ["*.opml", "*.xml"] }
            ]
        });

        if (files.Count == 0) return;

        try
        {
            string opml;
            await using (var stream = await files[0].OpenReadAsync())
            using (var reader = new StreamReader(stream))
                opml = await reader.ReadToEndAsync();

            var service = new OpmlService(_services.Folders, _services.Feeds);
            var result = await service.ImportAsync(opml);

            await LoadFeedTreeAsync();
            StatusMessage = AddFeedInput.DescribeImport(
                result.FoldersCreated, result.FeedsAdded, result.FeedsSkipped, result.FeedsFailed);

            // An imported feed has never been fetched, so nothing would appear
            // in the item list until the scheduler got round to it. Queue the
            // ones with no successful fetch behind them, which also covers a
            // re-import that is filling in feeds a previous run failed on.
            foreach (var feed in await _services.Feeds.GetAllAsync())
                if (feed.IsEnabled && feed.LastSuccessUtc is null)
                    _services.Refresh.TryQueue(feed.Id, isManual: true);
        }
        catch (OpmlParseException ex)
        {
            StatusMessage = "That file is not a readable OPML export: " + ex.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not import: " + ex.Message;
        }
    }

    public async Task ExportOpmlAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export subscriptions as OPML",
            SuggestedFileName = "lucidreader-subscriptions.opml",
            DefaultExtension = "opml",
            FileTypeChoices = [new FilePickerFileType("OPML") { Patterns = ["*.opml"] }]
        });

        if (file is null) return;

        try
        {
            var service = new OpmlService(_services.Folders, _services.Feeds);
            var opml = await service.ExportAsync(DateTimeOffset.UtcNow);

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(opml);

            StatusMessage = "Subscriptions exported to " + file.Name;
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not export: " + ex.Message;
        }
    }

    /// <summary>
    /// Click handlers rather than Command bindings for the two OPML toolbar
    /// buttons: with compiled bindings off, a Command binding to a property
    /// that does not exist fails silently and leaves a dead button, and these
    /// two have no keyboard shortcut that would need an ICommand anyway.
    /// </summary>
    private async void OnImportOpmlClicked(object? sender, RoutedEventArgs e) => await ImportOpmlAsync();

    private async void OnExportOpmlClicked(object? sender, RoutedEventArgs e) => await ExportOpmlAsync();
}
