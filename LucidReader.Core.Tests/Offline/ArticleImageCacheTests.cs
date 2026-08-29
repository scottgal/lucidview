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
/// Records what it was asked to rewrite and returns a marked-up result, so a
/// test can prove the downloader routed content through the cache without
/// depending on any real image fetching.
/// </summary>
internal sealed class RecordingImageCache : IArticleImageCache
{
    public List<string> Rewritten { get; } = [];
    public Uri? LastBaseUri { get; private set; }

    public Task<string> RewriteAsync(string markdown, Uri? baseUri, CancellationToken ct = default)
    {
        Rewritten.Add(markdown);
        LastBaseUri = baseUri;
        return Task.FromResult(markdown + "\n\n<!-- images cached -->");
    }
}

internal sealed class PassthroughConverter : IHtmlToMarkdownService
{
    public Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default) =>
        Task.FromResult("# Converted\n\n![pic](https://cdn.example/pic.png)");
}

public class ArticleImageCacheTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-08-29T12:00:00Z"));
    private readonly RecordingImageCache _cache = new();

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

    private OfflineDownloader CreateDownloader(IArticleImageCache? cache)
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html>page</html>", mediaType: "text/html");
        return new OfflineDownloader(
            _items, _feeds, new ArticleFetcher(handler.CreateClient()),
            new PassthroughConverter(), () => ReaderSettings.Defaults, _time,
            imageCache: cache);
    }

    private Task<long> AddPendingAsync() => _items.UpsertAsync(new FeedItem
    {
        FeedId = _feedId,
        Guid = "g1",
        Title = "An article",
        Link = "https://example.com/article",
        Summary = "<p>" + new string('x', 2000) + "</p>",
        FirstSeenUtc = _time.GetUtcNow(),
        OfflineState = OfflineState.Pending
    });

    [Fact]
    public async Task Downloaded_content_is_routed_through_the_image_cache()
    {
        await using var downloader = CreateDownloader(_cache);
        var id = await AddPendingAsync();

        await downloader.DownloadNowAsync(id);

        Assert.Single(_cache.Rewritten);
        var stored = await _items.GetAsync(id);
        Assert.Contains("images cached", stored!.ContentMarkdown);
    }

    [Fact]
    public async Task The_item_link_is_passed_as_the_base_uri_for_relative_images()
    {
        await using var downloader = CreateDownloader(_cache);
        var id = await AddPendingAsync();

        await downloader.DownloadNowAsync(id);

        Assert.Equal(new Uri("https://example.com/article"), _cache.LastBaseUri);
    }

    [Fact]
    public async Task No_cache_supplied_stores_the_markdown_unchanged()
    {
        await using var downloader = CreateDownloader(null);
        var id = await AddPendingAsync();

        await downloader.DownloadNowAsync(id);

        var stored = await _items.GetAsync(id);
        Assert.DoesNotContain("images cached", stored!.ContentMarkdown);
        Assert.Equal(OfflineState.Downloaded, stored.OfflineState);
    }

    [Fact]
    public async Task A_failing_image_cache_still_stores_the_article()
    {
        var failing = new ThrowingImageCache();
        await using var downloader = CreateDownloader(failing);
        var id = await AddPendingAsync();

        await downloader.DownloadNowAsync(id);

        var stored = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Downloaded, stored!.OfflineState);
        Assert.Contains("Converted", stored.ContentMarkdown);
    }

    private sealed class ThrowingImageCache : IArticleImageCache
    {
        public Task<string> RewriteAsync(string markdown, Uri? baseUri, CancellationToken ct = default) =>
            throw new IOException("disk full");
    }
}
