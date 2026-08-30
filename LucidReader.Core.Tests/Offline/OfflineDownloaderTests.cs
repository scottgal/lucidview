using System.Net;
using LucidReader.Core.Model;
using LucidReader.Core.Offline;
using LucidReader.Core.Storage;
using LucidReader.Core.Tests.Feeds;
using LucidReader.Core.Tests.Storage;
using MarkdownViewer.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Offline;

/// <summary>
/// A converter that records what it was given, so tests can assert which HTML
/// reached it without depending on AngleSharp's exact markdown output.
/// </summary>
internal sealed class RecordingConverter : IHtmlToMarkdownService
{
    public List<string> Converted { get; } = [];

    public Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default)
    {
        Converted.Add(html);
        return Task.FromResult("# Converted\n\n" + html.Length + " characters of HTML.");
    }
}

public class OfflineDownloaderTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time =
        new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));
    private readonly RecordingConverter _converter = new();

    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private FeedRepository _feeds = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _feeds = new FeedRepository(_db);
        _feedId = await _feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private OfflineDownloader CreateDownloader(
        StubHttpHandler handler, TimeSpan? maxFetchDuration = null) =>
        new(_items, _feeds, new ArticleFetcher(handler.CreateClient()),
            _converter, () => ReaderSettings.Defaults, _time, maxFetchDuration: maxFetchDuration);

    private Task<long> AddItemAsync(string? summary, OfflineState state = OfflineState.Pending) =>
        _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = Guid.NewGuid().ToString(),
            Title = "An article",
            Link = "https://example.com/article",
            Summary = summary,
            FirstSeenUtc = _time.GetUtcNow(),
            OfflineState = state
        });

    private static string LongArticle() =>
        "<p>" + string.Join(" ", Enumerable.Repeat("substantive prose here", 200)) + "</p>";

    [Fact]
    public async Task Feed_content_that_is_already_complete_is_converted_without_a_page_fetch()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html>full page</html>");
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync(LongArticle());

        await downloader.DownloadNowAsync(id);

        Assert.Empty(handler.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Downloaded, item!.OfflineState);
        Assert.Equal(ContentSource.Feed, item.ContentSource);
        Assert.NotNull(item.ContentMarkdown);
    }

    [Fact]
    public async Task A_stub_triggers_a_page_fetch_and_is_stored_as_extracted()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<html><body>" + LongArticle() + "</body></html>",
            mediaType: "text/html");
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync("<p>Short teaser.</p>");

        await downloader.DownloadNowAsync(id);

        Assert.Single(handler.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Downloaded, item!.OfflineState);
        Assert.Equal(ContentSource.Extracted, item.ContentSource);
    }

    [Fact]
    public async Task A_failed_page_fetch_leaves_the_summary_readable()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotFound);
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync("<p>Short teaser.</p>");

        await downloader.DownloadNowAsync(id);

        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Failed, item!.OfflineState);
        Assert.NotNull(item.OfflineError);
        Assert.Equal("<p>Short teaser.</p>", item.Summary);
    }

    [Fact]
    public async Task A_stub_with_no_link_falls_back_to_converting_the_summary()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html>page</html>");
        await using var downloader = CreateDownloader(handler);
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = "no-link",
            Summary = "<p>Short teaser.</p>",
            Link = null,
            FirstSeenUtc = _time.GetUtcNow(),
            OfflineState = OfflineState.Pending
        });

        await downloader.DownloadNowAsync(id);

        Assert.Empty(handler.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Downloaded, item!.OfflineState);
        Assert.Equal(ContentSource.Feed, item.ContentSource);
    }

    [Fact]
    public async Task Full_text_fetch_disabled_on_the_feed_skips_the_page_fetch()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html>page</html>");
        var feedId = await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://nofulltext.example/feed.xml",
            FetchFullText = false
        });
        await using var downloader = CreateDownloader(handler);
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = feedId,
            Guid = "x",
            Summary = "<p>Short teaser.</p>",
            Link = "https://nofulltext.example/article",
            FirstSeenUtc = _time.GetUtcNow(),
            OfflineState = OfflineState.Pending
        });

        await downloader.DownloadNowAsync(id);

        Assert.Empty(handler.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(ContentSource.Feed, item!.ContentSource);
    }

    [Fact]
    public async Task Queueing_pending_work_picks_up_everything_marked_pending()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, LongArticle());
        await using var downloader = CreateDownloader(handler);
        for (var i = 0; i < 3; i++) await AddItemAsync(LongArticle());

        var queued = await downloader.QueuePendingAsync();

        Assert.Equal(3, queued);
    }

    [Fact]
    public async Task An_item_already_queued_is_not_queued_twice()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, LongArticle());
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync(LongArticle());

        var first = downloader.TryQueue(id);
        var second = downloader.TryQueue(id);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task Every_queued_item_reaches_completion()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, LongArticle());
        await using var downloader = CreateDownloader(handler);
        var ids = new List<long>();
        for (var i = 0; i < 10; i++) ids.Add(await AddItemAsync(LongArticle()));

        var completed = new List<long>();
        var done = new TaskCompletionSource();
        downloader.Completed += id =>
        {
            lock (completed)
            {
                completed.Add(id);
                if (completed.Count == 10) done.TrySetResult();
            }
        };

        foreach (var id in ids) downloader.TryQueue(id);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(10, completed.Count);
    }

    [Fact]
    public async Task Downloaded_content_becomes_searchable()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, LongArticle());
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync(LongArticle());

        await downloader.DownloadNowAsync(id);

        var results = await new SearchRepository(_db).SearchAsync("Converted", 10);
        Assert.Single(results);
        Assert.Equal(id, results[0].Item.Id);
    }

    // --- Article image capture (Task 8b) ---
    //
    // ArticleFetcher already downloads the article page for full-text
    // extraction; the OpenGraph image is in that same HTML, so no second
    // fetch is needed to populate FeedItem.ImageUrl.

    [Fact]
    public async Task An_item_downloaded_via_the_extracted_path_captures_its_open_graph_image()
    {
        var articleHtml =
            "<html><head><meta property=\"og:image\" content=\"https://cdn.example.com/card.jpg\">" +
            "</head><body>" + LongArticle() + "</body></html>";
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, articleHtml, mediaType: "text/html");
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync("<p>Short teaser.</p>");

        await downloader.DownloadNowAsync(id);

        Assert.Single(handler.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(ContentSource.Extracted, item!.ContentSource);
        Assert.Equal("https://cdn.example.com/card.jpg", item.ImageUrl);
    }

    [Fact]
    public async Task An_item_downloaded_via_the_summary_path_leaves_the_image_null()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html>full page</html>");
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync(LongArticle());

        await downloader.DownloadNowAsync(id);

        Assert.Empty(handler.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(ContentSource.Feed, item!.ContentSource);
        Assert.Null(item.ImageUrl);
    }

    // --- Timeout handling ---
    //
    // Mostlylucid.Ephemeral 3.0.0's EphemeralWorkCoordinator does not actually
    // enforce the maxBodyDuration it is given: BodyDurationGuard.RunBoundedAsync
    // races a timer against the body's own task, but hands the body the
    // coordinator's own long-lived shutdown token, never a per-body timeout
    // token. When the timer fires it throws BodyDurationExceededException and
    // orphans the still-running body: nothing is cancelled, and a body stuck on
    // a hung HTTP call keeps running detached forever. OfflineDownloader must
    // therefore enforce MaxArticleFetchDuration itself, the same way
    // FeedRefreshService enforces MaxFeedFetchDuration itself. These tests use
    // a tiny duration (not the real 180s) plus StubHttpHandler.Blocking(), which
    // hangs until its own request's cancellation token fires, to prove that
    // enforcement without an actual 180-second wait.

    [Fact]
    public async Task A_hung_article_fetch_is_cancelled_at_the_duration_bound_and_marked_failed()
    {
        var handler = StubHttpHandler.Blocking();
        await using var downloader =
            CreateDownloader(handler, maxFetchDuration: TimeSpan.FromMilliseconds(50));
        var id = await AddItemAsync("<p>Short teaser.</p>");

        // Bounded, like its sibling test below: if the timeout guard were
        // ever regressed away, this must fail promptly rather than hang
        // until CI's outer job timeout.
        await downloader.DownloadNowAsync(id).WaitAsync(TimeSpan.FromSeconds(10));

        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Failed, item!.OfflineState);
        Assert.NotNull(item.OfflineError);
    }

    [Fact]
    public async Task A_hung_article_fetch_still_raises_Completed_for_a_queued_item()
    {
        var handler = StubHttpHandler.Blocking();
        await using var downloader =
            CreateDownloader(handler, maxFetchDuration: TimeSpan.FromMilliseconds(50));
        var id = await AddItemAsync("<p>Short teaser.</p>");

        var completed = new TaskCompletionSource<long>();
        downloader.Completed += completedId => completed.TrySetResult(completedId);

        Assert.True(downloader.TryQueue(id));
        var completedItemId = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(id, completedItemId);

        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Failed, item!.OfflineState);

        // The item was released once the timeout was recorded, not left
        // permanently in flight.
        Assert.True(downloader.TryQueue(id));
    }

    // --- Article-page validation ---
    //
    // Whatever ArticleFetcher returns is handed straight to the markdown
    // converter and, on success, overwrites the item's content and marks it
    // Downloaded and Extracted. There is no secondary "does this look like
    // an article" check downstream, so ArticleFetcher's own gates are the
    // only thing standing between a login wall, captcha page, soft 404 or
    // CSV export and it being silently stored as the article.

    [Fact]
    public async Task A_text_plain_response_is_rejected_and_the_summary_survives()
    {
        // No explicit mediaType: StubHttpHandler.Returning defaults to
        // StringContent's own "text/plain", the same shape a login wall or
        // a misconfigured server without a proper Content-Type would take.
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, LongArticle());
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync("<p>Short teaser.</p>");

        await downloader.DownloadNowAsync(id);

        Assert.Single(handler.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Failed, item!.OfflineState);
        Assert.NotNull(item.OfflineError);
        Assert.Equal("<p>Short teaser.</p>", item.Summary);
        Assert.Null(item.ContentMarkdown);
    }

    [Fact]
    public async Task A_chunked_response_over_the_size_cap_is_rejected_not_buffered()
    {
        // No Content-Length header at all - the shape a chunked response
        // takes - so the fast Content-Length pre-check in ArticleFetcher
        // cannot fire; only the streaming bound can reject this.
        var handler = StubHttpHandler.ReturningUnboundedLength(9 * 1024 * 1024);
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync("<p>Short teaser.</p>");

        await downloader.DownloadNowAsync(id).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(handler.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Failed, item!.OfflineState);
        Assert.NotNull(item.OfflineError);
        Assert.Equal("<p>Short teaser.</p>", item.Summary);
    }

    [Fact]
    public async Task A_markdown_article_is_stored_without_going_through_the_html_converter()
    {
        const string source = "# The author's own title\n\nProse as it was written.\n";
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, source, mediaType: "text/markdown");
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync("<p>A stub.</p>");

        await downloader.DownloadNowAsync(id);

        Assert.Empty(_converter.Converted);
        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Downloaded, item!.OfflineState);
        Assert.Equal(ContentSource.Extracted, item.ContentSource);
        Assert.Equal(source, item.ContentMarkdown);
    }

    [Fact]
    public async Task An_html_article_still_goes_through_the_converter_exactly_as_before()
    {
        const string page = "<html><body><p>Rendered prose.</p></body></html>";
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, page, mediaType: "text/html");
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync("<p>A stub.</p>");

        await downloader.DownloadNowAsync(id);

        Assert.Equal(page, Assert.Single(_converter.Converted));
        var item = await _items.GetAsync(id);
        Assert.Equal(ContentSource.Extracted, item!.ContentSource);
    }
}
