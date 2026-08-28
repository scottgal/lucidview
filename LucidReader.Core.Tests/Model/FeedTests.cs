using LucidReader.Core.Model;
using Xunit;

namespace LucidReader.Core.Tests.Model;

public class FeedTests
{
    [Fact]
    public void DisplayTitle_prefers_the_user_override()
    {
        var feed = new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            Title = "Feed's own title",
            TitleOverride = "My name for it"
        };

        Assert.Equal("My name for it", feed.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_falls_back_to_the_feed_title_when_no_override()
    {
        var feed = new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            Title = "Feed's own title"
        };

        Assert.Equal("Feed's own title", feed.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_falls_back_to_the_url_when_the_feed_has_no_title()
    {
        var feed = new Feed { FeedUrl = "https://example.com/feed.xml" };

        Assert.Equal("https://example.com/feed.xml", feed.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_treats_a_whitespace_override_as_absent()
    {
        var feed = new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            Title = "Feed's own title",
            TitleOverride = "   "
        };

        Assert.Equal("Feed's own title", feed.DisplayTitle);
    }
}
