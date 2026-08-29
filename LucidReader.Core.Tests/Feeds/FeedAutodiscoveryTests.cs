using System.Net;
using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

public class FeedAutodiscoveryTests
{
    private static string Html(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", name));

    [Fact]
    public async Task A_url_that_is_already_a_feed_is_returned_as_is()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"), mediaType: "application/rss+xml");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/feed.xml");

        var one = Assert.Single(found);
        Assert.Equal("https://example.com/feed.xml", one.FeedUrl);
        Assert.Equal("Example Blog", one.Title);
    }

    [Fact]
    public async Task A_page_with_one_feed_link_yields_it_with_an_absolute_url()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("single-feed.html"), mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/blog");

        var one = Assert.Single(found);
        Assert.Equal("https://example.com/feed.xml", one.FeedUrl);
        Assert.Equal("Example Blog RSS", one.Title);
    }

    [Fact]
    public async Task A_page_with_two_feeds_yields_both_in_document_order()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("two-feeds.html"), mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/");

        Assert.Equal(2, found.Count);
        Assert.Equal("https://example.com/feed.xml", found[0].FeedUrl);
        Assert.Equal("https://example.com/comments.atom", found[1].FeedUrl);
    }

    [Fact]
    public async Task A_json_feed_link_is_ignored_because_the_parser_cannot_read_it()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("two-feeds.html"), mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/");

        Assert.DoesNotContain(found, f => f.FeedUrl.EndsWith("feed.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_page_with_no_feed_returns_empty_rather_than_throwing()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("no-feed.html"), mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        Assert.Empty(await discovery.DiscoverAsync("https://example.com/"));
    }

    [Fact]
    public async Task An_unreachable_url_returns_empty_rather_than_throwing()
    {
        var handler = StubHttpHandler.Throwing(new HttpRequestException("no route to host"));
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        Assert.Empty(await discovery.DiscoverAsync("https://nope.invalid/"));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    public async Task A_url_that_is_not_http_is_refused(string input)
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html></html>", mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        Assert.Empty(await discovery.DiscoverAsync(input));
        Assert.Empty(handler.Requests);
    }
}
