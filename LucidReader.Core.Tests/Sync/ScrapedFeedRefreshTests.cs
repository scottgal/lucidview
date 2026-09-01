using System.Net;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Core.Sync;
using LucidReader.Core.Tests.Feeds;
using LucidReader.Core.Tests.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Sync;

/// <summary>
/// A scraped subscription refreshing on the ordinary schedule.
///
/// The point being proved is that a scrape is not a parallel pipeline: the
/// detection is turned into a ParsedFeed and stored by exactly the code a real
/// feed's items go through, so dedupe, tombstones, retention, tags, read and
/// starred state and the offline queue all keep working with no knowledge that
/// a scrape happened. The tests that matter most are the ones about a scrape
/// that stops working, because that is the failure this feature invents.
/// </summary>
public class ScrapedFeedRefreshTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));

    private ReaderDatabase _db = null!;
    private FeedRepository _feeds = null!;
    private ItemRepository _items = null!;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _feeds = new FeedRepository(_db);
        _items = new ItemRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private static string Html(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", name));

    private FeedRefreshService CreateService(StubHttpHandler handler) =>
        new(_feeds, _items, new TagRepository(_db),
            new FeedFetcher(handler.CreateClient()),
            new FeedParser(),
            new BackoffPolicy(new Random(999)),
            () => ReaderSettings.Defaults,
            _time);

    private Task<long> AddScrapedAsync(string url = "https://www.mostlylucid.net/blog") =>
        _feeds.AddAsync(new Feed
        {
            FeedUrl = url,
            Title = "Blog Posts",
            SourceKind = FeedSourceKind.ScrapedPage
        });

    [Fact]
    public async Task Refreshing_a_scraped_feed_stores_the_detected_articles()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("mostlylucid-blog-index.html"), mediaType: "text/html");
        await using var service = CreateService(handler);
        var feedId = await AddScrapedAsync();

        var outcome = await service.RefreshNowAsync(feedId);

        Assert.True(outcome.Success);
        Assert.Equal(20, outcome.NewItemCount);

        var stored = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 100, 0));
        Assert.Equal(20, stored.Count);
        Assert.All(stored, item => Assert.False(string.IsNullOrWhiteSpace(item.Title)));
        Assert.All(stored, item => Assert.NotNull(item.CanonicalId));
    }

    /// <summary>
    /// Refreshing twice must not produce forty items. The guid is the canonical
    /// id, which is stable across refreshes, so the second pass upserts onto the
    /// same rows.
    /// </summary>
    [Fact]
    public async Task Refreshing_a_scraped_feed_twice_stores_each_article_once()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("mostlylucid-blog-index.html"), mediaType: "text/html");
        await using var service = CreateService(handler);
        var feedId = await AddScrapedAsync();

        await service.RefreshNowAsync(feedId);
        var second = await service.RefreshNowAsync(feedId);

        Assert.True(second.Success);
        Assert.Equal(0, second.NewItemCount);

        var stored = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 200, 0));
        Assert.Equal(20, stored.Count);
    }

    /// <summary>
    /// A scraped article and the same article arriving from a real feed are one
    /// article. This is the dedupe the canonical id exists for, and the reason
    /// the detector computes it rather than leaving it to the caller.
    /// </summary>
    [Fact]
    public async Task A_scraped_article_carries_the_canonical_id_a_feed_item_would()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("mostlylucid-blog-index.html"), mediaType: "text/html");
        await using var service = CreateService(handler);
        var feedId = await AddScrapedAsync();

        await service.RefreshNowAsync(feedId);

        var stored = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 200, 0));
        Assert.All(stored, item =>
            Assert.Equal(CanonicalArticleId.FromLink(item.Link), item.CanonicalId));
    }

    /// <summary>
    /// The failure this feature invents, and the one it must not get wrong.
    ///
    /// The site changed its layout, so the scrape now finds nothing. Recorded as
    /// a success with zero new items it would be indistinguishable from "nothing
    /// published today" and could stay that way forever. It is recorded as a
    /// failure instead, which puts a reason on the row, marks the sidebar,
    /// backs the schedule off, and eventually auto-pauses the feed into the
    /// status bar's health line.
    /// </summary>
    [Fact]
    public async Task A_scrape_that_stops_finding_articles_is_recorded_as_a_failure()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("no-feed.html"), mediaType: "text/html");
        await using var service = CreateService(handler);
        var feedId = await AddScrapedAsync("https://example.com/");

        var outcome = await service.RefreshNowAsync(feedId);

        Assert.False(outcome.Success);
        Assert.Contains("no longer looks like a list of articles", outcome.Error);

        var feed = (await _feeds.GetAsync(feedId))!;
        Assert.Equal(1, feed.ConsecutiveFailures);
        Assert.NotNull(feed.LastError);
        Assert.Null(feed.LastSuccessUtc);
    }

    /// <summary>
    /// A broken scrape must not delete what it already collected. The user's
    /// unread articles are still theirs while the site is being fixed.
    /// </summary>
    [Fact]
    public async Task A_scrape_that_stops_finding_articles_leaves_the_stored_ones_alone()
    {
        var working = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("mostlylucid-blog-index.html"), mediaType: "text/html");
        var feedId = await AddScrapedAsync();

        await using (var service = CreateService(working))
            await service.RefreshNowAsync(feedId);

        var broken = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("no-feed.html"), mediaType: "text/html");
        await using (var service = CreateService(broken))
            Assert.False((await service.RefreshNowAsync(feedId)).Success);

        var stored = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 200, 0));
        Assert.Equal(20, stored.Count);
    }

    /// <summary>
    /// Repeated failures reach the same auto-pause every dead feed reaches, so
    /// a scrape that has stopped working ends up in the status bar's health
    /// line rather than going quiet.
    /// </summary>
    [Fact]
    public async Task Repeated_scrape_failures_eventually_auto_pause_the_feed()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("no-feed.html"), mediaType: "text/html");
        await using var service = CreateService(handler);
        var feedId = await AddScrapedAsync("https://example.com/");

        for (var attempt = 0; attempt < BackoffPolicy.AutoPauseThreshold; attempt++)
        {
            await service.RefreshNowAsync(feedId);
            if ((await _feeds.GetAsync(feedId))!.AutoPausedUtc is not null) break;
        }

        var feed = (await _feeds.GetAsync(feedId))!;
        Assert.NotNull(feed.AutoPausedUtc);
        Assert.False(feed.IsEnabled);
    }

    /// <summary>
    /// A page that turns into a single article - a site collapsing its index
    /// onto its latest post, or a redirect landing somewhere else - is the same
    /// failure as an empty one, and must not quietly subscribe the user to one
    /// article forever.
    /// </summary>
    [Fact]
    public async Task A_page_that_becomes_a_single_article_is_recorded_as_a_failure()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("mostlylucid-post.html"), mediaType: "text/html");
        await using var service = CreateService(handler);
        var feedId = await AddScrapedAsync();

        var outcome = await service.RefreshNowAsync(feedId);

        Assert.False(outcome.Success);
        Assert.Contains("single article", outcome.Error);
    }

    /// <summary>
    /// A published feed must be completely unaffected by any of this: it still
    /// goes through the XML parser, even when the response happens to be HTML
    /// the detector could have read.
    /// </summary>
    [Fact]
    public async Task A_published_feed_still_goes_through_the_xml_parser()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("mostlylucid-blog-index.html"), mediaType: "text/html");
        await using var service = CreateService(handler);
        var feedId = await _feeds.AddAsync(new Feed { FeedUrl = "https://www.mostlylucid.net/blog" });

        var outcome = await service.RefreshNowAsync(feedId);

        Assert.False(outcome.Success);
        Assert.Equal(0, outcome.NewItemCount);
    }

    /// <summary>
    /// A scraped feed is named once, when the user approves it. A site that
    /// changes its title element must not silently rename a subscription the
    /// user has filed and sorted.
    /// </summary>
    [Fact]
    public async Task A_scraped_feed_keeps_the_title_it_was_approved_under()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("mostlylucid-blog-index.html"), mediaType: "text/html");
        await using var service = CreateService(handler);
        var feedId = await AddScrapedAsync();

        await service.RefreshNowAsync(feedId);

        Assert.Equal("Blog Posts", (await _feeds.GetAsync(feedId))!.Title);
    }
}
