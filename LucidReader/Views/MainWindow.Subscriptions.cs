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
        var queued = 0;
        string? firstProblem = null;

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
                // sidebar until the scheduler's next tick. The queue is
                // bounded and can refuse, so the result is counted rather than
                // assumed.
                if (_services.Refresh.TryQueue(id, isManual: true)) queued++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep the first reason. A count on its own cannot tell a
                // constraint violation from a genuine bug, and cancellation is
                // not a per-feed failure at all, so it is left to propagate.
                failed++;
                firstProblem ??= $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        await LoadFeedTreeAsync();
        StatusMessage = AddFeedInput.DescribeAdded(added, skipped, failed, firstProblem)
                        + AddFeedInput.DescribeQueued(queued, added);
    }

    public async Task ImportOpmlAsync()
    {
        try
        {
            // Inside the try: this is an async void handler's task, so an
            // exception from the picker itself (no storage provider, a broken
            // desktop portal) would otherwise reach the unhandled path rather
            // than the status bar.
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

            // Not every storage provider reports a size; when one does not,
            // the read below is the same unbounded one it always was, which is
            // no worse than before and is the only option available.
            var size = (await files[0].GetBasicPropertiesAsync()).Size;
            if (size is { } bytes && bytes > AddFeedInput.MaxOpmlBytes)
            {
                StatusMessage = AddFeedInput.OpmlTooLargeMessage((long)bytes);
                return;
            }

            string opml;
            await using (var stream = await files[0].OpenReadAsync())
            using (var reader = new StreamReader(stream))
                opml = await reader.ReadToEndAsync();

            var service = new OpmlService(_services.Folders, _services.Feeds);
            var result = await service.ImportAsync(opml);

            await LoadFeedTreeAsync();

            // An imported feed has never been fetched, so nothing would appear
            // in the item list until the scheduler got round to it. Queue the
            // ones this import actually added, and only those: a sweep over
            // every never-fetched feed in the database would also re-fire
            // feeds that have been failing for weeks, walking straight past
            // the backoff that is holding them off.
            var queued = 0;
            foreach (var id in result.AddedFeedIds)
                if (_services.Refresh.TryQueue(id, isManual: true)) queued++;

            StatusMessage = AddFeedInput.DescribeImport(
                                result.FoldersCreated,
                                result.FeedsAdded,
                                result.FeedsSkipped,
                                result.FeedsFailed,
                                result.FailedFeeds.Select(f => f.FeedUrl).ToList())
                            + AddFeedInput.DescribeQueued(queued, result.AddedFeedIds.Count);
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
        try
        {
            // Inside the try for the same reason as the import picker: a
            // failure to even show the dialog belongs in the status bar.
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export subscriptions as OPML",
                SuggestedFileName = "lucidreader-subscriptions.opml",
                DefaultExtension = "opml",
                FileTypeChoices = [new FilePickerFileType("OPML") { Patterns = ["*.opml"] }]
            });

            if (file is null) return;

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
