using LucidReader.Core.Model;
using LucidReader.Core.Notifications;
using Xunit;

namespace LucidReader.Core.Tests.Notifications;

/// <summary>
/// Coalescing a refresh sweep into one notification, deciding whether it is
/// worth posting, and what it says. All three are plain functions over plain
/// values, which is the only reason they can be asserted at all: the parts
/// that touch a platform are behind ISystemNotifier and are not testable
/// from here.
/// </summary>
public class NewArticleAccumulatorTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(6);

    private static NewArticleAccumulator Create() => new(Quiet);

    [Fact]
    public void Nothing_arrived_means_nothing_to_report()
    {
        var accumulator = Create();
        Assert.False(accumulator.HasPending);
        Assert.False(accumulator.TakeIfSettled(Start.AddHours(1)).HasArticles);
    }

    [Fact]
    public void A_feed_that_brought_nothing_new_is_not_a_sweep()
    {
        var accumulator = Create();
        accumulator.Add(feedId: 1, newArticleCount: 0, Start);

        Assert.False(accumulator.HasPending);
    }

    [Fact]
    public void One_scheduler_tick_across_four_feeds_produces_one_sweep()
    {
        var accumulator = Create();

        accumulator.Add(1, 5, Start);
        accumulator.Add(2, 3, Start.AddSeconds(1));
        accumulator.Add(3, 2, Start.AddSeconds(2));
        accumulator.Add(4, 2, Start.AddSeconds(3));

        var sweep = accumulator.TakeIfSettled(Start.AddSeconds(3).Add(Quiet));

        Assert.Equal(12, sweep.ArticleCount);
        Assert.Equal(4, sweep.FeedCount);
    }

    [Fact]
    public void A_sweep_still_arriving_is_not_reported_yet()
    {
        var accumulator = Create();
        accumulator.Add(1, 5, Start);

        Assert.False(accumulator.TakeIfSettled(Start.AddSeconds(3)).HasArticles);

        // And a later arrival pushes the deadline out again, so a feed that
        // is slow to finish cannot be cut out of the sweep it belongs to.
        accumulator.Add(2, 1, Start.AddSeconds(5));
        Assert.False(accumulator.TakeIfSettled(Start.AddSeconds(8)).HasArticles);
        Assert.True(accumulator.TakeIfSettled(Start.AddSeconds(12)).HasArticles);
    }

    [Fact]
    public void A_taken_sweep_is_not_reported_a_second_time()
    {
        var accumulator = Create();
        accumulator.Add(1, 5, Start);

        Assert.True(accumulator.TakeIfSettled(Start.Add(Quiet)).HasArticles);
        Assert.False(accumulator.TakeIfSettled(Start.Add(Quiet)).HasArticles);
        Assert.False(accumulator.HasPending);
    }

    [Fact]
    public void One_feed_counted_twice_in_a_sweep_is_still_one_feed()
    {
        // A manual refresh landing on top of an automatic one for the same
        // feed. Its articles count; it is not two feeds.
        var accumulator = Create();
        accumulator.Add(1, 3, Start);
        accumulator.Add(1, 2, Start.AddSeconds(1));

        var sweep = accumulator.TakeIfSettled(Start.AddSeconds(10));

        Assert.Equal(5, sweep.ArticleCount);
        Assert.Equal(1, sweep.FeedCount);
    }
}

public class NotificationPolicyTests
{
    private static ReaderSettings Settings(bool enabled, bool onlyWhenUnfocused) =>
        ReaderSettings.Defaults with
        {
            EnableNotifications = enabled,
            NotifyOnlyWhenUnfocused = onlyWhenUnfocused
        };

    [Fact]
    public void Notifications_off_means_nothing_is_posted_however_much_arrived()
    {
        Assert.False(NotificationPolicy.ShouldNotify(
            Settings(enabled: false, onlyWhenUnfocused: false),
            windowIsFocused: false,
            new NewArticleSweep(50, 9)));
    }

    [Fact]
    public void An_empty_sweep_is_never_posted()
    {
        Assert.False(NotificationPolicy.ShouldNotify(
            Settings(true, false), windowIsFocused: false, NewArticleSweep.Empty));
    }

    [Fact]
    public void Only_when_unfocused_suppresses_a_notification_while_the_window_is_in_front()
    {
        var settings = Settings(enabled: true, onlyWhenUnfocused: true);

        Assert.False(NotificationPolicy.ShouldNotify(
            settings, windowIsFocused: true, new NewArticleSweep(4, 2)));
        Assert.True(NotificationPolicy.ShouldNotify(
            settings, windowIsFocused: false, new NewArticleSweep(4, 2)));
    }

    [Fact]
    public void Always_notifying_posts_even_with_the_window_in_front()
    {
        Assert.True(NotificationPolicy.ShouldNotify(
            Settings(enabled: true, onlyWhenUnfocused: false),
            windowIsFocused: true,
            new NewArticleSweep(4, 2)));
    }

    [Theory]
    [InlineData(1, 1, "1 new article")]
    [InlineData(3, 1, "3 new articles")]
    [InlineData(12, 4, "12 new articles from 4 feeds")]
    [InlineData(2, 2, "2 new articles from 2 feeds")]
    public void The_wording_says_what_arrived(int articles, int feeds, string expected) =>
        Assert.Equal(expected, NotificationPolicy.Describe(articles, feeds));

    [Fact]
    public void Nothing_arrived_has_nothing_to_say()
    {
        Assert.Equal(string.Empty, NotificationPolicy.Describe(0, 0));
    }

    [Theory]
    [InlineData(0, "mylo: no unread articles")]
    [InlineData(1, "mylo: 1 unread article")]
    [InlineData(42, "mylo: 42 unread articles")]
    public void The_status_item_tooltip_leads_with_the_unread_count(int unread, string expected) =>
        Assert.Equal(expected, NotificationPolicy.DescribeUnread(unread));

    [Theory]
    [InlineData(0, "")]
    [InlineData(7, "7")]
    [InlineData(999, "999")]
    [InlineData(1000, "999+")]
    public void The_badge_is_empty_at_zero_and_capped_at_the_top(int unread, string expected) =>
        Assert.Equal(expected, NotificationPolicy.UnreadBadge(unread));
}
