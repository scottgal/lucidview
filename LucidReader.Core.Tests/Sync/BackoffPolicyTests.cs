using LucidReader.Core.Model;
using LucidReader.Core.Sync;
using Xunit;

namespace LucidReader.Core.Tests.Sync;

public class BackoffPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");

    private static EffectiveFeedSettings Settings(int minutes = 30) =>
        new(TimeSpan.FromMinutes(minutes), true, true, 30);

    // A fixed seed makes the jitter deterministic, so these tests never flake.
    private static BackoffPolicy Policy() => new(new Random(12345));

    [Fact]
    public void Success_schedules_the_next_fetch_one_interval_away()
    {
        var next = Policy().NextDueAfterSuccess(Now, Settings(30));

        Assert.Equal(Now.AddMinutes(30), next);
    }

    [Fact]
    public void The_first_failure_waits_longer_than_zero_but_less_than_the_interval()
    {
        var next = Policy().NextDueAfterFailure(Now, 1, Settings(30));

        Assert.True(next > Now);
        Assert.True(next <= Now.AddMinutes(30));
    }

    [Fact]
    public void Each_further_failure_waits_longer_than_the_one_before()
    {
        var policy = Policy();

        var first = policy.NextDueAfterFailure(Now, 1, Settings());
        var second = policy.NextDueAfterFailure(Now, 2, Settings());
        var third = policy.NextDueAfterFailure(Now, 3, Settings());
        var fourth = policy.NextDueAfterFailure(Now, 4, Settings());

        Assert.True(second > first);
        Assert.True(third > second);
        Assert.True(fourth > third);
    }

    [Fact]
    public void Backoff_is_capped_so_a_dead_feed_is_still_retried_occasionally()
    {
        var next = Policy().NextDueAfterFailure(Now, 50, Settings());

        Assert.True(next <= Now.Add(BackoffPolicy.MaxBackoff));
    }

    [Fact]
    public void Backoff_never_schedules_a_fetch_in_the_past()
    {
        var policy = Policy();

        for (var failures = 1; failures <= 30; failures++)
            Assert.True(policy.NextDueAfterFailure(Now, failures, Settings()) > Now);
    }

    [Fact]
    public void Jitter_spreads_two_feeds_failing_at_the_same_moment()
    {
        var policy = Policy();

        var a = policy.NextDueAfterFailure(Now, 5, Settings());
        var b = policy.NextDueAfterFailure(Now, 5, Settings());

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void A_feed_is_auto_paused_only_after_the_threshold()
    {
        Assert.False(BackoffPolicy.ShouldAutoPause(19));
        Assert.True(BackoffPolicy.ShouldAutoPause(20));
        Assert.True(BackoffPolicy.ShouldAutoPause(21));
    }
}
