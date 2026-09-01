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
/// A feed added by any route other than the Add Feed dialog used to have a null
/// icon forever: nothing but autodiscovery ever looked, and autodiscovery only
/// runs in that dialog. Refresh is the one path every subscription takes, so it
/// is where the backfill belongs.
/// </summary>
public class FeedIconBackfillTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time =
        new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));

    private ReaderDatabase _db = null!;
    private FeedRepository _feeds = null!;
    private ItemRepository _items = null!;
    private TagRepository _tags = null!;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _feeds = new FeedRepository(_db);
        _items = new ItemRepository(_db);
        _tags = new TagRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private const string FeedWithImage =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0">
          <channel>
            <title>Example</title>
            <link>https://example.com</link>
            <image><url>https://example.com/channel.png</url><title>Example</title></image>
            <item><guid>1</guid><link>https://example.com/1</link><title>One</title></item>
          </channel>
        </rss>
        """;

    private const string FeedWithoutImage =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0">
          <channel>
            <title>Example</title>
            <link>https://example.com</link>
            <item><guid>1</guid><link>https://example.com/1</link><title>One</title></item>
          </channel>
        </rss>
        """;

    private FeedRefreshService CreateService(
        StubHttpHandler feedHandler, StubHttpHandler? siteHandler = null) =>
        new(_feeds, _items, _tags,
            new FeedFetcher(feedHandler.CreateClient()),
            new FeedParser(),
            new BackoffPolicy(new Random(7)),
            () => ReaderSettings.Defaults,
            _time,
            icons: siteHandler is null
                ? null
                : new FeedIconResolver(siteHandler.CreateClient(), () => ReaderSettings.Defaults));

    /// <summary>
    /// The shape a seeded starter feed, an OPML import and a catalogue pick all
    /// share: a row written with no icon at all.
    /// </summary>
    private Task<long> AddFeedWithNoIconAsync() =>
        _feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });

    [Fact]
    public async Task A_feed_added_without_an_icon_gets_the_channel_image_on_refresh()
    {
        var feedHandler = StubHttpHandler.Returning(
            HttpStatusCode.OK, FeedWithImage, mediaType: "application/rss+xml");
        var siteHandler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<html></html>", mediaType: "text/html");
        await using var service = CreateService(feedHandler, siteHandler);

        var id = await AddFeedWithNoIconAsync();
        await service.RefreshNowAsync(id);

        var feed = await _feeds.GetAsync(id);
        Assert.Equal("https://example.com/channel.png", feed!.IconPath);

        // The channel image is free, so no page was fetched to find it.
        Assert.Empty(siteHandler.Requests);
    }

    [Fact]
    public async Task A_feed_with_no_channel_image_falls_back_through_the_site()
    {
        var feedHandler = StubHttpHandler.Returning(
            HttpStatusCode.OK, FeedWithoutImage, mediaType: "application/rss+xml");
        var siteHandler = StubHttpHandler.Returning(
            HttpStatusCode.OK,
            """<html><head><link rel="icon" href="/icon.png"></head></html>""",
            mediaType: "text/html");
        await using var service = CreateService(feedHandler, siteHandler);

        var id = await AddFeedWithNoIconAsync();
        await service.RefreshNowAsync(id);

        var feed = await _feeds.GetAsync(id);
        Assert.Equal("https://example.com/icon.png", feed!.IconPath);
    }

    [Fact]
    public async Task An_icon_already_recorded_is_left_alone_and_costs_no_lookup()
    {
        var feedHandler = StubHttpHandler.Returning(
            HttpStatusCode.OK, FeedWithImage, mediaType: "application/rss+xml");
        var siteHandler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<html></html>", mediaType: "text/html");
        await using var service = CreateService(feedHandler, siteHandler);

        var id = await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            IconPath = "https://example.com/chosen-earlier.png"
        });

        await service.RefreshNowAsync(id);

        var feed = await _feeds.GetAsync(id);
        Assert.Equal("https://example.com/chosen-earlier.png", feed!.IconPath);
        Assert.Empty(siteHandler.Requests);
    }

    /// <summary>
    /// The whole point of doing this beside the refresh rather than inside it:
    /// an icon is a nicety and a failure to find one must leave the refresh, its
    /// items and its success bookkeeping exactly as they were.
    /// </summary>
    [Fact]
    public async Task An_icon_lookup_that_throws_does_not_affect_the_refresh()
    {
        var feedHandler = StubHttpHandler.Returning(
            HttpStatusCode.OK, FeedWithoutImage, mediaType: "application/rss+xml");
        var siteHandler = StubHttpHandler.Throwing(new HttpRequestException("no route"));
        await using var service = CreateService(feedHandler, siteHandler);

        var id = await AddFeedWithNoIconAsync();
        var outcome = await service.RefreshNowAsync(id);

        Assert.True(outcome.Success);
        Assert.Equal(1, outcome.NewItemCount);

        var feed = await _feeds.GetAsync(id);
        Assert.Equal(0, feed!.ConsecutiveFailures);
        Assert.Null(feed.LastError);

        // The favicon guess still stands even when the page could not be read.
        Assert.Equal("https://example.com/favicon.ico", feed.IconPath);
    }

    /// <summary>
    /// Built without a resolver - which is what every test that does not care
    /// about icons does - nothing is looked up and nothing is written.
    /// </summary>
    [Fact]
    public async Task With_no_resolver_configured_nothing_is_backfilled()
    {
        var feedHandler = StubHttpHandler.Returning(
            HttpStatusCode.OK, FeedWithImage, mediaType: "application/rss+xml");
        await using var service = CreateService(feedHandler);

        var id = await AddFeedWithNoIconAsync();
        await service.RefreshNowAsync(id);

        var feed = await _feeds.GetAsync(id);
        Assert.Null(feed!.IconPath);
    }

    /// <summary>
    /// The write re-checks in SQL what the caller checked on a snapshot, so an
    /// icon that appeared between the two - from the add dialog, an import, or
    /// another refresh - wins over a later resolution rather than being
    /// overwritten by it.
    /// </summary>
    [Fact]
    public async Task The_write_never_replaces_an_icon_that_is_already_there()
    {
        var id = await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            IconPath = "https://example.com/first.png"
        });

        await _feeds.UpdateIconPathIfMissingAsync(id, "https://example.com/second.png");

        var feed = await _feeds.GetAsync(id);
        Assert.Equal("https://example.com/first.png", feed!.IconPath);
    }
}
