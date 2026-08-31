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
            if (SelectedFeedNode?.FeedId is not { } feedId) return;

            if (!_services.Refresh.TryQueue(feedId, isManual: true))
            {
                // Either the feed is already in flight or the queue is full.
                // Neither is an error, and neither is a refresh, so it must not
                // claim to be one.
                StatusMessage = "That feed is already being refreshed, or refresh is busy right now.";
                RefreshFeedUpdateLine();
                return;
            }

            StatusMessage = "Refreshing this feed...";
            RefreshFeedUpdateLine();
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not refresh this feed: " + ex.Message;
        }
    }
}
