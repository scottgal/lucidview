using Avalonia.Threading;
using LucidReader.Core.Sync;
using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// Says which feeds are refreshing and which ones failed.
///
/// Before this, a refresh was invisible. "Refresh all" put "Refresh started."
/// on the status line and then nothing ever replaced it: no sign of which
/// feeds were being fetched, no sign of when it finished, and a feed that
/// failed said so only as a "!" in the sidebar that appeared silently and
/// looked identical whether it had just happened or had been there for a
/// week. Watching a refresh, the honest summary of the old behaviour is that
/// you could not tell the difference between "working" and "did nothing".
///
/// Two surfaces, deliberately different in what they answer:
///
///   - The sidebar answers "is THIS feed busy", per row, because that is the
///     question you have while looking at a specific subscription.
///   - The status line answers "what is happening overall", because a
///     Refresh All over two hundred feeds cannot be read a row at a time.
///
/// Both are driven from FeedRefreshService, which is the only thing that
/// knows: InFlightChanged for the set of feeds being worked on, and Completed
/// for how each one turned out.
/// </summary>
public partial class MainWindow
{
    private Action? _onInFlightChanged;
    private Action<FeedRefreshOutcome>? _onRefreshCompletedForProgress;

    /// <summary>
    /// Feeds whose most recent attempt failed, in the order they failed,
    /// gathered across one burst of refreshing and reported once it goes
    /// quiet.
    ///
    /// A list rather than a running count because the status line names the
    /// first failure. "2 feeds failed" tells a user something is wrong but
    /// not what to look at; "BBC News failed, and 1 other" points at a row.
    ///
    /// Cleared when a burst begins, not when one ends, so the summary stays
    /// on screen after the work stops instead of being wiped by its own
    /// completion.
    /// </summary>
    private readonly List<string> _failuresThisBurst = [];

    private bool _refreshBurstRunning;

    private void StartRefreshProgress()
    {
        _onInFlightChanged ??= OnInFlightChanged;
        _onRefreshCompletedForProgress ??= OnRefreshCompletedForProgress;

        _services.Refresh.InFlightChanged += _onInFlightChanged;
        _services.Refresh.Completed += _onRefreshCompletedForProgress;
    }

    private void StopRefreshProgress()
    {
        if (_onInFlightChanged is not null)
            _services.Refresh.InFlightChanged -= _onInFlightChanged;

        if (_onRefreshCompletedForProgress is not null)
            _services.Refresh.Completed -= _onRefreshCompletedForProgress;
    }

    /// <summary>
    /// Both events arrive on the refresh pool's threads, so everything they
    /// lead to is posted rather than run inline: FeedTreeNode raises
    /// PropertyChanged straight into Avalonia's binding system, and
    /// StatusMessage is bound to a TextBlock.
    /// </summary>
    private void OnInFlightChanged() =>
        Dispatcher.UIThread.Post(SyncRefreshState);

    private void OnRefreshCompletedForProgress(FeedRefreshOutcome outcome)
    {
        if (outcome.Success) return;

        // Resolved to a name here, on the pool thread, from the tree that is
        // already in memory. Doing it when the summary is composed instead
        // would be reading a tree that a completed refresh may already have
        // rebuilt, and an unsubscribed feed would come back as "Unknown".
        var name = AllFeedTreeNodes
            .FirstOrDefault(n => n.FeedId == outcome.FeedId)?.Title;

        Dispatcher.UIThread.Post(() =>
        {
            _failuresThisBurst.Add(name ?? "A feed");
            SyncRefreshState();
        });
    }

    /// <summary>
    /// Brings the sidebar markers and the status line in line with what is
    /// actually in flight right now.
    ///
    /// Reads IsInFlight per row rather than tracking the transitions itself.
    /// The set changes on several threads and a row can be queued, started
    /// and finished between two events; asking for the current answer at the
    /// moment of the redraw cannot drift, whereas a tally of increments and
    /// decrements can and eventually will.
    /// </summary>
    private void SyncRefreshState()
    {
        var refreshing = 0;

        foreach (var node in AllFeedTreeNodes)
        {
            if (node.FeedId is not { } feedId) continue;

            var busy = _services.Refresh.IsInFlight(feedId);
            node.IsRefreshing = busy;
            if (busy) refreshing++;
        }

        if (refreshing > 0)
        {
            // A burst is starting. Clear whatever the last one reported, so a
            // failure from ten minutes ago is not shown beside a refresh
            // happening now.
            if (!_refreshBurstRunning)
            {
                _refreshBurstRunning = true;
                _failuresThisBurst.Clear();
            }

            StatusMessage = DescribeInProgress(refreshing, _failuresThisBurst.Count);
            return;
        }

        // Nothing in flight. Only say so if there was something to finish:
        // this method also runs when a queue attempt is refused for a feed
        // already in flight, and a reader sitting idle should not have its
        // status line overwritten by a refresh that is not happening.
        if (!_refreshBurstRunning) return;

        _refreshBurstRunning = false;
        StatusMessage = DescribeFinished(_failuresThisBurst);
    }

    private static string DescribeInProgress(int refreshing, int failed)
    {
        var line = refreshing == 1
            ? "Refreshing 1 feed..."
            : $"Refreshing {refreshing} feeds...";

        // Failures are surfaced while the burst is still running rather than
        // only at the end. Over two hundred feeds the end is a long way off,
        // and a user watching a refresh stall wants to know that things are
        // already going wrong.
        return failed == 0 ? line : $"{line} {failed} failed so far.";
    }

    /// <summary>
    /// The line left on screen once a burst finishes. Names the first failure
    /// rather than only counting them, because a count is not actionable and
    /// a name is: it is the row to go and look at.
    /// </summary>
    private static string DescribeFinished(IReadOnlyList<string> failures) => failures.Count switch
    {
        0 => "Refresh finished.",
        1 => $"Refresh finished. {failures[0]} failed.",
        2 => $"Refresh finished. {failures[0]} and 1 other feed failed.",
        _ => $"Refresh finished. {failures[0]} and {failures.Count - 1} other feeds failed."
    };
}
