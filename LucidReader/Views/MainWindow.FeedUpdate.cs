using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// The per-feed update line at the top of the item-list column: when the
/// selected feed last updated, when it is next due, and a way to refresh it now.
///
/// One quiet line, not a second toolbar. The unified toolbar at the top of the
/// window already carries six controls and the search box, and the thing being
/// described here is not the window, it is the feed whose articles are in the
/// column below - so it belongs at the top of that column, next to them.
///
/// All the wording lives in LucidReader.Models.FeedUpdateSummary, which is
/// plain and testable. This file is only the wiring: three bound properties, a
/// recompute, and the button.
/// </summary>
public partial class MainWindow
{
    private FeedUpdateLine _feedUpdate = FeedUpdateLine.Hidden;

    /// <summary>
    /// Bound to the line's TextBlock. Compiled bindings are off in this
    /// project, so a binding naming a property that does not exist fails
    /// silently: these three names and the ones in MainWindow.axaml have to
    /// match exactly.
    /// </summary>
    public string FeedUpdateText => _feedUpdate.Text;

    /// <summary>
    /// What the line actually renders. The full sentence is the tooltip: see
    /// FeedUpdateSummary for why the visible form is abbreviated.
    /// </summary>
    public string FeedUpdateShortText => _feedUpdate.ShortText;

    public bool IsFeedUpdateVisible => _feedUpdate.IsVisible;

    public bool CanRefreshSelectedFeed => _feedUpdate.CanRefresh;

    /// <summary>
    /// Whether the refresh control should be offered for whatever is selected,
    /// which is a broader question than <see cref="CanRefreshSelectedFeed"/>.
    ///
    /// Refreshing was offered for a single feed and nowhere else, so the
    /// reader looking at All items, at a folder, at Unread or at a tag - which
    /// is where most reading actually happens - had no way to say "fetch what
    /// I am looking at" short of Refresh all, which fetches every subscription
    /// whether or not it is in view. A grouping row describes a set of feeds
    /// perfectly well; there was no reason it could not be refreshed as one.
    ///
    /// Everything except a null selection qualifies. A tag included: it spans
    /// feeds rather than naming one, and refreshing the feeds its articles
    /// came from is still the useful reading of "refresh this".
    /// </summary>
    public bool CanRefreshSelection => SelectedFeedNode is not null;

    /// <summary>
    /// The strip is shown when there is a per-feed line to show OR a refresh
    /// control to offer, so a grouping row gets the button without inventing a
    /// "last updated" sentence that no set of feeds has a single answer for.
    /// </summary>
    public bool IsFeedUpdateStripVisible => IsFeedUpdateVisible || CanRefreshSelection;

    /// <summary>
    /// The feeds the current selection covers, which is what its refresh
    /// control acts on.
    ///
    /// A folder is its own feeds. Everything else - the smart rows and a tag -
    /// spans every subscription, so that is what they refresh. Disabled feeds
    /// are left out throughout: they are switched off, and a manual refresh of
    /// a group is not the place to override that.
    /// </summary>
    private async Task<IReadOnlyList<long>> FeedsInSelectionAsync()
    {
        if (SelectedFeedNode is not { } node) return [];

        if (node.FeedId is { } single) return [single];

        var feeds = await _services.Feeds.GetAllAsync();

        return node.Kind == FeedTreeNodeKind.Folder
            ? feeds.Where(f => f.IsEnabled && f.FolderId == node.FolderId).Select(f => f.Id).ToList()
            : feeds.Where(f => f.IsEnabled).Select(f => f.Id).ToList();
    }

    /// <summary>
    /// Recomputes the line and raises only what changed.
    ///
    /// Called from four places, and it needs all four: the selection changing,
    /// a tree reload (which is what replaces the node the timestamps are read
    /// from), the end of a refresh, and the health timer's tick. The timer is
    /// what makes "4 min ago" become "5 min ago" with nobody touching
    /// anything, and reusing it rather than adding a second DispatcherTimer is
    /// deliberate: the health timer is already started on open and already
    /// stopped in PrepareForShutdown, so this cannot outlive the window or leak
    /// a timer of its own. Thirty seconds is finer than the minute this line
    /// resolves to, so nothing is lost by sharing it.
    /// </summary>
    private void RefreshFeedUpdateLine()
    {
        var node = SelectedFeedNode;
        var isFeed = node?.FeedId is not null;

        var next = FeedUpdateSummary.Describe(
            isFeedSelected: isFeed,
            isRefreshing: isFeed && _services.Refresh.IsInFlight(node!.FeedId!.Value),
            isAutoPaused: node?.IsAutoPaused == true,
            isEnabled: node?.IsEnabled != false,
            lastFetchedUtc: node?.LastFetchedUtc,
            lastSuccessUtc: node?.LastSuccessUtc,
            lastError: node?.LastError,
            nextDueUtc: node?.NextDueUtc,
            now: DateTimeOffset.UtcNow,
            isScraped: node?.IsScraped == true);

        if (next == _feedUpdate) return;

        var previous = _feedUpdate;
        _feedUpdate = next;

        if (previous.Text != next.Text) Raise(nameof(FeedUpdateText));
        if (previous.ShortText != next.ShortText) Raise(nameof(FeedUpdateShortText));
        if (previous.IsVisible != next.IsVisible) Raise(nameof(IsFeedUpdateVisible));
        if (previous.CanRefresh != next.CanRefresh) Raise(nameof(CanRefreshSelectedFeed));

        // The strip's visibility is the OR of the line's and the refresh
        // control's, so it has to be raised whenever the line's changes too.
        if (previous.IsVisible != next.IsVisible) Raise(nameof(IsFeedUpdateStripVisible));
    }

    /// <summary>
    /// The manual per-feed refresh. Goes through the same TryQueue every other
    /// refresh in the app uses rather than fetching inline, so a feed already
    /// queued is not fetched twice and a full queue is reported as such instead
    /// of being silently dropped.
    ///
    /// RefreshNowAsync (the context menu's route) is deliberately not used
    /// here: it blocks on the fetch, and this control's whole point is that the
    /// line goes to "Refreshing now..." and comes back on its own.
    /// </summary>
    private async void OnFeedUpdateRefreshClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Guarded like every other async void handler in this window: an
        // exception escaping one lands on the synchronization context unhandled
        // and takes the process down.
        try
        {
            var feedIds = await FeedsInSelectionAsync();
            if (feedIds.Count == 0)
            {
                StatusMessage = "There is nothing to refresh here.";
                return;
            }

            // Counted rather than assumed: TryQueue returns false for a feed
            // already in flight, which is not a failure and not a refresh
            // either, so a group where everything is already running must not
            // claim to have started anything.
            var queued = feedIds.Count(id => _services.Refresh.TryQueue(id, isManual: true));

            if (queued == 0)
            {
                StatusMessage = feedIds.Count == 1
                    ? "That feed is already being refreshed, or refresh is busy right now."
                    : "Those feeds are already being refreshed, or refresh is busy right now.";
                RefreshFeedUpdateLine();
                return;
            }

            StatusMessage = queued == 1
                ? "Refreshing this feed..."
                : $"Refreshing {queued} feeds...";
            RefreshFeedUpdateLine();
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not refresh this feed: " + ex.Message;
        }
    }
}
