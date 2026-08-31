using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// A scraped page is read once by the detector, the answer is kept, and the
/// next refresh uses it. Then the page is redesigned and the kept answer has to
/// get out of the way rather than quietly return a short list.
///
/// The short-list case is the one worth writing a test for. A scrape that
/// returns nothing is already recorded as a failure and shows up on the feed
/// row; a scrape that returns four of thirty articles is recorded as a success
/// and reads, to the person waiting for new articles, exactly like a site that
/// has stopped publishing.
/// </summary>
public class ScrapeTemplateTests : IAsyncLifetime
{
    private string _directory = "";
    private ScrapeTemplateStore _store = null!;

    public Task InitializeAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mylo-scrape-templates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new ScrapeTemplateStore(Path.Combine(_directory, ScrapeTemplateStore.FileName));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static readonly Uri PageUri = new("https://notes.example/journal");

    private static string Page(int entries, string itemClass = "post", string wrapper = "main")
    {
        var body = new System.Text.StringBuilder();
        body.Append("<html><head><title>Journal</title></head><body>");
        body.Append("<nav><a href=\"/\">Home</a><a href=\"/about\">About this journal</a></nav>");
        body.Append($"<{wrapper}>");
        for (var i = 1; i <= entries; i++)
        {
            body.Append($"<div class=\"{itemClass}\">");
            body.Append($"<h2><a href=\"/journal/entry-{i}\">Entry number {i} and what came of it</a></h2>");
            body.Append($"<time datetime=\"2026-03-{i:00}\">{i} March 2026</time>");
            body.Append($"<p class=\"blurb\">A standfirst for entry number {i}.</p>");
            body.Append("</div>");
        }
        body.Append($"</{wrapper}></body></html>");
        return body.ToString();
    }

    [Fact]
    public async Task The_first_scrape_uses_the_detector_and_the_second_uses_the_template()
    {
        var html = Page(12);

        var first = await ScrapedPageReader.ReadAsync(html, PageUri, _store);

        Assert.Equal(ScrapeSource.Detector, first.Source);
        Assert.Equal(12, first.Articles.Count);

        var second = await ScrapedPageReader.ReadAsync(html, PageUri, _store);

        Assert.Equal(ScrapeSource.Template, second.Source);
        Assert.Equal(
            first.Articles.Select(a => a.Link),
            second.Articles.Select(a => a.Link));
        Assert.Equal(
            first.Articles.Select(a => a.Title),
            second.Articles.Select(a => a.Title));
        Assert.Equal(
            first.Articles.Select(a => a.CanonicalId),
            second.Articles.Select(a => a.CanonicalId));
    }

    /// <summary>
    /// The store is a file, so the second refresh is allowed to be a different
    /// run of the application. Opening a second store over the same file is as
    /// close as a test gets to restarting mylo, and it is what catches a host
    /// key that was random per process.
    /// </summary>
    [Fact]
    public async Task A_template_stored_in_one_run_is_found_by_the_next()
    {
        var html = Page(9);
        await ScrapedPageReader.ReadAsync(html, PageUri, _store);
        await _store.DisposeAsync();

        var reopened = new ScrapeTemplateStore(Path.Combine(_directory, ScrapeTemplateStore.FileName));
        try
        {
            var reading = await ScrapedPageReader.ReadAsync(html, PageUri, reopened);
            Assert.Equal(ScrapeSource.Template, reading.Source);
            Assert.Equal(9, reading.Articles.Count);
        }
        finally
        {
            await reopened.DisposeAsync();
            _store = new ScrapeTemplateStore(Path.Combine(_directory, "second-" + ScrapeTemplateStore.FileName));
        }
    }

    /// <summary>
    /// A page that has published since the template was learned yields the new
    /// entry too, which is the whole reason for storing a shape rather than a
    /// list of links.
    /// </summary>
    [Fact]
    public async Task A_template_picks_up_entries_published_after_it_was_learned()
    {
        await ScrapedPageReader.ReadAsync(Page(10), PageUri, _store);

        var reading = await ScrapedPageReader.ReadAsync(Page(13), PageUri, _store);

        Assert.Equal(ScrapeSource.Template, reading.Source);
        Assert.Equal(13, reading.Articles.Count);
        Assert.Contains(reading.Articles, a => a.Link.EndsWith("/journal/entry-13", StringComparison.Ordinal));
    }

    /// <summary>
    /// The redesign case. The page still lists the same twelve entries and a
    /// person would still call it an index; only the markup changed. The
    /// template must not be the reason the refresh comes back short, so the
    /// detector runs again and the full list comes back.
    /// </summary>
    [Fact]
    public async Task A_redesigned_page_falls_back_to_the_detector_rather_than_returning_a_short_list()
    {
        await ScrapedPageReader.ReadAsync(Page(12), PageUri, _store);

        var redesigned = Page(12, itemClass: "entry-card", wrapper: "section");
        var reading = await ScrapedPageReader.ReadAsync(redesigned, PageUri, _store);

        Assert.Equal(ScrapeSource.Detector, reading.Source);
        Assert.Equal(12, reading.Articles.Count);
    }

    /// <summary>
    /// And the redesign is learned in turn, so the fallback costs one refresh
    /// rather than every refresh from then on.
    /// </summary>
    [Fact]
    public async Task The_redesigned_page_is_learned_on_the_refresh_that_fell_back()
    {
        await ScrapedPageReader.ReadAsync(Page(12), PageUri, _store);

        var redesigned = Page(12, itemClass: "entry-card", wrapper: "section");
        await ScrapedPageReader.ReadAsync(redesigned, PageUri, _store);

        var reading = await ScrapedPageReader.ReadAsync(redesigned, PageUri, _store);

        Assert.Equal(ScrapeSource.Template, reading.Source);
        Assert.Equal(12, reading.Articles.Count);
    }

    /// <summary>
    /// A page that has stopped being a list at all is still a failure, template
    /// or no template. This is the behaviour that was already there and the one
    /// that must not be lost: it is what puts the reason on the feed row and
    /// eventually pauses the feed, instead of recording a refresh with no news.
    /// </summary>
    [Fact]
    public async Task A_page_that_is_no_longer_a_list_still_fails_the_refresh()
    {
        await ScrapedPageReader.ReadAsync(Page(12), PageUri, _store);

        const string gone = "<html><body><h1>Moved</h1><p>This journal has moved elsewhere.</p></body></html>";

        await Assert.ThrowsAsync<FeedScrapeException>(
            () => ScrapedPageReader.ReadAsync(gone, PageUri, _store));
    }

    /// <summary>
    /// A page that keeps each entry's date outside the entry is left to the
    /// detector.
    ///
    /// Hacker News is the real one: a story's title and a story's age are two
    /// separate table rows, and the detector reads the second when the first
    /// has no date of its own. A field rule is written relative to the entry's
    /// own root and cannot reach a sibling, so a template here would return the
    /// same articles undated, and undated articles sort to the bottom of a list
    /// the user reads by date. Caching an answer that is worse than the answer
    /// is not a cache.
    /// </summary>
    [Fact]
    public async Task A_page_whose_dates_a_template_could_not_carry_is_not_cached()
    {
        var body = new System.Text.StringBuilder();
        body.Append("<html><head><title>Journal</title></head><body><main>");
        for (var i = 1; i <= 12; i++)
        {
            body.Append("<div class=\"post\">");
            body.Append($"<h2><a href=\"/journal/entry-{i}\">Entry number {i} and what came of it</a></h2>");
            body.Append("</div>");
            body.Append($"<div class=\"meta\"><span class=\"date\">{i} March 2026</span></div>");
        }
        body.Append("</main></body></html>");
        var html = body.ToString();

        var first = await ScrapedPageReader.ReadAsync(html, PageUri, _store);
        Assert.Equal(ScrapeSource.Detector, first.Source);
        Assert.Equal(12, first.Articles.Count);
        Assert.All(first.Articles, a => Assert.NotNull(a.PublishedUtc));

        var second = await ScrapedPageReader.ReadAsync(html, PageUri, _store);

        Assert.Equal(ScrapeSource.Detector, second.Source);
        Assert.All(second.Articles, a => Assert.NotNull(a.PublishedUtc));
    }

    /// <summary>
    /// With no store the reader is what it was before templates existed.
    /// </summary>
    [Fact]
    public async Task Without_a_store_every_scrape_asks_the_detector()
    {
        var reading = await ScrapedPageReader.ReadAsync(Page(12), PageUri, store: null);

        Assert.Equal(ScrapeSource.Detector, reading.Source);
        Assert.Equal(12, reading.Articles.Count);
    }
}
