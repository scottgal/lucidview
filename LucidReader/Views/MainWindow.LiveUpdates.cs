using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LucidReader.Core.Sync;
using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// Bringing newly fetched articles into the list the user is already looking
/// at.
///
/// Before this, they never arrived. A background refresh reloaded the sidebar
/// tree, so the unread counts beside each feed climbed, and reloaded nothing
/// else: the item list was only ever rebuilt by selecting something or by the
/// Feed menu's explicit per-feed refresh. So a reader sat watching a feed
/// while its count went from 12 to 19 and the list in front of them did not
/// change, and could not be made to change short of clicking away and back.
///
/// THE RULE THAT SHAPES ALL OF THIS: nothing may move under the reader.
///
/// Inserting a row above the viewport shifts everything below it, so an
/// article being read slides down the screen mid-sentence. That is worse than
/// being slightly out of date. So new rows are inserted immediately only when
/// the list is already scrolled to the top, where there is nothing above the
/// viewport to shift. Anywhere else they are held, counted, and offered: the
/// strip above the list says how many are waiting, and clicking it brings
/// them in and returns to the top, which is a move the reader asked for.
///
/// Insert-only, never remove or reorder. An article that has left the query -
/// because it was read, or pruned, or the filter no longer matches it - stays
/// on screen until the next real load. Taking a row out from under someone is
/// the same broken promise as moving one, and a list that is briefly one item
/// generous is not a problem worth causing that for.
/// </summary>
public partial class MainWindow
{
    private Action<FeedRefreshOutcome>? _onRefreshCompletedForLiveUpdate;

    /// <summary>
    /// Rows that have arrived while the reader was scrolled away from the top,
    /// in the order they should appear. Held rather than inserted; see the
    /// class summary.
    /// </summary>
    private readonly List<ItemRow> _pendingRows = [];

    private ScrollViewer? _itemListScroll;

    /// <summary>
    /// How close to the top counts as the top. Not an equality test against
    /// zero: a list can sit a fraction of a pixel off after a resize or a
    /// scroll animation settles, and refusing to insert because of half a
    /// pixel would send every update into the pending strip instead.
    /// </summary>
    private const double AtTopTolerance = 4.0;

    public int PendingNewCount => _pendingRows.Count;

    public bool HasPendingNew => _pendingRows.Count > 0;

    public string PendingNewLabel => _pendingRows.Count == 1
        ? "1 new article"
        : $"{_pendingRows.Count} new articles";

    private void StartLiveUpdates()
    {
        _onRefreshCompletedForLiveUpdate ??= OnRefreshCompletedForLiveUpdate;
        _services.Refresh.Completed += _onRefreshCompletedForLiveUpdate;
    }

    private void StopLiveUpdates()
    {
        if (_onRefreshCompletedForLiveUpdate is not null)
            _services.Refresh.Completed -= _onRefreshCompletedForLiveUpdate;
    }

    /// <summary>
    /// Arrives on the refresh pool's threads, so everything real happens on
    /// the UI thread. Only a refresh that actually stored something can change
    /// what the list should show.
    /// </summary>
    private void OnRefreshCompletedForLiveUpdate(FeedRefreshOutcome outcome)
    {
        if (!outcome.Success || outcome.NewItemCount <= 0) return;

        Dispatcher.UIThread.Post(() => _ = MergeNewItemsAsync());
    }

    /// <summary>
    /// Re-runs the current query and brings in whatever the list does not
    /// already have.
    ///
    /// The query is BuildQuery(), the same one LoadItemsAsync uses, which is
    /// what makes this work for every scope rather than only for a feed: a
    /// folder, All items, Unread and a tag all describe themselves through it,
    /// so an article arriving in any feed those cover is picked up without
    /// this method knowing anything about which is selected.
    /// </summary>
    private async Task MergeNewItemsAsync()
    {
        // Search results answer a query the user typed, not the feed tree's
        // selection, and quietly folding fresh articles into them would change
        // what the results claim to be.
        if (IsShowingSearchResults) return;

        var items = await _services.Items.QueryAsync(BuildQuery());
        var feeds = (await _services.Feeds.GetAllAsync())
            .ToDictionary(f => f.Id, f => f.DisplayTitle);
        var now = DateTimeOffset.UtcNow;

        var onScreen = ItemRows.Select(r => r.Id).ToHashSet();
        var alreadyPending = _pendingRows.Select(r => r.Id).ToHashSet();

        // Position matters: an article's place in the list is decided by the
        // query's ordering, so each new row remembers the index it should sit
        // at and they are applied in order.
        var arrivals = new List<(int Index, ItemRow Row)>();

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (onScreen.Contains(item.Id) || alreadyPending.Contains(item.Id)) continue;

            arrivals.Add((index, new ItemRow
            {
                Item = item,
                FeedName = feeds.GetValueOrDefault(item.FeedId, "Unknown feed"),
                IsRead = item.IsRead,
                IsStarred = item.IsStarred,
                RelativeDate = ItemRow.FormatRelative(
                    item.PublishedUtc ?? item.FirstSeenUtc, now),
                Snippet = Snippet.FromMarkdown(item.ContentMarkdown, item.Summary),
                IsNewArrival = true
            }));
        }

        if (arrivals.Count == 0) return;

        if (IsItemListAtTop())
        {
            InsertArrivals(arrivals);
            return;
        }

        _pendingRows.AddRange(arrivals.Select(a => a.Row));
        RaisePendingNew();
    }

    /// <summary>
    /// Puts rows into the list at the positions the query gave them.
    ///
    /// One Insert per row rather than a wholesale replace, deliberately and
    /// against the batching used everywhere else here: a Reset tells the
    /// ListBox its contents are unrecognisable, which throws away the scroll
    /// position and the container for the selected row. An Insert is the
    /// notification that says "one row appeared, here", which is the only kind
    /// that keeps the rest of the list where it is. There are a handful of
    /// these per refresh, not five hundred.
    /// </summary>
    private void InsertArrivals(IReadOnlyList<(int Index, ItemRow Row)> arrivals)
    {
        foreach (var (index, row) in arrivals)
            ItemRows.Insert(Math.Clamp(index, 0, ItemRows.Count), row);

        StatusMessage = arrivals.Count == 1
            ? "1 new article."
            : $"{arrivals.Count} new articles.";

        // The thumbnails for rows that just appeared, and only those.
        _ = ResolveThumbnailsAsync(arrivals.Select(a => a.Row).ToList());
    }

    /// <summary>
    /// Brings the held rows in and returns to the top. Wired to the strip
    /// above the list; this is the reader asking for the move, which is what
    /// makes moving the content acceptable here and not in MergeNewItemsAsync.
    /// </summary>
    public void ShowPendingNewItems()
    {
        if (_pendingRows.Count == 0) return;

        // Re-derived rather than trusting the indexes captured when these
        // arrived: rows may have been inserted since, and an index from an
        // older query would put an article in the wrong place.
        var arrivals = _pendingRows.Select((row, i) => (Index: i, Row: row)).ToList();
        _pendingRows.Clear();

        InsertArrivals(arrivals);
        RaisePendingNew();

        ItemListScroll()?.ScrollToHome();
    }

    /// <summary>
    /// Whether the list is scrolled to the top, and so whether an insert can
    /// happen without moving anything the reader is looking at.
    ///
    /// A list too short to scroll is at the top by definition, and so is one
    /// whose ScrollViewer has not been realised yet, which is the state during
    /// the first load.
    /// </summary>
    private bool IsItemListAtTop()
    {
        var scroll = ItemListScroll();
        if (scroll is null) return true;

        return scroll.Offset.Y <= AtTopTolerance;
    }

    /// <summary>
    /// The ListBox's own ScrollViewer, found once through the visual tree.
    ///
    /// Not cached in a field until it is found: the control template is not
    /// realised until the list has been laid out, so an early lookup returns
    /// null and caching that null would mean never finding it at all.
    /// </summary>
    private ScrollViewer? ItemListScroll() =>
        _itemListScroll ??= ItemList?.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();

    private void RaisePendingNew()
    {
        Raise(nameof(PendingNewCount));
        Raise(nameof(HasPendingNew));
        Raise(nameof(PendingNewLabel));
    }

    /// <summary>
    /// Drops anything held. Called when the list is about to be rebuilt for
    /// another reason, since rows held for the previous scope have no meaning
    /// in the next one.
    /// </summary>
    private void ClearPendingNewItems()
    {
        if (_pendingRows.Count == 0) return;

        _pendingRows.Clear();
        RaisePendingNew();
    }

    private void OnShowPendingNewClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ShowPendingNewItems();
}
