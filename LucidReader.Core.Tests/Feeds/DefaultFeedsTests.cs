using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// Being compiled into the binary is not a reason for an address to skip the
/// gate every other address goes through. These are the checks that keep the
/// starter list honest as it is edited.
/// </summary>
public class DefaultFeedsTests
{
    [Fact]
    public void Every_default_feed_passes_the_url_policy()
    {
        foreach (var feed in DefaultFeeds.All)
            Assert.True(FeedUrlPolicy.IsAllowed(feed.FeedUrl), feed.FeedUrl);
    }

    [Fact]
    public void Allowed_returns_the_whole_list_while_every_entry_is_permitted()
    {
        Assert.Equal(DefaultFeeds.All.Count, DefaultFeeds.Allowed().Count);
    }

    /// <summary>
    /// Small on purpose. A starter list is a suggestion, and one long enough
    /// to be a chore to clear is not a kindness.
    /// </summary>
    [Fact]
    public void The_list_stays_short()
    {
        Assert.InRange(DefaultFeeds.All.Count, 3, 6);
    }

    [Fact]
    public void Every_entry_is_https_and_distinct()
    {
        Assert.All(DefaultFeeds.All, f => Assert.StartsWith("https://", f.FeedUrl));
        Assert.Equal(
            DefaultFeeds.All.Count,
            DefaultFeeds.All.Select(f => f.FeedUrl).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_entry_is_named_so_the_sidebar_is_readable_before_the_first_refresh()
    {
        Assert.All(DefaultFeeds.All, f => Assert.False(string.IsNullOrWhiteSpace(f.Title)));
    }

    [Fact]
    public void The_maintainers_own_site_is_first()
    {
        Assert.Contains("mostlylucid.net", DefaultFeeds.All[0].FeedUrl);
    }
}
