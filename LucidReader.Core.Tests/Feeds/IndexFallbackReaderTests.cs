using AngleSharp;
using AngleSharp.Dom;
using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// The fallback's gates, and what a scraped feed that only exists because of it
/// does on the refreshes after the first.
///
/// The corpus side of this lives in
/// <see cref="ScrapedPageAcceptanceCorpusTests"/>, which measures both paths
/// over the twenty-five saved pages. What is here is what the corpus cannot
/// show on its own: that the ordering holds at the point of use rather than only
/// by every caller remembering it, that the publisher's own declaration is what
/// declines a declared page and not something incidental, and what a feed the
/// fallback accepted does on the refreshes after the first.
/// </summary>
public class IndexFallbackReaderTests : IAsyncLifetime
{
    private string _directory = "";
    private ScrapeTemplateStore _store = null!;

    public Task InitializeAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mylo-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new ScrapeTemplateStore(Path.Combine(_directory, ScrapeTemplateStore.FileName));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static readonly Uri LwnUri = new("https://lwn.net/");

    private static string Lwn() => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", "corpus", "lwn.html"));

    private static IDocument Parse(string html, Uri uri) =>
        BrowsingContext.New(Configuration.Default)
            .OpenAsync(request => request.Content(html).Address(uri.ToString()))
            .GetAwaiter().GetResult();

    private static ArticleListDetection? Read(string html, Uri uri) =>
        IndexFallbackReader.TryRead(
            Parse(html, uri), uri, ArticleListDetector.Detect(html, uri));

    /// <summary>
    /// The page the fallback exists for, read end to end through the reader the
    /// refresh service calls, with no store, which is the shape of a first
    /// read. The source says fallback rather than detector, because a feed that
    /// only exists because the second pass found it is not the same thing as
    /// one mylo read itself.
    /// </summary>
    [Fact]
    public async Task A_page_the_detector_declines_is_read_by_the_fallback()
    {
        var html = Lwn();

        Assert.False(ArticleListDetector.Detect(html, LwnUri).IsArticleList);

        var reading = await ScrapedPageReader.ReadAsync(html, LwnUri, store: null);

        Assert.Equal(ScrapeSource.Fallback, reading.Source);
        Assert.True(reading.Articles.Count >= ArticleListDetector.MinimumArticles);
        Assert.All(reading.Articles, a => Assert.StartsWith(
            "https://lwn.net/Articles/", a.Link, StringComparison.Ordinal));
    }

    /// <summary>
    /// And a page the detector does read is read by the detector, with the
    /// fallback never consulted. This is the property everything else rests on,
    /// so it is asserted on a real page as well as on the corpus.
    /// </summary>
    [Fact]
    public async Task A_page_the_detector_reads_never_reaches_the_fallback()
    {
        var html = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Html", "corpus", "jvns.html"));
        var uri = new Uri("https://jvns.ca/");

        var reading = await ScrapedPageReader.ReadAsync(html, uri, store: null);

        Assert.Equal(ScrapeSource.Detector, reading.Source);
        Assert.Null(IndexFallbackReader.TryRead(
            Parse(html, uri), uri, ArticleListDetector.Detect(html, uri)));
    }

    /// <summary>
    /// A caller that hands the fallback an accepted detection gets nothing,
    /// whatever the page. The ordering is enforced at the point of use rather
    /// than only by the callers observing it.
    /// </summary>
    [Fact]
    public void The_fallback_declines_outright_when_the_detector_accepted_the_page()
    {
        var html = Lwn();
        var pretending = new ArticleListDetection
        {
            IsArticleList = true,
            Reason = "Pretend the detector read this page."
        };

        Assert.Null(IndexFallbackReader.TryRead(Parse(html, LwnUri), LwnUri, pretending));
    }

    /// <summary>
    /// A page whose publisher has said it is one article is declined outright,
    /// rather than given the higher bar the detector gives it.
    ///
    /// A news article's "more stories" rail is a genuine, well-formed,
    /// same-host list of articles and no structural rule tells it from an
    /// index. On this path the same-host share is the only other signal and a
    /// rail passes it, so the publisher's own statement has to be the end of it.
    ///
    /// <para>The evidence is the lwn front page with one line of markup added
    /// and read at an address that is not the site root, which is the only
    /// place a declaration counts for anything. Without the line the fallback
    /// reads the page; with it the fallback declines. Nothing else about the
    /// page differs, so the declaration is demonstrably what did the
    /// work.</para>
    /// </summary>
    [Fact]
    public void A_page_that_declares_itself_one_article_is_declined_by_the_fallback()
    {
        // Not the site root. A declaration on a page that lives at "/" is the
        // publisher's template misfiring and is ignored by both paths, so a
        // test written at the root would prove nothing either way.
        var uri = new Uri("https://lwn.net/2026/");
        var html = Lwn();

        var undeclared = Read(html, uri);
        Assert.NotNull(undeclared);
        Assert.True(undeclared.Articles.Count >= ArticleListDetector.MinimumArticles);

        var declared = html.Replace(
            "<head>", "<head><meta property=\"og:type\" content=\"article\">",
            StringComparison.Ordinal);
        Assert.NotEqual(html, declared);

        Assert.Null(Read(declared, uri));
    }

    /// <summary>
    /// The refresh path, and what it honestly does.
    ///
    /// <para>A feed the fallback accepted is polled forever afterwards, so the
    /// question is what the second poll does. For lwn the answer is that it runs
    /// the fallback again, because no template can be learned from that page:
    /// the entry's title is the heading's own text and its address is an anchor
    /// in a sibling, and a field rule is written relative to the entry's root
    /// and cannot reach out of it. Induction produces a rule set with a
    /// permalink and a date and no title, every record it returns comes back
    /// untitled, and the guard that was already in
    /// <see cref="ScrapedPageReader"/> throws that template away rather than
    /// storing an answer worse than the answer.</para>
    ///
    /// <para>That is the correct outcome and it is asserted rather than worked
    /// around, because the alternative - storing the template anyway - would
    /// mean every poll after the first returned nothing, fell back, and stored
    /// it again. What matters for the user is the last two assertions: the poll
    /// after the first returns the same articles, so a feed accepted by the
    /// fallback keeps working.</para>
    /// </summary>
    [Fact]
    public async Task A_feed_the_fallback_accepted_keeps_reading_on_later_refreshes()
    {
        var html = Lwn();

        var first = await ScrapedPageReader.ReadAsync(html, LwnUri, _store);
        Assert.Equal(ScrapeSource.Fallback, first.Source);

        var second = await ScrapedPageReader.ReadAsync(html, LwnUri, _store);
        Assert.Equal(ScrapeSource.Fallback, second.Source);

        Assert.Equal(
            first.Articles.Select(a => a.Link).Order(),
            second.Articles.Select(a => a.Link).Order());
    }

    /// <summary>
    /// And the safety property the template path already had holds on this path
    /// too: a page that has stopped being a list is a failed refresh, not a
    /// refresh with no news. The fallback must not turn a dead page into a
    /// quiet one.
    /// </summary>
    [Fact]
    public async Task A_fallback_feed_whose_page_stopped_being_a_list_fails_the_refresh()
    {
        await ScrapedPageReader.ReadAsync(Lwn(), LwnUri, _store);

        const string gone = "<html><body><h1>Moved</h1><p>This site has moved elsewhere.</p></body></html>";

        await Assert.ThrowsAsync<FeedScrapeException>(
            () => ScrapedPageReader.ReadAsync(gone, LwnUri, _store));
    }
}
