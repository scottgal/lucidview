using LucidReader.Core.Storage;

namespace LucidReader.Core.Sync;

/// <summary>
/// A plain timer over one SQL query. Ephemeral's ScheduledTasks atom is not
/// used here on purpose: the whole scheduling rule is "next_due_utc has
/// passed", which the database answers better than a scheduler would.
/// </summary>
public sealed class RefreshScheduler(
    FeedRepository feeds,
    FeedRefreshService refresh,
    TimeProvider timeProvider,
    TimeSpan? tickInterval = null) : IAsyncDisposable
{
    private const int MaxFeedsPerTick = 200;

    /// <summary>
    /// How long StopAsync/DisposeAsync waits for a tick already running on
    /// the timer thread to unwind before tearing down the token it reads
    /// from. Bounded so a hung tick (a stuck query, a stalled connection)
    /// cannot block shutdown forever; it just means that one tick's feeds
    /// are dropped, same as before this bound existed, instead of the whole
    /// app hanging on close.
    /// </summary>
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _interval = tickInterval ?? TimeSpan.FromMinutes(1);

    private CancellationTokenSource _stopping = new();
    private ITimer? _timer;
    private Task _activeTick = Task.CompletedTask;

    // 0 = idle, 1 = a tick is currently running. Guards against ticks
    // overlapping: a slow GetDueAsync under lock contention must not let
    // ticks stack up unbounded, so a tick that arrives while one is still
    // running is skipped rather than queued behind it.
    private int _ticking;

    private bool _disposed;

    public bool IsRunning => _timer is not null;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_timer is not null) return;

        _timer = timeProvider.CreateTimer(
            OnTimerTick,
            null,
            _interval,
            _interval);
    }

    /// <summary>
    /// Stops ticking. Restart-safe by design: a later Start() works again,
    /// because the cancellation token ticks read from is replaced here
    /// rather than left cancelled forever. The alternative - leaving Start()
    /// unusable after a Stop() and throwing on the next call - would trade
    /// one obvious failure for a worse one: IsRunning would report true
    /// while every tick threw OperationCanceledException on the stale token
    /// and got silently swallowed, so refreshing would look alive but never
    /// queue anything again. That is exactly the silent-failure mode this
    /// class exists to prevent, just reached by restart instead of by an
    /// unhandled exception, so restart has to actually work.
    /// </summary>
    public async Task StopAsync()
    {
        if (_disposed) return;

        await StopTimerAndWaitForActiveTickAsync();

        var old = _stopping;
        _stopping = new CancellationTokenSource();
        old.Dispose();
    }

    /// <summary>
    /// Queues every feed whose next_due_utc has passed. Returns how many were
    /// actually queued, which is fewer than were due when some are already in
    /// flight from a manual refresh.
    /// </summary>
    public async Task<int> TickAsync(CancellationToken ct = default)
    {
        var due = await feeds.GetDueAsync(timeProvider.GetUtcNow(), MaxFeedsPerTick, ct);

        var queued = 0;
        foreach (var feed in due)
            if (refresh.TryQueue(feed.Id))
                queued++;

        return queued;
    }

    private void OnTimerTick(object? state)
    {
        // Only one tick runs at a time. If the previous tick (or its
        // shutdown-time wait) hasn't finished, this firing is skipped
        // outright rather than piling up behind it.
        if (Interlocked.CompareExchange(ref _ticking, 1, 0) != 0)
            return;

        _activeTick = TickSafelyAsync();
    }

    private async Task TickSafelyAsync()
    {
        try
        {
            await TickAsync(_stopping.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception)
        {
            // A tick that throws must not kill the timer, or refreshing stops
            // silently for the rest of the session. The next tick a minute
            // later gets a clean attempt.
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    private async Task StopTimerAndWaitForActiveTickAsync()
    {
        if (_timer is not null)
        {
            await _timer.DisposeAsync();
            _timer = null;
        }

        // Cancel any tick in flight, then give it a bounded window to
        // unwind before the token source it reads from is torn down.
        // Without this a tick mid GetDueAsync throws ObjectDisposedException
        // on the token, which TickSafelyAsync's catch swallows silently and
        // any feeds it was about to queue are dropped with no trace.
        //
        // Deliberately TimeProvider.System here, not the injected
        // `timeProvider` used everywhere else in this class. This bound is
        // a real-world shutdown grace period, not part of the scheduling
        // domain the injected clock models - it has to elapse on the wall
        // clock even when the caller supplied a FakeTimeProvider that never
        // advances on its own, or a genuinely hung tick would make this
        // wait, and so StopAsync/DisposeAsync, never return.
        await _stopping.CancelAsync();
        await Task.WhenAny(_activeTick, Task.Delay(ShutdownWait, TimeProvider.System));
    }

    public async ValueTask DisposeAsync()
    {
        // Idempotent: a second DisposeAsync (defensive cleanup in a finally,
        // a double `await using`) must not throw, it should just be a no-op.
        if (_disposed) return;
        _disposed = true;

        await StopTimerAndWaitForActiveTickAsync();
        _stopping.Dispose();
    }
}
