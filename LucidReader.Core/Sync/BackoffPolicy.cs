using LucidReader.Core.Model;
using Mostlylucid.Ephemeral.Atoms.Retry;

namespace LucidReader.Core.Sync;

/// <summary>
/// Decides when a feed is next due.
///
/// Uses BackoffStrategies from the Ephemeral retry atom for the curve, but not
/// RetryAtom itself: that holds its queue in memory, and our retry state has to
/// survive the app closing. The schedule lives in feeds.next_due_utc instead.
/// </summary>
public sealed class BackoffPolicy(Random? random = null)
{
    /// <summary>
    /// After this many consecutive failures a feed is paused and the user is
    /// asked, rather than hammering a host that is plainly not coming back.
    /// </summary>
    public const int AutoPauseThreshold = 20;

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(6);

    private static readonly TimeSpan BaseDelay = TimeSpan.FromMinutes(2);

    // BackoffStrategies.ExponentialWithJitter computes baseDelay * factor^(attempt-1)
    // as a double before converting back to a TimeSpan. With a 2 minute base and a
    // factor of 2, that conversion overflows TimeSpan.MaxValue somewhere around
    // attempt 33 (it throws OverflowException rather than saturating), so the
    // attempt number fed into the curve is clamped here, before multiplication.
    // The clamp is a no-op for the actual schedule: by attempt 9 the raw delay
    // (2 min * 2^8 = 8.5 hours) already exceeds MaxBackoff below, so every attempt
    // from 9 upward is capped to the same 6 hours regardless of where this ceiling
    // sits, as long as it is above 9 and below the overflow point.
    private const int MaxAttemptForCurve = 30;

    private readonly Func<int, TimeSpan> _backoff =
        BackoffStrategies.ExponentialWithJitter(
            BaseDelay,
            factor: 2.0,
            jitterRatio: 0.2,
            random: random ?? Random.Shared);

    // Deliberately an instance method, not static: it belongs next to
    // NextDueAfterFailure on the same policy object the caller already holds,
    // even though it does not touch the injected Random.
#pragma warning disable CA1822 // Mark members as static
    public DateTimeOffset NextDueAfterSuccess(
        DateTimeOffset nowUtc,
        EffectiveFeedSettings settings) =>
        nowUtc.Add(settings.RefreshInterval);
#pragma warning restore CA1822

    public DateTimeOffset NextDueAfterFailure(
        DateTimeOffset nowUtc,
        int consecutiveFailures,
        EffectiveFeedSettings settings)
    {
        var attempt = Math.Clamp(consecutiveFailures, 1, MaxAttemptForCurve);
        var delay = _backoff(attempt);

        if (delay > MaxBackoff) delay = MaxBackoff;

        // Jitter is symmetric, so a small delay can come back at or below zero.
        // A next-due in the past would make the scheduler spin.
        if (delay < TimeSpan.FromSeconds(30)) delay = TimeSpan.FromSeconds(30);

        return nowUtc.Add(delay);
    }

    public static bool ShouldAutoPause(int consecutiveFailures) =>
        consecutiveFailures >= AutoPauseThreshold;
}
