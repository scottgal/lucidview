using Avalonia.Threading;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Models;
using LucidReader.Services;

namespace LucidReader.Views;

/// <summary>
/// The item list. Loads whatever the feed tree has selected, and marks the
/// selected item read after a dwell delay rather than instantly, so holding
/// J to scan a list does not mark everything read behind you.
///
/// Two coordinators, both plain non-Avalonia classes tested directly in
/// LucidReader.Core.Tests, back the two correctness properties this file
/// depends on:
/// - <see cref="_loadGuard"/> (LoadSequenceGuard) stops a slow, earlier
///   LoadItemsAsync call from landing after a faster, later one and
///   repopulating the list with the wrong feed's items while the tree shows
///   the new selection.
/// - <see cref="_dwell"/> (DwellCoordinator) owns the single outstanding
///   mark-read timer, so every event that invalidates it (reselecting an
///   item, reloading the list, a manual read toggle, the window closing)
///   cancels it explicitly rather than relying on incidental ListBox
///   selection-clearing behaviour.
/// </summary>
public partial class MainWindow
{
    private readonly LoadSequenceGuard _loadGuard = new();
    private readonly DwellCoordinator _dwell = new();

    /// <summary>
    /// Builds the query for whatever is selected in the feed tree. Delegates
    /// to ItemQueryBuilder (a plain class) so the mapping itself is unit
    /// tested without constructing a Window; this method stays internal so
    /// it is still directly awaitable/callable from a test that already has
    /// a MainWindow instance, per the task's interface contract.
    /// </summary>
    internal ItemQuery BuildQuery() => ItemQueryBuilder.Build(SelectedFeedNode, CurrentFilter);

    public async Task LoadItemsAsync()
    {
        // The reload itself invalidates any dwell in flight: whatever row it
        // belonged to is about to be cleared out of ItemRows regardless of
        // whether this particular query turns out to win the race below.
        _dwell.CancelPending();

        var ticket = _loadGuard.Begin();
        var items = await _services.Items.QueryAsync(BuildQuery());
        var feeds = (await _services.Feeds.GetAllAsync())
            .ToDictionary(f => f.Id, f => f.DisplayTitle);
        var now = DateTimeOffset.UtcNow;

        // A newer LoadItemsAsync call started while these awaits were in
        // flight. That call's result is the one the user actually asked
        // for (the feed tree has already moved on); this one is stale and
        // must not touch ItemRows.
        if (!_loadGuard.IsCurrent(ticket)) return;

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
        _dwell.CancelPending();

        await ShowArticleAsync(row);

        if (row is null || row.IsRead) return;

        var token = _dwell.StartNew();
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

        // A manual toggle is an explicit user action; it must not be
        // silently reverted a moment later by a dwell timer that was
        // already running against the same row.
        _dwell.CancelPending();

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
    /// so scanning a list stays responsive. If this cache is ever observed to
    /// drift from the database (e.g. an external write racing a dwell mark),
    /// the resync path is a full LoadFeedTreeAsync, not a bigger clamp here.
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
