using Avalonia.Threading;
using LucidReader.Models;
using LucidReader.Services;

namespace LucidReader.Views;

/// <summary>
/// Background resolution of favicons (sidebar) and OpenGraph thumbnails
/// (item list) into the local cached paths FeedTreeNode.IconPath and
/// ItemRow.ThumbnailPath bind to. The reading pane's hero image lives in
/// MainWindow.Reading.cs instead, next to ShowArticleAsync, since it
/// resolves one image per article rather than a whole list.
///
/// The design constraint driving every method here: LoadFeedTreeAsync and
/// LoadItemsAsync must return with rows fully visible before a single image
/// request goes out. Both callers fire these methods without awaiting them,
/// so a slow or large feed/list never delays the text appearing on screen.
///
/// Cancellation reuses ImageResolutionCoordinator (itself deliberately
/// shaped like Task 7's LoadSequenceGuard/DwellCoordinator) rather than a
/// third mechanism: starting a new batch cancels whichever rows the
/// previous batch was still resolving for, so a fast feed switch does not
/// leave stale favicon/thumbnail fetches running against rows that have
/// since been cleared out of the sidebar or the list.
/// </summary>
public partial class MainWindow
{
    private readonly ImageResolutionCoordinator _iconCoordinator = new();
    private readonly ImageResolutionCoordinator _thumbnailCoordinator = new();

    private async Task ResolveSidebarIconsAsync()
    {
        var token = _iconCoordinator.StartBatch();

        var feedNodes = AllFeedTreeNodes
            .Where(n => n.Kind == FeedTreeNodeKind.Feed && !string.IsNullOrWhiteSpace(n.IconUrl))
            .ToList();

        var tasks = feedNodes.Select(node => _iconCoordinator.RunAsync(token, async ct =>
        {
            var local = await _services.Images.ResolveAsync(node.IconUrl, ct);
            if (ct.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(() => node.IconPath = local);
        }));

        await Task.WhenAll(tasks);
    }

    private async Task ResolveThumbnailsAsync(IReadOnlyList<ItemRow> rows)
    {
        var token = _thumbnailCoordinator.StartBatch();

        var withImages = rows.Where(r => !string.IsNullOrWhiteSpace(r.Item.ImageUrl)).ToList();

        var tasks = withImages.Select(row => _thumbnailCoordinator.RunAsync(token, async ct =>
        {
            var local = await _services.Images.ResolveAsync(row.Item.ImageUrl, ct);
            if (ct.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(() => row.ThumbnailPath = local);
        }));

        await Task.WhenAll(tasks);
    }
}
