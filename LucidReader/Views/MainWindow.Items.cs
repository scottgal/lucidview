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

        // Same reasoning for thumbnail resolution: rows about to be cleared
        // must not have a favicon-style fetch land on them after they are
        // gone. ResolveThumbnailsAsync below starts a fresh batch anyway,
        // but that only happens after the awaits below complete, and a slow
        // query must not leave the previous list's thumbnails resolving in
        // the meantime.
        _thumbnailCoordinator.CancelPending();

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

        // Cancelled again, deliberately, not a copy-paste leftover: the early
        // CancelPending() above only stops a dwell that was already pending
        // when this reload started. If the user selected an item in the
        // still-displayed old list during the awaits above, OnItemSelectedAsync
        // started a brand new dwell that the early call never saw. That dwell
        // targets a row about to be wiped out by the Clear() below, so it must
        // die here too, or it fires 800ms later against a feed the user has
        // since navigated away from.
        _dwell.CancelPending();

        // Built as a plain list and handed over in one go. Adding to the
        // bound collection row by row raised a change notification per row,
        // so a full page (500, see ItemQueryBuilder) meant 500 separate
        // container updates for one list. See BatchObservableCollection.
        ItemRows.ReplaceAll(items.Select(item => new ItemRow
        {
            Item = item,
            FeedName = feeds.GetValueOrDefault(item.FeedId, "Unknown feed"),
            IsRead = item.IsRead,
            IsStarred = item.IsStarred,
            RelativeDate = ItemRow.FormatRelative(
                item.PublishedUtc ?? item.FirstSeenUtc, now),
            Snippet = Snippet.FromMarkdown(item.ContentMarkdown, item.Summary)
        }).ToList());

        StatusMessage = ItemRows.Count == 0
            ? "No articles here yet."
            : $"{ItemRows.Count} articles";

        // Fired without awaiting: the list is already on screen with full
        // text and no thumbnail, and thumbnails fill in as they resolve.
        // See MainWindow.Images.cs.
        _ = ResolveThumbnailsAsync(ItemRows.ToList());
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
                var affected = await _services.Items.SetReadAsync(row.Id, true, token);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    row.IsRead = true;
                    AdjustUnreadCount(affected, -1);
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

    /// <summary>
    /// Drops the article selection and empties the reading pane, awaiting the
    /// clear rather than leaving it to run on its own.
    ///
    /// Assigning null through the SelectedItemRow property would work, but its
    /// setter fires OnItemSelectedAsync without awaiting it, so the caller has
    /// no way to know when the pane has actually been emptied. A caller that
    /// then writes its own content into the pane - ShowUserManualAsync is the
    /// one that does - would race that continuation and have its text blanked
    /// a moment later. Everything the setter does is done here instead, in
    /// order, with the clear awaited.
    ///
    /// Raising SelectedItemRow is what un-highlights the row in the list. The
    /// write-back from the ListBox's two-way binding lands in the setter as
    /// null over null, which it discards.
    /// </summary>
    private async Task ClearArticleSelectionAsync()
    {
        _dwell.CancelPending();

        if (_selectedItemRow is not null)
        {
            _selectedItemRow = null;
            Raise(nameof(SelectedItemRow));
            UpdateMenuEnablement();
        }

        await ShowArticleAsync(null);
    }

    public async Task MarkSelectedReadAsync()
    {
        if (SelectedItemRow is not { } row) return;

        // A manual toggle is an explicit user action; it must not be
        // silently reverted a moment later by a dwell timer that was
        // already running against the same row.
        if (ReferenceEquals(row, SelectedItemRow)) _dwell.CancelPending();

        await ToggleReadAsync(row);
    }

    /// <summary>
    /// Shared by the M keybinding (via MarkSelectedReadAsync, always the
    /// selected row) and the hover row actions (any row under the pointer,
    /// selected or not). A row's own dwell only needs cancelling when it is
    /// the one currently selected; MarkSelectedReadAsync already does that
    /// before calling in.
    /// </summary>
    public async Task ToggleReadAsync(ItemRow row)
    {
        var target = !row.IsRead;
        var affected = await _services.Items.SetReadAsync(row.Id, target);
        row.IsRead = target;
        AdjustUnreadCount(affected, target ? -1 : 1);
    }

    /// <summary>
    /// Nudges the cached unread counts rather than requerying the whole tree,
    /// so scanning a list stays responsive. If this cache is ever observed to
    /// drift from the database (e.g. an external write racing a dwell mark),
    /// the resync path is a full LoadFeedTreeAsync, not a bigger clamp here.
    ///
    /// changedFeedIds holds one entry per ROW the write actually changed, which
    /// is more than one when the same article is stored under two
    /// subscriptions (see ItemRepository.SetReadAsync). Every feed named there
    /// moves by delta, because each of those feeds really did gain or lose an
    /// unread row. The folder and Unread totals move by delta ONCE however
    /// many rows changed, because those counts are counts of articles and the
    /// list shows the article once.
    /// </summary>
    private void AdjustUnreadCount(IReadOnlyList<long> changedFeedIds, int delta)
    {
        if (changedFeedIds.Count == 0) return;

        var allNodes = AllFeedTreeNodes.ToList();

        foreach (var node in allNodes)
        {
            var steps = node.Kind switch
            {
                FeedTreeNodeKind.Feed => changedFeedIds.Count(id => id == node.FeedId),
                FeedTreeNodeKind.Smart => node.SmartFilter == ItemFilter.Unread ? 1 : 0,
                FeedTreeNodeKind.Folder => allNodes.Any(n =>
                    n.Kind == FeedTreeNodeKind.Feed
                    && n.FolderId == node.FolderId
                    && changedFeedIds.Contains(n.FeedId ?? -1))
                    ? 1
                    : 0,
                _ => 0
            };

            if (steps > 0) node.UnreadCount = Math.Max(0, node.UnreadCount + delta * steps);
        }

        // The status item shows the same total the Unread row does, and this
        // is the path that changes it without a tree reload: reading an
        // article would otherwise leave the menu bar claiming the old count
        // until something else happened to rebuild the sidebar.
        UpdateStatusItemUnreadCount();
    }
}
