using Avalonia.Threading;

namespace LucidReader.Views;

/// <summary>
/// Refresh health (Task 14). Plan 1 added RefreshScheduler.LastTickError and
/// ConsecutiveTickFailures precisely so the shell could tell the difference
/// between "background refresh is working" and "a timer exists and every tick
/// it fires throws". IsRunning alone only proves the second thing. Nothing
/// read those two properties until this file, and a feed the Core layer
/// auto-paused after repeated failures had no route back into rotation.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _healthTimer;

    /// <summary>
    /// How many consecutive failing ticks before the user is told. One blip is
    /// noise; a streak means background refresh has genuinely stopped working.
    /// </summary>
    private const int TickFailureThreshold = 3;

    /// <summary>
    /// How often the shell re-reads scheduler health and the auto-paused feed
    /// count. Long enough to cost nothing, short enough that a user who walks
    /// away and comes back sees the truth rather than a stale line.
    /// </summary>
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The whole of the health wording, kept as a static pure function so it
    /// can be unit-tested without constructing a Window (see
    /// LucidReader.Core.Tests/Ui/HealthTests.cs). Returns an empty string when
    /// there is nothing to report, which is what lets the caller leave a
    /// status line the user's own action just produced alone.
    /// </summary>
    internal static string DescribeHealth(
        bool isRunning,
        string? lastTickError,
        int consecutiveFailures,
        int autoPausedCount)
    {
        var parts = new List<string>();

        if (!isRunning)
        {
            parts.Add("Background refresh is not running.");
        }
        else if (consecutiveFailures >= TickFailureThreshold)
        {
            // IsRunning being true says only that a timer exists. This is the
            // case Plan 1 added the counters for: the loop is alive and every
            // tick is throwing, so nothing is actually being refreshed.
            parts.Add($"Background refresh is failing ({consecutiveFailures} attempts): {lastTickError}");
        }

        if (autoPausedCount == 1)
            parts.Add("1 feed was paused after repeated failures.");
        else if (autoPausedCount > 1)
            parts.Add($"{autoPausedCount} feeds were paused after repeated failures.");

        return string.Join("  ", parts);
    }

    /// <summary>
    /// A DispatcherTimer rather than a background loop: CheckHealthAsync
    /// writes StatusMessage, which is bound, so it has to run on the UI thread
    /// anyway. Stopped from the window's Closing handler.
    /// </summary>
    private void StartHealthMonitoring()
    {
        if (_healthTimer is not null) return;

        _healthTimer = new DispatcherTimer { Interval = HealthCheckInterval };
        _healthTimer.Tick += OnHealthTimerTick;
        _healthTimer.Start();
    }

    private void StopHealthMonitoring()
    {
        if (_healthTimer is null) return;

        _healthTimer.Stop();
        _healthTimer.Tick -= OnHealthTimerTick;
        _healthTimer = null;
    }

    /// <summary>
    /// A DispatcherTimer Tick handler is a void-returning event, so anything
    /// thrown past it lands on the dispatcher as an unhandled exception and
    /// takes the app down. A health readout is never worth that, hence the
    /// catch-all: the next tick will try again.
    /// </summary>
    private async void OnHealthTimerTick(object? sender, EventArgs e)
    {
        try { await CheckHealthAsync(); }
        catch (Exception) { /* a failed health read must not kill the timer or the app */ }
    }

    public async Task CheckHealthAsync()
    {
        var pausedCount = (await _services.Feeds.GetAllAsync())
            .Count(f => f.AutoPausedUtc is not null);

        var text = DescribeHealth(
            _services.Scheduler.IsRunning,
            _services.Scheduler.LastTickError,
            _services.Scheduler.ConsecutiveTickFailures,
            pausedCount);

        // Do not stamp over a message the user's own action just produced.
        if (text.Length > 0) StatusMessage = text;
    }

    /// <summary>
    /// True when the sidebar selection is a feed the Core layer auto-paused.
    /// Drives the Resume toolbar button's visibility; raised from
    /// SelectedFeedNode's setter and again after every tree reload, since a
    /// resume changes the answer for the node that is still selected.
    /// </summary>
    public bool IsPausedFeedSelected => SelectedFeedNode?.IsAutoPaused == true;

    private async void OnResumeFeedClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (NodeFromSender(sender)?.FeedId is { } feedId) await ResumeFeedAsync(feedId);
    }

    /// <summary>
    /// Toolbar route to the same action. The context menu opens into its own
    /// PopupRoot, which the UI test harness can neither click nor capture, so
    /// resume would otherwise be permanently unverifiable - the same reason
    /// Task 12 gave per-feed settings a toolbar button. NodeFromSender is no
    /// use here: this button's DataContext is the window, not a row.
    /// </summary>
    private async void OnToolbarResumeFeedClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedFeedNode?.FeedId is { } feedId) await ResumeFeedAsync(feedId);
    }

    /// <summary>
    /// Puts an auto-paused feed back into rotation. Must go through
    /// SetEnabledAsync, which clears the failure count and the pause stamp:
    /// re-enabling without clearing them means the very next failure pauses
    /// the feed again, so the user gets exactly one attempt.
    /// </summary>
    public async Task ResumeFeedAsync(long feedId)
    {
        await _services.Feeds.SetEnabledAsync(feedId, true);

        // TryQueue returns false when the feed is already in flight or the
        // queue is full. That is not a failure to resume - the row is already
        // enabled and unpaused - but saying "refreshing" when nothing was
        // queued would be a lie, so the two cases word themselves differently.
        var queued = _services.Refresh.TryQueue(feedId, isManual: true);

        var wasSelected = SelectedFeedNode?.FeedId == feedId;
        await LoadFeedTreeAsync();

        // LoadFeedTreeAsync builds brand new FeedTreeNode instances, so the
        // node SelectedFeedNode still points at is the pre-resume snapshot:
        // it reports IsAutoPaused true forever and would keep the Resume
        // button on screen for a feed that is no longer paused. Re-point the
        // selection at the fresh row for the same feed.
        if (wasSelected)
        {
            SelectedFeedNode = AllFeedTreeNodes.FirstOrDefault(n => n.FeedId == feedId);
            Raise(nameof(IsPausedFeedSelected));
        }

        StatusMessage = queued
            ? "Feed resumed."
            : "Feed resumed. Refresh is busy, so it will be picked up on the next pass.";
    }
}
