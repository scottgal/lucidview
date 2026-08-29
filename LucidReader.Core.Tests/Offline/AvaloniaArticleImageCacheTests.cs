using LucidReader.Core.Model;
using LucidReader.Services;
using Xunit;

namespace LucidReader.Core.Tests.Offline;

/// <summary>
/// A fake <see cref="IRemoteImageFetcher"/> that records every URL it was
/// asked to fetch and returns a scripted result, so the rewrite logic in
/// <see cref="AvaloniaArticleImageCache"/> - regex matching, the scheme
/// allowlist, the CacheImages gate, and the MaxImageBytes limit - can be
/// tested without Avalonia and without any real network or disk IO.
/// </summary>
internal sealed class FakeRemoteImageFetcher : IRemoteImageFetcher
{
    private readonly Dictionary<string, CachedImage?> _results;

    public FakeRemoteImageFetcher(Dictionary<string, CachedImage?> results) => _results = results;

    public List<string> Requested { get; } = [];

    public Task<CachedImage?> FetchAsync(string absoluteUrl, CancellationToken ct)
    {
        Requested.Add(absoluteUrl);
        return Task.FromResult(_results.TryGetValue(absoluteUrl, out var result) ? result : null);
    }
}

public class AvaloniaArticleImageCacheTests
{
    private static readonly Uri BaseUri = new("https://example.com/article");

    private static AvaloniaArticleImageCache CreateCache(
        Dictionary<string, CachedImage?> results,
        out FakeRemoteImageFetcher fetcher,
        ReaderSettings? settings = null)
    {
        fetcher = new FakeRemoteImageFetcher(results);
        var effectiveSettings = settings ?? ReaderSettings.Defaults;
        return new AvaloniaArticleImageCache(fetcher, () => effectiveSettings);
    }

    [Fact]
    public async Task A_fetched_markdown_image_is_rewritten_to_its_local_path()
    {
        const string url = "https://cdn.example/pic.png";
        var cache = CreateCache(
            new Dictionary<string, CachedImage?> { [url] = new CachedImage("/tmp/pic.png", 1024) },
            out var fetcher);

        var result = await cache.RewriteAsync($"# Title\n\n![alt text]({url})", BaseUri);

        Assert.Contains("![alt text](/tmp/pic.png)", result);
        Assert.Equal([url], fetcher.Requested);
    }

    [Fact]
    public async Task An_unfetchable_image_keeps_its_remote_url()
    {
        const string url = "https://cdn.example/missing.png";
        var cache = CreateCache(
            new Dictionary<string, CachedImage?> { [url] = null },
            out _);

        var markdown = $"![alt]({url})";
        var result = await cache.RewriteAsync(markdown, BaseUri);

        Assert.Equal(markdown, result);
    }

    [Fact]
    public async Task An_oversized_image_keeps_its_remote_url()
    {
        const string url = "https://cdn.example/huge.png";
        var settings = ReaderSettings.Defaults with { MaxImageBytes = 1000 };
        var cache = CreateCache(
            new Dictionary<string, CachedImage?> { [url] = new CachedImage("/tmp/huge.png", 1001) },
            out _,
            settings);

        var markdown = $"![alt]({url})";
        var result = await cache.RewriteAsync(markdown, BaseUri);

        Assert.Equal(markdown, result);
    }

    [Fact]
    public async Task An_image_at_exactly_the_limit_is_kept()
    {
        const string url = "https://cdn.example/exact.png";
        var settings = ReaderSettings.Defaults with { MaxImageBytes = 1000 };
        var cache = CreateCache(
            new Dictionary<string, CachedImage?> { [url] = new CachedImage("/tmp/exact.png", 1000) },
            out _,
            settings);

        var result = await cache.RewriteAsync($"![alt]({url})", BaseUri);

        Assert.Contains("/tmp/exact.png", result);
    }

    [Fact]
    public async Task CacheImages_disabled_skips_rewriting_entirely()
    {
        const string url = "https://cdn.example/pic.png";
        var settings = ReaderSettings.Defaults with { CacheImages = false };
        var cache = CreateCache(
            new Dictionary<string, CachedImage?> { [url] = new CachedImage("/tmp/pic.png", 10) },
            out var fetcher,
            settings);

        var markdown = $"![alt]({url})";
        var result = await cache.RewriteAsync(markdown, BaseUri);

        Assert.Equal(markdown, result);
        Assert.Empty(fetcher.Requested);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("data:image/png;base64,AAAA")]
    public async Task A_disallowed_scheme_is_never_sent_to_the_fetcher(string url)
    {
        var cache = CreateCache(new Dictionary<string, CachedImage?>(), out var fetcher);

        var markdown = $"![alt]({url})";
        var result = await cache.RewriteAsync(markdown, BaseUri);

        Assert.Equal(markdown, result);
        Assert.Empty(fetcher.Requested);
    }

    [Fact]
    public async Task A_raw_html_img_tag_embedded_in_markdown_is_rewritten()
    {
        const string url = "https://cdn.example/table-pic.png";
        var cache = CreateCache(
            new Dictionary<string, CachedImage?> { [url] = new CachedImage("/tmp/table-pic.png", 10) },
            out var fetcher);

        var markdown = $"""
            # Title

            <table><tr><td><img src="{url}" alt="in a table"></td></tr></table>
            """;

        var result = await cache.RewriteAsync(markdown, BaseUri);

        Assert.Contains("src=\"/tmp/table-pic.png\"", result);
        Assert.Equal([url], fetcher.Requested);
    }

    [Fact]
    public async Task A_raw_html_img_tag_with_single_quoted_src_is_rewritten()
    {
        const string url = "https://cdn.example/single-quote.png";
        var cache = CreateCache(
            new Dictionary<string, CachedImage?> { [url] = new CachedImage("/tmp/single-quote.png", 10) },
            out _);

        var markdown = $"<img src='{url}' alt='pic'>";
        var result = await cache.RewriteAsync(markdown, BaseUri);

        Assert.Contains("src='/tmp/single-quote.png'", result);
    }

    [Fact]
    public async Task A_relative_image_url_is_resolved_against_the_base_uri()
    {
        var cache = CreateCache(
            new Dictionary<string, CachedImage?> { ["https://example.com/images/pic.png"] = new CachedImage("/tmp/pic.png", 10) },
            out var fetcher);

        var result = await cache.RewriteAsync("![alt](/images/pic.png)", BaseUri);

        Assert.Contains("/tmp/pic.png", result);
        Assert.Equal(["https://example.com/images/pic.png"], fetcher.Requested);
    }
}
