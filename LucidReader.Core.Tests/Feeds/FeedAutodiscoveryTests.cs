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

    // --- Size bound ---
    //
    // ArticleFetcher and FeedFetcher already solved exactly this problem: an
    // unbounded ReadAsStringAsync buffers a chunked response, which never
    // sets Content-Length, with no cap at all. FeedAutodiscovery is the most
    // exposed of the three - it fires straight off a string the user pasted,
    // with no prior evidence the target is even feed-shaped.

    [Fact]
    public async Task A_chunked_response_over_the_size_cap_is_rejected_not_buffered()
    {
        // No Content-Length header at all - the shape a chunked response
        // takes - so the fast Content-Length pre-check cannot fire; only the
        // streaming bound can reject this.
        var handler = StubHttpHandler.ReturningUnboundedLength(9 * 1024 * 1024);
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/blog")
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(found);
    }

    // --- Redirects ---

    [Fact]
    public async Task Relative_links_resolve_against_the_post_redirect_url_not_the_original()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK,
            Html("single-feed.html"),
            mediaType: "text/html",
            finalRequestUri: new Uri("https://www.example.com/blog"));
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/blog");

        var one = Assert.Single(found);
        Assert.Equal("https://www.example.com/feed.xml", one.FeedUrl);
    }

    [Fact]
    public async Task An_already_a_feed_url_is_reported_as_the_post_redirect_url()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK,
            Fixtures.Feed("rss2-simple.xml"),
            mediaType: "application/rss+xml",
            finalRequestUri: new Uri("https://www.example.com/feed.xml"));
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/feed.xml");

        var one = Assert.Single(found);
        Assert.Equal("https://www.example.com/feed.xml", one.FeedUrl);
    }

    // --- HTML entities in hrefs ---

    [Fact]
    public async Task An_html_entity_encoded_href_is_decoded_before_being_resolved()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("entity-encoded-href.html"), mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/blog");

        var one = Assert.Single(found);
        Assert.Equal("https://example.com/feed?a=1&b=2", one.FeedUrl);
    }

    // --- Site icon (Task 8b) ---
    //
    // FeedAutodiscovery already downloads the site page to find feed links;
    // the favicon is in that same HTML, so no second fetch is needed to
    // populate DiscoveredFeed.IconUrl.

    [Fact]
    public async Task A_discovered_feed_carries_the_site_icon_found_on_the_page()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("metadata-rich.html"), mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/");

        var one = Assert.Single(found);
        Assert.Equal("https://example.com/icons/favicon-32.png", one.IconUrl);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_page_declaring_no_icon_falls_back_to_a_favicon_ico_guess_at_the_site_root()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("single-feed.html"), mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/blog");

        var one = Assert.Single(found);
        Assert.Equal("https://example.com/favicon.ico", one.IconUrl);
    }

    [Fact]
    public async Task An_already_a_feed_url_gets_a_favicon_ico_guess_since_there_is_no_page_to_read()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"), mediaType: "application/rss+xml");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/feed.xml");

        var one = Assert.Single(found);
        Assert.Equal("https://example.com/favicon.ico", one.IconUrl);
    }

    // --- rel token matching ---

    [Fact]
    public async Task Rel_matching_is_token_based_not_a_substring_check()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("rel-token-boundary.html"), mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/blog");

        var one = Assert.Single(found);
        Assert.Equal("https://example.com/feed.xml", one.FeedUrl);
    }
}
