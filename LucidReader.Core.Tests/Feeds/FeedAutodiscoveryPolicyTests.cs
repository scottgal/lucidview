using System.Net;
using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// Everything DiscoverAsync returns is offered to the user pre-ticked, stored
/// on confirmation, and fetched by the scheduler forever afterwards. The
/// anchor and well-known stages have always run FeedUrlPolicy over their
/// candidates; these cover the two paths that did not.
/// </summary>
public class FeedAutodiscoveryPolicyTests
{
    private const string PageWithPrivateLinks =
        """
        <html><head>
        <link rel="alternate" type="application/rss+xml" href="http://169.254.169.254/latest/meta-data/">
        <link rel="alternate" type="application/rss+xml" href="http://user:pass@10.0.0.5/feed">
        <link rel="alternate" type="application/rss+xml" href="http://127.0.0.1:9200/_cluster/settings">
        <link rel="alternate" type="application/rss+xml" href="http://169.254.169.254./feed">
        </head><body>page</body></html>
        """;

    [Fact]
    public async Task A_link_element_naming_a_private_address_is_not_offered()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, PageWithPrivateLinks, mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/blog");

        Assert.DoesNotContain(found, f => f.FeedUrl.Contains("169.254.169.254", StringComparison.Ordinal));
        Assert.DoesNotContain(found, f => f.FeedUrl.Contains("10.0.0.5", StringComparison.Ordinal));
        Assert.DoesNotContain(found, f => f.FeedUrl.Contains("127.0.0.1", StringComparison.Ordinal));
    }

    /// <summary>
    /// The credentialed form matters on its own: stored in feeds.feed_url it
    /// would be replayed, password and all, on every scheduler tick.
    /// </summary>
    [Fact]
    public async Task A_link_element_carrying_credentials_is_not_offered()
    {
        const string page =
            """
            <html><head>
            <link rel="alternate" type="application/rss+xml" href="https://user:pass@example.com/feed">
            </head><body>page</body></html>
            """;
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, page, mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/blog");

        Assert.DoesNotContain(found, f => f.FeedUrl.Contains("pass@", StringComparison.Ordinal));
    }

    /// <summary>
    /// The "the page you pasted IS a feed" branch returns the post-redirect
    /// address, which is chosen by the server rather than by the user.
    /// </summary>
    [Fact]
    public async Task A_feed_that_redirected_onto_a_private_address_is_not_offered()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK,
            Fixtures.Feed("rss2-simple.xml"),
            mediaType: "application/rss+xml",
            finalRequestUri: new Uri("http://169.254.169.254/feed.xml"));
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        Assert.Empty(await discovery.DiscoverAsync("https://example.com/feed.xml"));
    }

    /// <summary>
    /// The same hole one stage further on: a probed candidate that redirects
    /// is recorded at the address it ended up at, not the one that passed the
    /// policy.
    /// </summary>
    [Fact]
    public async Task A_probed_candidate_that_redirected_onto_a_private_address_is_not_offered()
    {
        var handler = new StubHttpHandler(request =>
            request.RequestUri!.AbsolutePath == "/blog"
                ? StubHttpHandler.Response(
                    "<html><head></head><body><a href=\"/feed\">rss</a></body></html>", "text/html")
                : Respond(Fixtures.Feed("rss2-simple.xml")));

        var discovery = new FeedAutodiscovery(handler.CreateClient());

        Assert.Empty(await discovery.DiscoverAsync("https://example.com/blog"));

        static HttpResponseMessage Respond(string body)
        {
            var response = StubHttpHandler.Response(body, "application/rss+xml");
            response.RequestMessage = new HttpRequestMessage(
                HttpMethod.Get, new Uri("http://192.168.1.1/feed"));
            return response;
        }
    }

    [Fact]
    public async Task An_ordinary_public_link_element_is_still_offered()
    {
        const string page =
            """
            <html><head>
            <link rel="alternate" type="application/rss+xml" title="Blog" href="https://example.com/feed.xml">
            </head><body>page</body></html>
            """;
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, page, mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var one = Assert.Single(await discovery.DiscoverAsync("https://example.com/blog"));
        Assert.Equal("https://example.com/feed.xml", one.FeedUrl);
    }
}
