namespace LucidReader.Core.Sync;

/// <summary>
/// What a scheduler tick is allowed to do when the wall clock has not
/// behaved the way a once-a-minute timer assumes it does.
///
/// A reader is left open for weeks, and on a laptop that means it is
/// suspended and resumed dozens of times. Two things follow from that, and
/// neither is hypothetical.
///
/// The first is the wake storm. While the machine sleeps no tick fires, so
/// every feed's next_due_utc goes past. The tick that runs on wake sees the
/// whole subscription list as due at once and queues all of it in one go:
/// two hundred outbound fetches the instant the lid opens, on whatever
/// network the machine has just reconnected to. The concurrency bound stops
/// that from being two hundred simultaneous sockets, but it does not stop it
/// being two hundred queued fetches ahead of anything the user then asks
/// for. So a tick that follows a gap much longer than the tick interval
/// enters catch-up mode and queues a small batch, then keeps queuing small
/// batches on subsequent ticks until the backlog is gone. The feeds are all
/// refreshed either way; they are refreshed over the next few minutes
/// instead of the next few seconds.
///
/// The second is the clock going backwards. next_due_utc is an absolute
/// instant, so a clock corrected backwards (a bad NTP step, a manual change,
/// a virtual machine restored from a snapshot) leaves every feed scheduled
/// that far in the future, and background refresh silently stops for exactly
/// as long as the rewind was. Nothing recovers from that on its own, because
/// the condition looks identical to "nothing is due yet". Detecting the
/// rewind and pulling impossible due times back is the only way out.
///
/// Daylight saving needs nothing here and deliberately gets nothing: every
/// instant in this app is UTC (TimeProvider.GetUtcNow, DateTimeOffset stored
/// through ToDbString), and UTC has no daylight saving. A local-time spring
/// forward or fall back does not move a single next_due_utc.
///
/// All of it is static and takes its inputs as arguments so the rules can be
/// asserted without a scheduler, a timer or a database.
/// </summary>
public static class RefreshCatchUp
{
    /// <summary>How many feeds an ordinary tick may queue.</summary>
    public const int NormalBatchSize = 200;

    /// <summary>
    /// How many feeds a tick may queue while working through a backlog.
    /// Sized so a large subscription list drains over minutes rather than
    /// instantly, and so the first thing the machine does on waking is not
    /// saturate a connection that has only just come back.
    /// </summary>
    public const int CatchUpBatchSize = 10;

    /// <summary>
    /// How far ahead a next_due_utc can legitimately be. The longest refresh
    /// interval the settings dialog offers is 1440 minutes and the longest
    /// failure backoff is 6 hours, so anything beyond a day is not a schedule
    /// this app wrote against the current clock.
    /// </summary>
    public static readonly TimeSpan MaxSaneDueAhead = TimeSpan.FromHours(48);

    /// <summary>
    /// How many multiples of the tick interval may pass before a gap is read
    /// as suspend-and-resume rather than an ordinary late timer. Three is far
    /// enough above normal scheduler jitter that a loaded machine does not
    /// trip it, and far below the hours a real sleep lasts.
    /// </summary>
    public const double WakeGapMultiple = 3.0;

    /// <summary>
    /// Small allowance before a backwards step in the clock is treated as a
    /// real rewind rather than the ordinary imprecision of two reads.
    /// </summary>
    public static readonly TimeSpan ClockRewindTolerance = TimeSpan.FromMinutes(2);

    public static bool IsWakeGap(TimeSpan sinceLastTick, TimeSpan tickInterval)
    {
        if (tickInterval <= TimeSpan.Zero) return false;
        return sinceLastTick > tickInterval * WakeGapMultiple;
    }

    public static bool IsClockRewind(DateTimeOffset lastTickUtc, DateTimeOffset nowUtc) =>
        nowUtc < lastTickUtc - ClockRewindTolerance;

    public static int BatchSize(bool inCatchUp) => inCatchUp ? CatchUpBatchSize : NormalBatchSize;

    /// <summary>
    /// Whether the backlog is still there after a tick queued
    /// <paramref name="queued"/> feeds out of a batch of
    /// <paramref name="batchSize"/>. A tick that filled its batch almost
    /// certainly left more behind; one that did not has drained it.
    /// </summary>
    public static bool StillCatchingUp(int queued, int batchSize) =>
        batchSize > 0 && queued >= batchSize;

    /// <summary>
    /// The instant beyond which a stored next_due_utc cannot have been
    /// written by this app against the current clock.
    /// </summary>
    public static DateTimeOffset ImpossibleDueThreshold(DateTimeOffset nowUtc) =>
        nowUtc + MaxSaneDueAhead;
}
