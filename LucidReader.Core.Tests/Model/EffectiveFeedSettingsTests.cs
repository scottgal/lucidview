using LucidReader.Core.Model;
using Xunit;

namespace LucidReader.Core.Tests.Model;

public class EffectiveFeedSettingsTests
{
    private static readonly ReaderSettings Globals = ReaderSettings.Defaults with
    {
        DefaultRefreshIntervalMinutes = 30,
        AutoDownloadArticles = true,
        FetchFullText = true,
        KeepReadArticlesDays = 30
    };

    private static Feed Feed() => new() { FeedUrl = "https://example.com/feed.xml" };

    [Fact]
    public void A_feed_with_no_overrides_inherits_every_global()
    {
        var effective = EffectiveFeedSettings.Resolve(Feed(), Globals);

        Assert.Equal(TimeSpan.FromMinutes(30), effective.RefreshInterval);
        Assert.True(effective.AutoDownload);
        Assert.True(effective.FetchFullText);
        Assert.Equal(30, effective.RetentionDays);
    }

    [Fact]
    public void An_override_wins_over_the_global()
    {
        var feed = Feed() with { RefreshIntervalMinutes = 5 };

        var effective = EffectiveFeedSettings.Resolve(feed, Globals);

        Assert.Equal(TimeSpan.FromMinutes(5), effective.RefreshInterval);
    }

    [Fact]
    public void A_false_override_is_respected_and_not_mistaken_for_unset()
    {
        var feed = Feed() with { AutoDownload = false };

        var effective = EffectiveFeedSettings.Resolve(feed, Globals);

        Assert.False(effective.AutoDownload);
    }

    [Fact]
    public void Changing_a_global_moves_every_non_overridden_feed()
    {
        var feed = Feed();

        var before = EffectiveFeedSettings.Resolve(feed, Globals);
        var after = EffectiveFeedSettings.Resolve(
            feed, Globals with { DefaultRefreshIntervalMinutes = 120 });

        Assert.Equal(TimeSpan.FromMinutes(30), before.RefreshInterval);
        Assert.Equal(TimeSpan.FromMinutes(120), after.RefreshInterval);
    }

    [Fact]
    public void Changing_a_global_leaves_an_overridden_feed_alone()
    {
        var feed = Feed() with { RefreshIntervalMinutes = 5 };

        var after = EffectiveFeedSettings.Resolve(
            feed, Globals with { DefaultRefreshIntervalMinutes = 120 });

        Assert.Equal(TimeSpan.FromMinutes(5), after.RefreshInterval);
    }

    [Fact]
    public void Keeping_unread_forever_resolves_to_no_retention_limit()
    {
        var globals = Globals with { KeepUnreadForever = true };

        var effective = EffectiveFeedSettings.Resolve(Feed(), globals);

        Assert.Equal(30, effective.RetentionDays);
        Assert.True(globals.KeepUnreadForever);
    }

    [Fact]
    public void A_refresh_interval_below_the_floor_is_clamped()
    {
        var feed = Feed() with { RefreshIntervalMinutes = 0 };

        var effective = EffectiveFeedSettings.Resolve(feed, Globals);

        Assert.Equal(ReaderSettings.MinimumRefreshInterval, effective.RefreshInterval);
    }
}
