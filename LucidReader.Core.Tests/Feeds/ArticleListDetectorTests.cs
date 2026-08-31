using System.Text.RegularExpressions;
using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// The detector measured against pages that really exist, saved as fixtures.
///
/// Hand-written HTML is not evidence here. A fixture written to suit the
/// heuristic will always pass it, and the failure mode this feature has is
/// exactly that: markup that looks like an index in the abstract and does not
/// occur in the wild, or markup that is plainly an index and that a tidy little
/// example never prepares you for (a table with the date in the next row, a
/// card whose date is a data attribute, a link list whose titles are all
/// "Read more"). So every assertion below is against a real saved page, and the
/// counts are what a person reading that page would say.
///
/// The corpus, and what a human says about each:
///
///   hn-front-page.html            30 stories. No dates in the list itself,
///                                 almost every link off-host. The hard
///                                 positive.
///   mostlylucid-blog-index.html   20 posts. The page says so: "Page 1 of 40
///                                 (Total items: 794)" at pageSize=20. The easy
///                                 positive, and the one with dates and
///                                 standfirsts.
///   mostlylucid-post.html         One article. Not a list.
///   nasa.html                     One article, with rails of other articles
///                                 around it. Not a list.
///   verge.html                    One article, with rails of other articles
///                                 around it. Not a list.
///   hn-item.html                  A comment thread. Not a list.
///   no-feed.html                  A page with nothing on it at all.
/// </summary>
public class ArticleListDetectorTests
{
    private static string Html(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", name));

    private static ArticleListDetection Detect(string fixture, string url) =>
        ArticleListDetector.Detect(Html(fixture), new Uri(url));

    // -------------------------------------------------------------------
    // Positives
    // -------------------------------------------------------------------

    [Fact]
    public void Hacker_news_front_page_yields_its_thirty_stories()
    {
        var detection = Detect("hn-front-page.html", "https://news.ycombinator.com/");

        Assert.True(detection.IsArticleList);
        Assert.Equal(30, detection.Articles.Count);
        Assert.Contains("“I just chose words carefully”", detection.Articles.Select(a => a.Title));
    }

    /// <summary>
    /// The whole reason dates are a bonus rather than a gate. Hacker News shows
    /// no date anywhere in the list markup a reader would call a date; the
    /// timestamp lives in a title attribute on the age span, in the table row
    /// AFTER the story's own row. Both halves of that are load-bearing: the
    /// sibling fallback and the attribute-before-text ordering.
    /// </summary>
    [Fact]
    public void Hacker_news_dates_come_from_the_sibling_row()
    {
        var detection = Detect("hn-front-page.html", "https://news.ycombinator.com/");

        Assert.All(detection.Articles, a => Assert.NotNull(a.PublishedUtc));
    }

    /// <summary>
    /// Almost every Hacker News link points at somebody else's site, which is
    /// the shape the "mostly off-host" rejection has to be careful not to
    /// catch: an aggregator is not a blogroll.
    /// </summary>
    [Fact]
    public void Hacker_news_is_detected_despite_almost_every_link_being_off_host()
    {
        var detection = Detect("hn-front-page.html", "https://news.ycombinator.com/");

        var offHost = detection.Articles
            .Count(a => !new Uri(a.Link).Host.Equals(
                "news.ycombinator.com", StringComparison.OrdinalIgnoreCase));

        Assert.True(offHost > detection.Articles.Count / 2,
            "The fixture is meant to be an aggregator; if it is not, this test proves nothing.");
        Assert.True(detection.IsArticleList);
    }

    [Fact]
    public void The_mostlylucid_blog_index_yields_its_twenty_posts()
    {
        var detection = Detect("mostlylucid-blog-index.html", "https://www.mostlylucid.net/blog");

        Assert.True(detection.IsArticleList);

        // The page itself says "Page 1 of 40 (Total items: 794)" at a page size
        // of 20, so twenty is not a number this test chose.
        Assert.Equal(20, detection.Articles.Count);
        Assert.All(detection.Articles, a =>
            Assert.StartsWith("https://www.mostlylucid.net/blog/", a.Link, StringComparison.Ordinal));
    }

    [Fact]
    public void The_mostlylucid_blog_index_yields_dates_and_summaries()
    {
        var detection = Detect("mostlylucid-blog-index.html", "https://www.mostlylucid.net/blog");

        Assert.All(detection.Articles, a => Assert.NotNull(a.PublishedUtc));
        Assert.All(detection.Articles, a => Assert.False(string.IsNullOrWhiteSpace(a.Summary)));

        var newest = detection.Articles[0];
        Assert.Equal(new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero).Date,
            newest.PublishedUtc!.Value.Date);
    }

    /// <summary>
    /// The identity a scraped article carries has to be the same one the same
    /// article gets when it arrives from a real feed, or subscribing to both
    /// stores every post twice. Checked by computing the canonical id the feed
    /// path would compute and comparing.
    /// </summary>
    [Fact]
    public void Detected_articles_carry_the_same_canonical_id_a_feed_item_would()
    {
        var detection = Detect("mostlylucid-blog-index.html", "https://www.mostlylucid.net/blog");

        Assert.All(detection.Articles, a =>
            Assert.Equal(CanonicalArticleId.FromLink(a.Link), a.CanonicalId));
    }

    [Fact]
    public void Detected_articles_are_all_distinct()
    {
        var detection = Detect("hn-front-page.html", "https://news.ycombinator.com/");

        Assert.Equal(
            detection.Articles.Count,
            detection.Articles.Select(a => a.CanonicalId).Distinct(StringComparer.Ordinal).Count());
    }

    // -------------------------------------------------------------------
    // Negatives
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("mostlylucid-post.html", "https://www.mostlylucid.net/blog/signal-shingle-architecture")]
    [InlineData("nasa.html", "https://science.nasa.gov/blogs/ribbon-cutting/")]
    [InlineData("verge.html", "https://www.theverge.com/news/1/steam-leak")]
    [InlineData("hn-item.html", "https://news.ycombinator.com/item?id=1")]
    [InlineData("no-feed.html", "https://example.com/")]
    public void A_page_that_is_not_a_list_returns_nothing(string fixture, string url)
    {
        var detection = Detect(fixture, url);

        Assert.False(detection.IsArticleList);
        Assert.Empty(detection.Articles);
    }

    /// <summary>
    /// The negative that is not flattering, and the reason this test exists.
    ///
    /// A modern news article page carries rails of other articles, and those
    /// rails are structurally indistinguishable from an index: repeated
    /// siblings, one substantial link each, dates, all on-host. With the
    /// publisher's own declaration removed, the detector scores them as lists,
    /// and it should - they are lists. What tells an article page apart from an
    /// index page is not the markup, it is the declaration, so this test pins
    /// down that the declaration is doing that work rather than pretending the
    /// structure could.
    ///
    /// Two consequences follow, and both are deliberate. A page that declares
    /// nothing and shows a "more stories" rail can be offered wrongly - which
    /// is why the offer is a guess needing approval and why the sample titles
    /// are shown. And a page that declares itself an article is never offered,
    /// even when it does list other articles.
    /// </summary>
    [Theory]
    [InlineData("nasa.html", "https://science.nasa.gov/blogs/ribbon-cutting/")]
    [InlineData("verge.html", "https://www.theverge.com/news/1/steam-leak")]
    public void An_article_page_is_rejected_by_its_own_declaration_not_by_its_markup(
        string fixture, string url)
    {
        Assert.False(Detect(fixture, url).IsArticleList);

        var undeclared = StripDeclarations(Html(fixture));
        var detection = ArticleListDetector.Detect(undeclared, new Uri(url));

        Assert.True(detection.Confidence > 0.5,
            "With the declaration gone the related-article rail scores as a list, " +
            "which is what makes the declaration necessary.");
    }

    [Theory]
    [InlineData("mostlylucid-post.html", "https://www.mostlylucid.net/blog/signal-shingle-architecture")]
    public void An_article_page_with_no_repeated_links_is_rejected_structurally(
        string fixture, string url)
    {
        var undeclared = StripDeclarations(Html(fixture));

        var detection = ArticleListDetector.Detect(undeclared, new Uri(url));

        Assert.False(detection.IsArticleList);
        Assert.Empty(detection.Articles);
    }

    /// <summary>
    /// The blog index does not depend on any declaration to be detected: it
    /// declares og:type=website and a schema.org WebSite, neither of which the
    /// detector reads as permission. Stripping them changes nothing, which is
    /// what makes the positives structural rather than declaration-driven.
    /// </summary>
    [Fact]
    public void An_index_page_is_detected_with_no_declarations_at_all()
    {
        var undeclared = StripDeclarations(Html("mostlylucid-blog-index.html"));

        var detection = ArticleListDetector.Detect(
            undeclared, new Uri("https://www.mostlylucid.net/blog"));

        Assert.True(detection.IsArticleList);
        Assert.Equal(20, detection.Articles.Count);
    }

    private static string StripDeclarations(string html) =>
        Regex.Replace(
            Regex.Replace(html, """<meta[^>]*og:type[^>]*>""", "", RegexOptions.IgnoreCase),
            """<script[^>]*application/ld\+json[^>]*>.*?</script>""",
            "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

    // -------------------------------------------------------------------
    // The rules, checked one at a time
    // -------------------------------------------------------------------

    [Fact]
    public void Empty_input_is_not_a_list()
    {
        Assert.False(ArticleListDetector.Detect("", new Uri("https://example.com/")).IsArticleList);
        Assert.False(ArticleListDetector.Detect("   ", new Uri("https://example.com/")).IsArticleList);
    }

    /// <summary>
    /// A page's own address is the "you are here" link every index carries. It
    /// must not become one of the articles, or the first refresh subscribes the
    /// user to the index page as an article on itself.
    /// </summary>
    [Fact]
    public void The_pages_own_address_is_never_one_of_the_articles()
    {
        var detection = Detect("mostlylucid-blog-index.html", "https://www.mostlylucid.net/blog");

        Assert.DoesNotContain(
            CanonicalArticleId.FromLink("https://www.mostlylucid.net/blog"),
            detection.Articles.Select(a => a.CanonicalId));
    }

    /// <summary>
    /// Every link the detector hands back is one the app will later store and
    /// then fetch. FeedUrlPolicy is the gate that keeps loopback, link-local
    /// and private addresses out of that, and it has to be applied inside the
    /// detector rather than left to whoever consumes the result.
    /// </summary>
    [Fact]
    public void Links_that_fail_the_url_policy_are_dropped()
    {
        const string html = """
            <html><body><ul>
              <li class="post"><a href="https://good.example/one-two-three-four">A properly long title here</a></li>
              <li class="post"><a href="http://169.254.169.254/latest/meta-data/">Cloud metadata endpoint link</a></li>
              <li class="post"><a href="http://127.0.0.1:8080/admin/panel">Loopback administration panel</a></li>
              <li class="post"><a href="https://good.example/five-six-seven">Another properly long title</a></li>
              <li class="post"><a href="https://good.example/eight-nine">A third properly long title</a></li>
              <li class="post"><a href="https://good.example/ten-eleven">A fourth properly long title</a></li>
            </ul></body></html>
            """;

        var detection = ArticleListDetector.Detect(html, new Uri("https://good.example/"));

        Assert.All(detection.Articles, a => Assert.True(FeedUrlPolicy.IsAllowed(a.Link)));
        Assert.DoesNotContain(detection.Articles, a => a.Link.Contains("169.254"));
        Assert.DoesNotContain(detection.Articles, a => a.Link.Contains("127.0.0.1"));
    }

    [Fact]
    public void The_article_cap_is_honoured()
    {
        var detection = ArticleListDetector.Detect(
            Html("hn-front-page.html"), new Uri("https://news.ycombinator.com/"), maxArticles: 5);

        Assert.Equal(5, detection.Articles.Count);
    }

    /// <summary>
    /// Turning a detection into a ParsedFeed is what lets the refresh path
    /// store scraped articles through the same code that stores real ones. The
    /// guid has to be the canonical id: that is what makes an article stable
    /// across refreshes and what makes it dedupe against the same article
    /// arriving from a feed.
    /// </summary>
    [Fact]
    public void A_detection_converts_to_a_parsed_feed_keyed_on_the_canonical_id()
    {
        var detection = Detect("mostlylucid-blog-index.html", "https://www.mostlylucid.net/blog");

        var parsed = detection.ToParsedFeed("Blog Posts", new Uri("https://www.mostlylucid.net/blog"));

        Assert.Equal("Blog Posts", parsed.Title);
        Assert.Equal("https://www.mostlylucid.net/blog", parsed.SiteUrl);
        Assert.Equal(detection.Articles.Count, parsed.Items.Count);
        Assert.Equal(0, parsed.SkippedItemCount);

        for (var i = 0; i < parsed.Items.Count; i++)
        {
            Assert.Equal(detection.Articles[i].CanonicalId, parsed.Items[i].Guid);
            Assert.Equal(detection.Articles[i].Link, parsed.Items[i].Link);
            Assert.Equal(detection.Articles[i].Title, parsed.Items[i].Title);
        }
    }

    [Fact]
    public void Sample_titles_come_off_the_front_in_order()
    {
        var detection = Detect("hn-front-page.html", "https://news.ycombinator.com/");

        var sample = detection.SampleTitles(3);

        Assert.Equal(3, sample.Count);
        Assert.Equal(detection.Articles.Take(3).Select(a => a.Title), sample);
    }

    [Fact]
    public void Detection_is_deterministic_across_repeated_runs()
    {
        var first = Detect("hn-front-page.html", "https://news.ycombinator.com/");
        var second = Detect("hn-front-page.html", "https://news.ycombinator.com/");

        Assert.Equal(first.Confidence, second.Confidence, 10);
        Assert.Equal(
            first.Articles.Select(a => a.CanonicalId),
            second.Articles.Select(a => a.CanonicalId));
    }
}

/// <summary>
/// The shapes the detector has to say no to, written as small documents
/// because each one isolates a single gate. These do not replace the real
/// fixtures above; they pin down which rule rejects what, so a later change
/// that loosens one gate fails here with the name of the gate it broke rather
/// than as a mysterious count change on a 300KB page.
/// </summary>
public class ArticleListDetectorRejectionTests
{
    private static ArticleListDetection Detect(string html) =>
        ArticleListDetector.Detect(html, new Uri("https://example.com/index"));

    [Fact]
    public void A_nav_bar_is_not_an_article_list()
    {
        var detection = Detect("""
            <html><body><nav><ul>
              <li class="nav"><a href="/">Home</a></li>
              <li class="nav"><a href="/about">About</a></li>
              <li class="nav"><a href="/contact">Contact</a></li>
              <li class="nav"><a href="/archive">Archive</a></li>
              <li class="nav"><a href="/subscribe">Subscribe</a></li>
              <li class="nav"><a href="/search">Search</a></li>
            </ul></nav></body></html>
            """);

        Assert.False(detection.IsArticleList);
    }

    [Fact]
    public void A_tag_cloud_is_not_an_article_list()
    {
        var detection = Detect("""
            <html><body><div class="tags">
              <a class="tag" href="/tag/csharp">csharp</a>
              <a class="tag" href="/tag/dotnet">dotnet</a>
              <a class="tag" href="/tag/sqlite">sqlite</a>
              <a class="tag" href="/tag/avalonia">avalonia</a>
              <a class="tag" href="/tag/docker">docker</a>
              <a class="tag" href="/tag/linux">linux</a>
            </div></body></html>
            """);

        Assert.False(detection.IsArticleList);
    }

    [Fact]
    public void A_column_of_read_more_links_is_not_an_article_list()
    {
        var detection = Detect("""
            <html><body><div class="cards">
              <div class="card"><a href="/one">Continue reading this article</a></div>
              <div class="card"><a href="/two">Continue reading this article</a></div>
              <div class="card"><a href="/three">Continue reading this article</a></div>
              <div class="card"><a href="/four">Continue reading this article</a></div>
              <div class="card"><a href="/five">Continue reading this article</a></div>
            </div></body></html>
            """);

        Assert.False(detection.IsArticleList);
    }

    [Fact]
    public void A_pagination_strip_is_not_an_article_list()
    {
        var detection = Detect("""
            <html><body><div class="pager">
              <a class="page" href="/blog?page=1">1</a>
              <a class="page" href="/blog?page=2">2</a>
              <a class="page" href="/blog?page=3">3</a>
              <a class="page" href="/blog?page=4">4</a>
              <a class="page" href="/blog?page=5">5</a>
              <a class="page" href="/blog?page=6">Next page</a>
            </div></body></html>
            """);

        Assert.False(detection.IsArticleList);
    }

    [Fact]
    public void Three_articles_are_too_few_to_be_a_list()
    {
        var detection = Detect("""
            <html><body><div class="posts">
              <div class="post"><a href="/a">A perfectly plausible article title</a></div>
              <div class="post"><a href="/b">Another perfectly plausible title</a></div>
              <div class="post"><a href="/c">A third perfectly plausible title</a></div>
            </div></body></html>
            """);

        Assert.False(detection.IsArticleList);
    }

    /// <summary>
    /// A blogroll: a run of links all pointing at one or two other sites. This
    /// is the shape the off-host rule is aimed at, and the one Hacker News is
    /// deliberately not caught by - the difference is how many hosts are
    /// involved, not whether they are the page's own.
    /// </summary>
    [Fact]
    public void A_blogroll_pointing_at_one_other_site_is_not_an_article_list()
    {
        var detection = Detect("""
            <html><body><ul class="blogroll">
              <li class="link"><a href="https://elsewhere.example/one">Something worth reading here</a></li>
              <li class="link"><a href="https://elsewhere.example/two">Another thing worth reading</a></li>
              <li class="link"><a href="https://elsewhere.example/three">A third thing worth reading</a></li>
              <li class="link"><a href="https://elsewhere.example/four">A fourth thing worth reading</a></li>
              <li class="link"><a href="https://elsewhere.example/five">A fifth thing worth reading</a></li>
            </ul></body></html>
            """);

        Assert.False(detection.IsArticleList);
    }

    /// <summary>
    /// A plain index with no dates anywhere. Dates are worth 0.15 and nothing
    /// more, so a list has to clear the bar without them - which is the Hacker
    /// News case reduced to its essentials.
    /// </summary>
    [Fact]
    public void A_dateless_on_host_index_still_clears_the_bar()
    {
        var detection = Detect("""
            <html><body><ul class="posts">
              <li class="post"><a href="/posts/one">The first article on this weblog</a></li>
              <li class="post"><a href="/posts/two">The second article on this weblog</a></li>
              <li class="post"><a href="/posts/three">The third article on this weblog</a></li>
              <li class="post"><a href="/posts/four">The fourth article on this weblog</a></li>
              <li class="post"><a href="/posts/five">The fifth article on this weblog</a></li>
              <li class="post"><a href="/posts/six">The sixth article on this weblog</a></li>
            </ul></body></html>
            """);

        Assert.True(detection.IsArticleList);
        Assert.Equal(6, detection.Articles.Count);
        Assert.All(detection.Articles, a => Assert.Null(a.PublishedUtc));
    }
}
