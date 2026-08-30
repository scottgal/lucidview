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
    /// Cancelled from the window's Closing handler. Checked after the store
    /// read inside CheckHealthAsync, which is the only await a tick suspends
    /// on and so the only place a continuation can come back to a disposing
    /// ReaderServices.
    /// </summary>
    private readonly CancellationTokenSource _healthCancellation = new();

    /// <summary>
    /// Health reads that throw are swallowed so a failed readout cannot kill
    /// the timer or the app, but swallowing every one of them means a store
    /// that fails on every tick leaves health reporting dead for the whole
    /// session with no trace at all. The first failure is written out so
    /// there is something to find.
    /// </summary>
    private bool _healthFailureReported;

    /// <summary>
    /// Ticks must not overlap. The interval is 30 seconds and a healthy read
    /// is far quicker than that, but a store that has gone slow (a wedged
    /// writer, a database on a stalled network volume) would otherwise stack
    /// one GetAllAsync on top of another every 30 seconds for the rest of the
    /// session. 0 means idle, 1 means a tick is in flight.
    /// </summary>
    private int _healthTickRunning;

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
    /// <param name="isExpectedToRun">
    /// False when the user turned off "Refresh on startup" in settings, which
    /// is the only reason ReaderServices never calls Scheduler.Start(). A
    /// stopped scheduler is then the setting working, not a fault, and saying
    /// otherwise every 30 seconds would stamp over every status line the
    /// user's own actions produce and make the status bar useless.
    /// </param>
    internal static string DescribeHealth(
        bool isExpectedToRun,
        bool isRunning,
        string? lastTickError,
        int consecutiveFailures,
        int autoPausedCount)
    {
        var parts = new List<string>();

        if (!isRunning)
        {
            if (isExpectedToRun) parts.Add("Background refresh is not running.");
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
        // Stopping the timer only prevents future ticks. A tick already
        // suspended on the GetAllAsync await would otherwise resume after the
        // window closed and touch a ReaderServices App.axaml.cs is disposing,
        // which is the exact window this call is meant to close. Cancelling
        // gives the continuation something to check before it does that.
        _healthCancellation.Cancel();

        if (_healthTimer is null) return;

        _healthTimer.Stop();
        _healthTimer.Tick -= OnHealthTimerTick;
        _healthTimer = null;
    }

    /// <summary>
    /// Releases the health cancellation source itself. Separate from
    /// StopHealthMonitoring, which only has to make in-flight ticks observable
    /// as cancelled: this runs from PrepareForShutdown once, after the token
    /// has already been cancelled and no further tick can start.
    /// </summary>
    private void DisposeHealthMonitoring() => _healthCancellation.Dispose();

    /// <summary>
    /// A DispatcherTimer Tick handler is a void-returning event, so anything
    /// thrown past it lands on the dispatcher as an unhandled exception and
    /// takes the app down. A health readout is never worth that, hence the
    /// catch-all: the next tick will try again.
    /// </summary>
    private async void OnHealthTimerTick(object? sender, EventArgs e)
    {
        try
        {
            // Ahead of the overlap guard, because this is the one thing on the
            // tick that does not await: it reads fields already in memory and
            // writes bound properties on the UI thread, so a slow health read
            // must not be able to stop the clock on the per-feed update line.
            // Sharing this timer rather than starting a second DispatcherTimer
            // is what keeps "Updated 4 min ago" honest with no extra timer to
            // start, stop and leak.
            RefreshFeedUpdateLine();

            if (Interlocked.Exchange(ref _healthTickRunning, 1) != 0) return;

            try
            {
                await CheckHealthAsync();
            }
            finally
            {
                Volatile.Write(ref _healthTickRunning, 0);
            }
        }
        catch (OperationCanceledException)
        {
            // The window closed while this read was in flight. Expected.
        }
        catch (Exception ex)
        {
            // A failed health read must not kill the timer or the app, but it
            // must not vanish either: report the first one so a persistently
            // failing store is diagnosable.
            if (!_healthFailureReported)
            {
                _healthFailureReported = true;
                Console.Error.WriteLine($"[Health] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public async Task CheckHealthAsync()
    {
        var token = _healthCancellation.Token;

        // The token is passed, not just checked afterwards: a read that is
        // already in flight when the window closes should be given the chance
        // to unwind rather than run to completion against a store the app is
        // disposing.
        var pausedCount = (await _services.Feeds.GetAllAsync(token))
            .Count(f => f.AutoPausedUtc is not null);

        // The window may have closed while that read was in flight.
        if (token.IsCancellationRequested) return;

        var text = DescribeHealth(
            _services.Settings.RefreshOnStartup,
            _services.Scheduler.IsRunning,
            _services.Scheduler.LastTickError,
            _services.Scheduler.ConsecutiveTickFailures,
            pausedCount);

        // Do not stamp over a message the user's own action just produced.
        if (text.Length > 0) StatusMessage = text;
    }

    /// <summary>
    /// True when the sidebar selection is a feed the Core layer auto-paused.
    /// Drives the Resume toolbar button's visibility. Raised from
    /// SelectedFeedNode's setter, and again from the end of LoadFeedTreeAsync
    /// (see RepointSelectionAfterTreeReload), because a reload both replaces
    /// the node object the selection points at and can change the answer for
    /// the feed that is still selected: a background auto-pause makes it true,
    /// a resume or a successful refresh makes it false.
    /// </summary>
    public bool IsPausedFeedSelected => SelectedFeedNode?.IsAutoPaused == true;

    private async void OnResumeFeedClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (NodeFromSender(sender)?.FeedId is { } feedId)
            await RunGuardedAsync(() => ResumeFeedAsync(feedId), "resume this feed");
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
        // Guarded like every other async void handler here, and this one
        // matters most: Resume is the button a user presses precisely because
        // feeds are already failing, so it is the likeliest of all of them to
        // throw.
        if (SelectedFeedNode?.FeedId is { } feedId)
            await RunGuardedAsync(() => ResumeFeedAsync(feedId), "resume this feed");
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

        // LoadFeedTreeAsync builds brand new FeedTreeNode instances and
        // re-points the selection at the fresh row for the same feed, so the
        // Resume button reads the resumed state rather than the pre-resume
        // snapshot. Items are then loaded here, before the confirmation is
        // written: LoadItemsAsync ends by setting StatusMessage itself, so
        // writing the confirmation first would let the article count replace
        // it as soon as the query completed. Same shape as AfterRefreshAsync.
        await LoadFeedTreeAsync();
        await LoadItemsAsync();

        StatusMessage = queued
            ? "Feed resumed."
            : "Feed resumed. Refresh is busy, so it will be picked up on the next pass.";
    }
}
