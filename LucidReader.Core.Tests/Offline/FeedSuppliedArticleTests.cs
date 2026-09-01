using System.Net;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Offline;
using LucidReader.Core.Storage;
using LucidReader.Core.Sync;
using LucidReader.Core.Tests.Feeds;
using LucidReader.Core.Tests.Storage;
using MarkdownViewer.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Offline;

/// <summary>
/// The full body a feed already handed over must be kept and used, rather than
/// thrown away and re-fetched from the page it came from.
///
/// This is written against the shape alvinashcraft.com actually publishes,
/// because that is the feed the bug was found on: a short &lt;description&gt;
/// beside a whole post in &lt;content:encoded&gt;. Before V9 the refresh stored
/// only the description, so the downloader saw a stub, went to the network, and
/// left the reader with a teaser whenever that fetch failed or full-text
/// fetching was off - for an article mylo had already been given in full.
/// </summary>
/// <summary>
/// Stands in for the real converter's block classifier discarding everything it
/// was given, which is exactly what it did to a bare feed fragment before
/// FeedContentHtml wrapped one in a document.
/// </summary>
internal sealed class EmptyConverter : IHtmlToMarkdownService
{
    public Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default) =>
        Task.FromResult(" ");
}

public class FeedSuppliedArticleTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time =
        new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));
    /// <summary>
    /// The same recording converter OfflineDownloaderTests uses. Asserting on
    /// what HTML reached the converter is the point here: it says which body
    /// the downloader chose, which is exactly what this file is about, and it
    /// does not depend on the real pipeline's readability heuristics deciding
    /// that a paragraph of repeated filler is worth keeping.
    /// </summary>
    private readonly RecordingConverter _converter = new();

    private ReaderDatabase _db = null!;
    private FeedRepository _feeds = null!;
    private ItemRepository _items = null!;
    private TagRepository _tags = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _feeds = new FeedRepository(_db);
        _items = new ItemRepository(_db);
        _tags = new TagRepository(_db);
        _feedId = await _feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    /// <summary>
    /// A teaser short enough that StubDetector calls it a stub, beside a body
    /// long enough that it calls that a full article. The exact proportions of
    /// the real feed: a couple of hundred characters against tens of thousands.
    /// </summary>
    private const string Teaser =
        "<p>Top links for today, including a few things worth reading. Read more.</p>";

    private static string FullPost() =>
        "<p>" + string.Join(" ", Enumerable.Repeat("substantive prose here", 300)) + "</p>";

    private static string FeedXml(string description, string? encoded) =>
        $"""
         <?xml version="1.0" encoding="utf-8"?>
         <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/">
           <channel>
             <title>Morning Dew</title>
             <link>https://example.com</link>
             <item>
               <guid>https://example.com/post-1</guid>
               <link>https://example.com/post-1</link>
               <title>Dew Drop</title>
               <description><![CDATA[{description}]]></description>
               {(encoded is null ? "" : $"<content:encoded><![CDATA[{encoded}]]></content:encoded>")}
             </item>
           </channel>
         </rss>
         """;

    private FeedRefreshService CreateRefresh(StubHttpHandler handler) =>
        new(_feeds, _items, _tags,
            new FeedFetcher(handler.CreateClient()),
            new FeedParser(),
            new BackoffPolicy(new Random(1)),
            () => ReaderSettings.Defaults,
            _time);

    private OfflineDownloader CreateDownloader(
        StubHttpHandler handler, ReaderSettings? settings = null) =>
        new(_items, _feeds, new ArticleFetcher(handler.CreateClient()),
            _converter, () => settings ?? ReaderSettings.Defaults, _time);

    [Fact]
    public async Task A_refresh_stores_the_content_encoded_body_beside_the_summary()
    {
        var full = FullPost();
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, FeedXml(Teaser, full), mediaType: "application/rss+xml");
        await using var refresh = CreateRefresh(handler);

        await refresh.RefreshNowAsync(_feedId);

        var item = Assert.Single(await _items.QueryAsync(new ItemQuery { Limit = 10 }));
        Assert.Equal(Teaser, item.Summary);
        Assert.Equal(full, item.ContentHtml);
        Assert.True(item.ContentHtml!.Length > item.Summary!.Length * 10);
    }

    /// <summary>
    /// FeedParser falls back to the description when there is no
    /// content:encoded, so ContentHtml and Summary are then the same string.
    /// Storing both would double every such item's size to hold a second copy
    /// of the same text; the column is left null instead, which is what "the
    /// feed gave us nothing richer" means and what the downloader falls back to.
    /// </summary>
    [Fact]
    public async Task A_feed_with_no_richer_body_leaves_the_html_column_null()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, FeedXml(Teaser, null), mediaType: "application/rss+xml");
        await using var refresh = CreateRefresh(handler);

        await refresh.RefreshNowAsync(_feedId);

        var item = Assert.Single(await _items.QueryAsync(new ItemQuery { Limit = 10 }));
        Assert.Equal(Teaser, item.Summary);
        Assert.Null(item.ContentHtml);
    }

    [Fact]
    public async Task The_downloader_uses_the_stored_feed_body_instead_of_fetching_the_page()
    {
        var full = FullPost();
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = "post-1",
            Title = "Dew Drop",
            Link = "https://example.com/post-1",
            Summary = Teaser,
            ContentHtml = full,
            FirstSeenUtc = _time.GetUtcNow(),
            OfflineState = OfflineState.Pending
        });

        var pages = StubHttpHandler.Returning(HttpStatusCode.OK, "<html>page</html>");
        await using var downloader = CreateDownloader(pages);

        await downloader.DownloadNowAsync(id);

        Assert.Empty(pages.Requests);

        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Downloaded, item!.OfflineState);
        Assert.Equal(ContentSource.FeedArticle, item.ContentSource);
        Assert.NotNull(item.ContentMarkdown);

        // The whole post reached the converter, not the teaser. Contains
        // rather than Equal: FeedContentHtml wraps a feed fragment in a
        // document before conversion, without which the converter's block
        // classifier discards the lot as page furniture.
        Assert.Contains(full, Assert.Single(_converter.Converted));
    }

    /// <summary>
    /// The other half of the same rule. With nothing richer than the teaser
    /// stored, the page fetch is still the right thing to do, and what comes
    /// back is still Extracted.
    /// </summary>
    [Fact]
    public async Task A_teaser_with_no_stored_body_still_fetches_the_page()
    {
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = "post-2",
            Title = "Dew Drop",
            Link = "https://example.com/post-2",
            Summary = Teaser,
            FirstSeenUtc = _time.GetUtcNow(),
            OfflineState = OfflineState.Pending
        });

        var pages = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<html><body>" + FullPost() + "</body></html>",
            mediaType: "text/html");
        await using var downloader = CreateDownloader(pages);

        await downloader.DownloadNowAsync(id);

        Assert.Single(pages.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(ContentSource.Extracted, item!.ContentSource);
    }

    /// <summary>
    /// The case the maintainer cares most about: full-text fetching turned off,
    /// which used to leave the reader with 281 characters of an article the
    /// feed had sent in full. The stored body is used and no request is made.
    /// </summary>
    [Fact]
    public async Task The_feed_body_is_used_even_when_full_text_fetching_is_off()
    {
        var full = FullPost();
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = "post-3",
            Title = "Dew Drop",
            Link = "https://example.com/post-3",
            Summary = Teaser,
            ContentHtml = full,
            FirstSeenUtc = _time.GetUtcNow(),
            OfflineState = OfflineState.Pending
        });

        var pages = StubHttpHandler.Returning(HttpStatusCode.OK, "<html>page</html>");
        await using var downloader = CreateDownloader(
            pages, ReaderSettings.Defaults with { FetchFullText = false });

        await downloader.DownloadNowAsync(id);

        Assert.Empty(pages.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(ContentSource.FeedArticle, item!.ContentSource);
        Assert.Contains(full, Assert.Single(_converter.Converted));
    }

    /// <summary>
    /// The converter classifies blocks as article or furniture, and a feed
    /// fragment carries nothing to say which it is. Wrapping it in a document
    /// with an article element is what makes the classification come out right;
    /// unwrapped, alvinashcraft.com's real content:encoded converted to a
    /// single character.
    /// </summary>
    [Fact]
    public async Task Feed_html_reaches_the_converter_inside_a_document()
    {
        var full = FullPost();
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = "post-4",
            Title = "Dew Drop",
            Link = "https://example.com/post-4",
            Summary = Teaser,
            ContentHtml = full,
            FirstSeenUtc = _time.GetUtcNow(),
            OfflineState = OfflineState.Pending
        });

        await using var downloader = CreateDownloader(
            StubHttpHandler.Returning(HttpStatusCode.OK, "<html>page</html>"));
        await downloader.DownloadNowAsync(id);

        var converted = Assert.Single(_converter.Converted);
        Assert.StartsWith("<!doctype html>", converted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<article>", converted);
        Assert.Contains(full, converted);
    }

    /// <summary>
    /// A body the converter keeps nothing of must not be stored as a blank
    /// article: an empty reading pane says nothing and offers nothing. When
    /// there is a page to go to, the downloader goes to it.
    /// </summary>
    [Fact]
    public async Task A_feed_body_that_converts_to_nothing_falls_through_to_the_page()
    {
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = "post-5",
            Title = "Dew Drop",
            Link = "https://example.com/post-5",
            Summary = Teaser,
            ContentHtml = FullPost(),
            FirstSeenUtc = _time.GetUtcNow(),
            OfflineState = OfflineState.Pending
        });

        var pages = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<html><body>" + FullPost() + "</body></html>",
            mediaType: "text/html");
        await using var downloader = new OfflineDownloader(
            _items, _feeds, new ArticleFetcher(pages.CreateClient()),
            new EmptyConverter(), () => ReaderSettings.Defaults, _time);

        await downloader.DownloadNowAsync(id);

        Assert.Single(pages.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Downloaded, item!.OfflineState);
        Assert.Equal(ContentSource.Extracted, item.ContentSource);
    }

    /// <summary>
    /// The same case with nowhere to go: recorded as a failure, with a reason
    /// and a retry, rather than stored as an empty article.
    /// </summary>
    [Fact]
    public async Task A_feed_body_that_converts_to_nothing_and_has_no_page_is_a_failure()
    {
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = "post-6",
            Title = "Dew Drop",
            Summary = Teaser,
            ContentHtml = FullPost(),
            FirstSeenUtc = _time.GetUtcNow(),
            OfflineState = OfflineState.Pending
        });

        var pages = StubHttpHandler.Returning(HttpStatusCode.OK, "<html>page</html>");
        await using var downloader = new OfflineDownloader(
            _items, _feeds, new ArticleFetcher(pages.CreateClient()),
            new EmptyConverter(), () => ReaderSettings.Defaults, _time);

        await downloader.DownloadNowAsync(id);

        Assert.Empty(pages.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Failed, item!.OfflineState);
        Assert.Contains("could not be read", item.OfflineError!);
        Assert.Null(item.ContentMarkdown);
    }

    /// <summary>
    /// content_html is publisher-owned and follows the same COALESCE rule as
    /// title, summary and link: a publisher who stops emitting content:encoded
    /// must not blank the only complete copy of an article this database holds.
    /// </summary>
    [Fact]
    public async Task A_later_poll_without_the_body_does_not_erase_the_stored_one()
    {
        var full = FullPost();
        var withBody = StubHttpHandler.Returning(
            HttpStatusCode.OK, FeedXml(Teaser, full), mediaType: "application/rss+xml");
        await using (var refresh = CreateRefresh(withBody))
            await refresh.RefreshNowAsync(_feedId);

        var withoutBody = StubHttpHandler.Returning(
            HttpStatusCode.OK, FeedXml(Teaser, null), mediaType: "application/rss+xml");
        await using (var refresh = CreateRefresh(withoutBody))
            await refresh.RefreshNowAsync(_feedId);

        var item = Assert.Single(await _items.QueryAsync(new ItemQuery { Limit = 10 }));
        Assert.Equal(full, item.ContentHtml);
    }
}
