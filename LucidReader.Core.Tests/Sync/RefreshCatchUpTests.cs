using LucidReader.Core.Sync;
using Xunit;

namespace LucidReader.Core.Tests.Sync;

/// <summary>
/// The rules a scheduler tick follows when the wall clock has misbehaved:
/// suspend-and-resume, and the clock stepping backwards. Both are things a
/// reader left open for weeks on a laptop meets routinely and neither is
/// otherwise observable.
/// </summary>
public class RefreshCatchUpTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    [Fact]
    public void An_ordinary_late_tick_is_not_a_wake_gap()
    {
        Assert.False(RefreshCatchUp.IsWakeGap(TimeSpan.FromSeconds(70), Interval));
        Assert.False(RefreshCatchUp.IsWakeGap(TimeSpan.FromMinutes(2), Interval));
    }

    [Fact]
    public void A_gap_of_hours_is_a_wake_gap()
    {
        Assert.True(RefreshCatchUp.IsWakeGap(TimeSpan.FromHours(9), Interval));
    }

    [Fact]
    public void A_zero_interval_can_never_report_a_wake_gap()
    {
        // Guards against a divide-by-nothing style trap if a caller ever
        // hands in an unconfigured interval: every gap would otherwise look
        // like a suspend and the scheduler would never leave catch-up mode.
        Assert.False(RefreshCatchUp.IsWakeGap(TimeSpan.FromDays(1), TimeSpan.Zero));
    }

    [Fact]
    public void Catching_up_queues_far_fewer_feeds_than_an_ordinary_tick()
    {
        Assert.Equal(RefreshCatchUp.NormalBatchSize, RefreshCatchUp.BatchSize(inCatchUp: false));
        Assert.Equal(RefreshCatchUp.CatchUpBatchSize, RefreshCatchUp.BatchSize(inCatchUp: true));
        Assert.True(RefreshCatchUp.CatchUpBatchSize < RefreshCatchUp.NormalBatchSize);
    }

    [Fact]
    public void A_full_batch_means_there_is_more_behind_it()
    {
        Assert.True(RefreshCatchUp.StillCatchingUp(10, 10));
        Assert.False(RefreshCatchUp.StillCatchingUp(9, 10));
        Assert.False(RefreshCatchUp.StillCatchingUp(0, 10));
    }

    [Fact]
    public void A_small_backwards_step_is_not_a_rewind()
    {
        var last = DateTimeOffset.UtcNow;

        Assert.False(RefreshCatchUp.IsClockRewind(last, last));
        Assert.False(RefreshCatchUp.IsClockRewind(last, last.AddSeconds(-30)));
    }

    [Fact]
    public void A_clock_corrected_backwards_is_a_rewind()
    {
        var last = DateTimeOffset.UtcNow;

        Assert.True(RefreshCatchUp.IsClockRewind(last, last.AddHours(-3)));
        Assert.True(RefreshCatchUp.IsClockRewind(last, last.AddDays(-30)));
    }

    [Fact]
    public void The_clock_moving_forward_is_never_a_rewind()
    {
        var last = DateTimeOffset.UtcNow;
        Assert.False(RefreshCatchUp.IsClockRewind(last, last.AddDays(30)));
    }

    [Fact]
    public void Nothing_this_app_schedules_can_land_past_the_impossible_threshold()
    {
        var now = DateTimeOffset.UtcNow;
        var threshold = RefreshCatchUp.ImpossibleDueThreshold(now);

        // The longest interval the settings dialog offers, and the longest
        // failure backoff, both have to sit comfortably inside it or the
        // clamp would pull back schedules the app wrote on purpose.
        Assert.True(now.AddMinutes(1440) < threshold);
        Assert.True(now.Add(BackoffPolicy.MaxBackoff) < threshold);
    }

    /// <summary>
    /// Daylight saving needs no handling and gets none, and this is what
    /// says so. Every instant in the scheduling path is UTC, and UTC has no
    /// daylight saving: the same absolute instant either side of a local
    /// transition is the same instant.
    /// </summary>
    [Fact]
    public void A_local_daylight_saving_transition_does_not_move_a_due_time()
    {
        // 26 October 2025, 02:00 local in London: the clocks went back an
        // hour. Both local spellings of 01:30 are the same two UTC instants
        // an hour apart, and neither is a rewind nor a wake gap.
        var beforeUtc = new DateTimeOffset(2025, 10, 26, 0, 30, 0, TimeSpan.Zero);
        var afterUtc = new DateTimeOffset(2025, 10, 26, 1, 30, 0, TimeSpan.Zero);

        Assert.False(RefreshCatchUp.IsClockRewind(beforeUtc, afterUtc));
        Assert.Equal(TimeSpan.FromHours(1), afterUtc - beforeUtc);
    }
}
