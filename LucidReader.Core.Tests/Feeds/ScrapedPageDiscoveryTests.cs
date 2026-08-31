using System.Net;
using System.Text.RegularExpressions;
using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// Discovery's fourth stage: offering the page itself when the site publishes
/// no feed at all.
///
/// A note on the fixtures. Both mostlylucid.net pages really do declare feeds,
/// which is why the first test here is the one proving that a declared feed
/// wins and the page is never offered as a scrape. To reach the fourth stage
/// at all the tests below strip those declarations, which is exactly the site
/// this feature is for: the same markup, published by somebody who never set
/// up a feed. Stripping is done here rather than by saving a doctored fixture,
/// so the fixture on disk stays a faithful copy of the real page.
/// </summary>
public class ScrapedPageDiscoveryTests
{
    private static string Html(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", name));

    /// <summary>
    /// Removes every feed declaration from a page: the link elements the first
    /// stage reads, and the anchors to /rss and /atom the second stage reads.
    /// What is left is a site with no feed, which is the only kind of site the
    /// fourth stage ever sees.
    /// </summary>
    private static string WithNoFeed(string name)
    {
        var html = Html(name);
        html = Regex.Replace(html, """<link[^>]*rel=["']?alternate[^>]*>""", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, """<a[^>]*href=["']/(rss|atom|feed)(\.xml)?["'][^>]*>""", "<a>", RegexOptions.IgnoreCase);
        return html;
    }

    private static StubHttpHandler Serving(string body) =>
        StubHttpHandler.Returning(HttpStatusCode.OK, body, mediaType: "text/html");

    /// <summary>
    /// A real feed always wins, and this is the page the rest of the tests
    /// have to disable to get past. mostlylucid.net publishes both an RSS and
    /// an Atom feed and declares them in its head, so the first stage resolves
    /// it and the detector never runs.
    /// </summary>
    [Fact]
    public async Task A_page_that_declares_feeds_is_never_offered_as_a_scrape()
    {
        var handler = Serving(Html("mostlylucid-blog-index.html"));
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://www.mostlylucid.net/blog");

        Assert.NotEmpty(found);
        Assert.All(found, f => Assert.False(f.IsScrapedPage));
        Assert.All(found, f => Assert.Null(f.Scrape));
    }

    /// <summary>
    /// The common case, unchanged. A page declaring one feed in its head is
    /// resolved by the first stage after a single request; the detector never
    /// runs, so a site with a feed pays nothing for this feature existing.
    /// </summary>
    [Fact]
    public async Task A_site_with_a_declared_feed_still_costs_one_request()
    {
        var handler = Serving(Html("single-feed.html"));
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/blog");

        Assert.False(Assert.Single(found).IsScrapedPage);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_page_with_no_feed_that_lists_articles_is_offered_as_a_scrape()
    {
        var handler = Serving(WithNoFeed("mostlylucid-blog-index.html"));
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://www.mostlylucid.net/blog");

        var offer = Assert.Single(found);
        Assert.True(offer.IsScrapedPage);
        Assert.Equal("https://www.mostlylucid.net/blog", offer.FeedUrl);
        Assert.Equal(20, offer.Scrape!.ArticleCount);
        Assert.Equal(FeedAutodiscovery.ScrapeSampleTitleCount, offer.Scrape.SampleTitles.Count);
        Assert.All(offer.Scrape.SampleTitles, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.True(offer.Scrape.Confidence >= ArticleListDetector.ConfidenceThreshold);
    }

    /// <summary>
    /// A scraped subscription has no feed document to take a name from, so the
    /// page's own title element is the only name it can have.
    /// </summary>
    [Fact]
    public async Task A_scraped_offer_is_named_after_the_pages_title()
    {
        var handler = Serving(WithNoFeed("mostlylucid-blog-index.html"));
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://www.mostlylucid.net/blog");

        Assert.Equal("Blog Posts", found[0].Title);
    }

    /// <summary>
    /// A page mylo's own detector cannot read is still offered, by the
    /// fallback, and the offer says so.
    ///
    /// lwn.net is the page in question and it does publish a feed, so the same
    /// stripping the tests above use is what gets discovery as far as the
    /// fourth stage. What is being asserted is the last step of that stage:
    /// the detector declines, the fallback reads it, and the offer that comes
    /// back is marked as having come from the fallback so the dialog can be
    /// honest about which of the two found it.
    /// </summary>
    [Fact]
    public async Task A_page_only_the_fallback_can_read_is_offered_and_says_so()
    {
        var lwn = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Html", "corpus", "lwn.html"));
        var stripped = Regex.Replace(
            lwn, """<link[^>]*rel=["']?alternate[^>]*>""", "", RegexOptions.IgnoreCase);

        var handler = Serving(stripped);
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://lwn.net/");

        var offer = Assert.Single(found);
        Assert.True(offer.IsScrapedPage);
        Assert.True(offer.Scrape!.FromFallback,
            "The detector cannot read this page, so the offer must be marked as the " +
            "fallback's rather than passed off as mylo's own reading.");
        Assert.True(offer.Scrape.ArticleCount >= ArticleListDetector.MinimumArticles);
        Assert.NotEmpty(offer.Scrape.SampleTitles);
    }

    /// <summary>
    /// And a page the detector reads is not marked as the fallback's, so the
    /// mark means something.
    /// </summary>
    [Fact]
    public async Task An_offer_the_detector_found_is_not_marked_as_the_fallbacks()
    {
        var handler = Serving(WithNoFeed("mostlylucid-blog-index.html"));
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://www.mostlylucid.net/blog");

        Assert.False(Assert.Single(found).Scrape!.FromFallback);
    }

    [Fact]
    public async Task An_article_page_with_no_feed_is_not_offered_as_a_scrape()
    {
        var handler = Serving(WithNoFeed("mostlylucid-post.html"));
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync(
            "https://www.mostlylucid.net/blog/signal-shingle-architecture");

        Assert.Empty(found);
    }

    [Fact]
    public async Task A_page_with_nothing_on_it_is_not_offered_as_a_scrape()
    {
        var handler = Serving(Html("no-feed.html"));
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        Assert.Empty(await discovery.DiscoverAsync("https://example.com/"));
    }

    /// <summary>
    /// The cost check, and the promise the fourth stage has to keep: the
    /// detector reads the page body discovery already downloaded, so offering
    /// a scrape adds no request of its own. The page is fetched exactly once.
    /// </summary>
    [Fact]
    public async Task Offering_a_scrape_does_not_refetch_the_page()
    {
        var handler = Serving(WithNoFeed("mostlylucid-blog-index.html"));
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://www.mostlylucid.net/blog");

        Assert.True(found[0].IsScrapedPage);
        Assert.Equal(1, handler.Requests.Count(
            r => r.RequestUri!.ToString() == "https://www.mostlylucid.net/blog"));
    }
}
