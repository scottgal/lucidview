using Avalonia.Threading;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// The item list. Loads whatever the feed tree has selected, and marks the
/// selected item read after a dwell delay rather than instantly, so holding
/// J to scan a list does not mark everything read behind you.
/// </summary>
public partial class MainWindow
{
    private CancellationTokenSource? _dwellCts;

    /// <summary>
    /// Builds the query for whatever is selected in the feed tree. A smart row
    /// queries across every feed and overrides the filter chips; a folder or a
    /// feed scopes the query and keeps the chosen filter.
    /// </summary>
    internal ItemQuery BuildQuery()
    {
        var node = SelectedFeedNode;

        if (node is null || node.Kind == FeedTreeNodeKind.Smart)
            return new ItemQuery(null, null, node?.SmartFilter ?? CurrentFilter, 500, 0);

        return new ItemQuery(
            node.Kind == FeedTreeNodeKind.Feed ? node.FeedId : null,
            node.Kind == FeedTreeNodeKind.Folder ? node.FolderId : null,
            CurrentFilter,
            500,
            0);
    }

    public async Task LoadItemsAsync()
    {
        var items = await _services.Items.QueryAsync(BuildQuery());
        var feeds = (await _services.Feeds.GetAllAsync())
            .ToDictionary(f => f.Id, f => f.DisplayTitle);
        var now = DateTimeOffset.UtcNow;

        ItemRows.Clear();
        foreach (var item in items)
        {
            ItemRows.Add(new ItemRow
            {
                Item = item,
                FeedName = feeds.GetValueOrDefault(item.FeedId, "Unknown feed"),
                IsRead = item.IsRead,
                IsStarred = item.IsStarred,
                RelativeDate = ItemRow.FormatRelative(
                    item.PublishedUtc ?? item.FirstSeenUtc, now)
            });
        }

        StatusMessage = ItemRows.Count == 0
            ? "No articles here yet."
            : $"{ItemRows.Count} articles";
    }

    private async Task OnItemSelectedAsync(ItemRow? row)
    {
        // Cancel any pending mark-as-read from the previously selected item.
        // Without this, holding J to scan a list marks every item read behind
        // you, which is the single most annoying thing a reader can do.
        if (_dwellCts is not null)
        {
            await _dwellCts.CancelAsync();
            _dwellCts.Dispose();
            _dwellCts = null;
        }

        await ShowArticleAsync(row);

        if (row is null || row.IsRead) return;

        _dwellCts = new CancellationTokenSource();
        var token = _dwellCts.Token;
        var dwell = TimeSpan.FromMilliseconds(
            Math.Max(0, _services.Settings.MarkReadDwellMilliseconds));

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(dwell, token);
                await _services.Items.SetReadAsync(row.Id, true, token);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    row.IsRead = true;
                    AdjustUnreadCount(row.Item.FeedId, -1);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    StatusMessage = "Could not mark the article as read: " + ex.Message);
            }
        }, token);
    }

    public async Task MarkSelectedReadAsync()
    {
        if (SelectedItemRow is not { } row) return;

        var target = !row.IsRead;
        await _services.Items.SetReadAsync(row.Id, target);
        row.IsRead = target;
        AdjustUnreadCount(row.Item.FeedId, target ? -1 : 1);
    }

    /// <summary>Stub: a later task re-queries ItemRows against SearchText.</summary>
#pragma warning disable CA1822
    private Task OnSearchTextChangedAsync() => Task.CompletedTask;
#pragma warning restore CA1822

    /// <summary>
    /// Nudges the cached unread counts rather than requerying the whole tree,
    /// so scanning a list stays responsive.
    /// </summary>
    private void AdjustUnreadCount(long feedId, int delta)
    {
        foreach (var node in FeedNodes)
        {
            var affected = node.Kind switch
            {
                FeedTreeNodeKind.Feed => node.FeedId == feedId,
                FeedTreeNodeKind.Smart => node.SmartFilter == ItemFilter.Unread,
                FeedTreeNodeKind.Folder => FeedNodes.Any(n =>
                    n.Kind == FeedTreeNodeKind.Feed && n.FeedId == feedId && n.FolderId == node.FolderId),
                _ => false
            };

            if (affected) node.UnreadCount = Math.Max(0, node.UnreadCount + delta);
        }
    }
}
