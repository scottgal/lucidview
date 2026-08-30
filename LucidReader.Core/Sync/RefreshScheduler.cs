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
    /// <summary>
    /// How long StopAsync/DisposeAsync waits for a tick already running on
    /// the timer thread to unwind before tearing down the token it reads
    /// from. Bounded so a hung tick (a stuck query, a stalled connection)
    /// cannot block shutdown forever; it just means that one tick's feeds
    /// are dropped, same as before this bound existed, instead of the whole
    /// app hanging on close.
    ///
    /// Narrow edge case worth knowing about: if a tick is still running when
    /// this bound elapses, shutdown returns anyway while that tick keeps
    /// running in the background. A fast Stop-then-Start after that leaves
    /// two ticks alive at once - the old one, still unwinding, and a new one
    /// from the restarted timer - and when the old one's `finally` finally
    /// runs, it resets `_ticking` to 0 regardless of which tick's run that
    /// value belonged to. Not fixed here; flagging it so the next person
    /// touching this doesn't have to rediscover it.
    /// </summary>
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _interval = tickInterval ?? TimeSpan.FromMinutes(1);

    private CancellationTokenSource _stopping = new();
    private ITimer? _timer;
    private Task _activeTick = Task.CompletedTask;

    /// <summary>
    /// When the previous tick ran, or null before the first one. Read only by
    /// TickAsync, which is the only caller, and only to answer the two
    /// questions in RefreshCatchUp: was there a gap long enough to mean the
    /// machine was asleep, and did the clock step backwards.
    /// </summary>
    private DateTimeOffset? _lastTickUtc;

    /// <summary>
    /// True while a backlog is being worked through a small batch at a time.
    /// Set by the tick that finds a wake-sized gap, cleared by the first tick
    /// that does not fill its batch.
    /// </summary>
    private bool _catchingUp;

    /// <summary>
    /// How many ticks have found the clock stepped backwards and pulled
    /// impossible next_due_utc values back into range, and how many feed rows
    /// the most recent one changed. Diagnostic only: nothing decides anything
    /// from these, but without them a rewind is invisible after the fact.
    /// </summary>
    public int ClockRewindsHandled { get; private set; }

    public int LastClockRewindFeedsRescheduled { get; private set; }

    /// <summary>
    /// True while the scheduler is deliberately queuing a reduced batch per
    /// tick because it is working through a backlog left by a suspend.
    /// </summary>
    public bool IsCatchingUp => _catchingUp;

    // 0 = idle, 1 = a tick is currently running. Guards against ticks
    // overlapping: a slow GetDueAsync under lock contention must not let
    // ticks stack up unbounded, so a tick that arrives while one is still
    // running is skipped rather than queued behind it.
    private int _ticking;

    // 0 = live, 1 = disposed. An int rather than a bool so DisposeAsync can
    // use Interlocked.Exchange: a plain check-then-set bool lets two
    // overlapping DisposeAsync calls both pass the check before either sets
    // it, which happened to be harmless only because
    // CancellationTokenSource.Dispose tolerates repeat calls. This makes
    // idempotency a property of the method, not an accident of what it
    // happens to call afterward.
    private int _disposed;

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public bool IsRunning => _timer is not null;

    /// <summary>
    /// The error message from the most recent tick that threw, or null if
    /// the most recent tick succeeded (or none has run yet). This is the
    /// only externally visible sign that background refresh has stopped
    /// actually doing anything: the timer keeps firing and IsRunning stays
    /// true no matter what a tick throws, so without this a caller - and
    /// the user - would have no way to tell "queued zero feeds because none
    /// were due" apart from "queued zero feeds because the last five ticks
    /// all threw." A caller surfacing background refresh health should
    /// read this alongside IsRunning.
    /// </summary>
    public string? LastTickError { get; private set; }

    /// <summary>
    /// How many ticks have thrown in a row. Resets to 0 on the next
    /// successful tick.
    /// </summary>
    public int ConsecutiveTickFailures { get; private set; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
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
        if (IsDisposed) return;

        await StopTimerAndWaitForActiveTickAsync();

        var old = _stopping;
        _stopping = new CancellationTokenSource();
        old.Dispose();
    }

    /// <summary>
    /// Queues every feed whose next_due_utc has passed. Returns how many were
    /// actually queued, which is fewer than were due when some are already in
    /// flight from a manual refresh.
    ///
    /// This is the manual path: "Refresh all" calls it. It deliberately does
    /// no clock bookkeeping and is never trimmed to a catch-up batch. See
    /// <see cref="ScheduledTickAsync"/>, which is the timer's.
    /// </summary>
    public Task<int> TickAsync(CancellationToken ct = default) =>
        QueueDueAsync(timeProvider.GetUtcNow(), RefreshCatchUp.NormalBatchSize, ct);

    /// <summary>
    /// The timer's own tick, and the only one that looks at the clock.
    ///
    /// The two checks at the top are what make this safe to leave running for
    /// weeks on a laptop, and neither exists to be clever. Without the first,
    /// the tick that runs when the lid opens finds every feed overdue and
    /// queues the entire subscription list at once, on a network that has
    /// only just come back. Without the second, a clock corrected backwards
    /// stops background refresh for the length of the correction and reports
    /// nothing, because "nothing is due" is exactly what the query returns
    /// either way.
    ///
    /// Kept apart from <see cref="TickAsync"/> deliberately. A manual refresh
    /// must never be trimmed to a catch-up batch, nor mistaken for the
    /// passage of time: a user pressing the button twice twenty seconds apart
    /// would otherwise be the last two "ticks", and the gap a real suspend
    /// left would be measured from the button rather than from the timer.
    /// </summary>
    public async Task<int> ScheduledTickAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        if (_lastTickUtc is { } previous)
        {
            if (RefreshCatchUp.IsClockRewind(previous, now))
            {
                ClockRewindsHandled++;
                LastClockRewindFeedsRescheduled = await feeds.ClampFutureDueAsync(
                    now, RefreshCatchUp.ImpossibleDueThreshold(now), ct);
            }
            else if (RefreshCatchUp.IsWakeGap(now - previous, _interval))
            {
                _catchingUp = true;
            }
        }

        _lastTickUtc = now;

        var batchSize = RefreshCatchUp.BatchSize(_catchingUp);
        var (queued, dueCount) = await QueueDueCountingAsync(now, batchSize, ct);

        // The backlog is measured by how full the batch came back, not by how
        // many were queued: a feed already in flight from a manual refresh is
        // still one this tick had to look at. Using the due count rather than
        // the queued count keeps a list of feeds that are all mid-refresh
        // from dropping out of catch-up mode a tick early.
        if (_catchingUp && !RefreshCatchUp.StillCatchingUp(dueCount, batchSize))
            _catchingUp = false;

        return queued;
    }

    private async Task<int> QueueDueAsync(DateTimeOffset now, int batchSize, CancellationToken ct) =>
        (await QueueDueCountingAsync(now, batchSize, ct)).Queued;

    private async Task<(int Queued, int DueCount)> QueueDueCountingAsync(
        DateTimeOffset now, int batchSize, CancellationToken ct)
    {
        var due = await feeds.GetDueAsync(now, batchSize, ct);

        var queued = 0;
        foreach (var feed in due)
            if (refresh.TryQueue(feed.Id))
                queued++;

        return (queued, due.Count);
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
            await ScheduledTickAsync(_stopping.Token);

            // A tick landing here succeeded: clear any failure streak a
            // caller might be reading from LastTickError/ConsecutiveTickFailures.
            LastTickError = null;
            ConsecutiveTickFailures = 0;
        }
        catch (OperationCanceledException)
        {
            // Shutting down. Not a tick failure - deliberately does not
            // touch LastTickError/ConsecutiveTickFailures.
        }
        catch (Exception ex)
        {
            // A tick that throws must not kill the timer, or refreshing stops
            // silently for the rest of the session. The next tick a minute
            // later gets a clean attempt. This is also the only place that
            // populates LastTickError/ConsecutiveTickFailures - without it,
            // a caller has no observable way to tell "refresh is failing"
            // from "refresh has nothing to do".
            LastTickError = ex.Message;
            ConsecutiveTickFailures++;
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
        // Idempotent by construction: Interlocked.Exchange means only the
        // caller that actually flips 0 -> 1 proceeds, even if two calls
        // race. A second DisposeAsync (defensive cleanup in a finally, a
        // double `await using`) must not throw, it should just be a no-op.
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        await StopTimerAndWaitForActiveTickAsync();
        _stopping.Dispose();
    }
}
