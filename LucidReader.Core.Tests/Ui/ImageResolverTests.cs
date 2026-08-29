using LucidReader.Core.Model;
using LucidReader.Core.Tests.Offline;
using LucidReader.Services;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// Reuses <see cref="FakeRemoteImageFetcher"/> from the
/// AvaloniaArticleImageCache tests: same fake-fetcher shape, same "record
/// what was requested" assertion style, just exercising ImageResolver's own
/// thin allowlist/settings gate instead of the markdown rewrite logic.
/// </summary>
public class ImageResolverTests
{
    private static ImageResolver CreateResolver(
        Dictionary<string, CachedImage?> results,
        out FakeRemoteImageFetcher fetcher,
        ReaderSettings? settings = null)
    {
        fetcher = new FakeRemoteImageFetcher(results);
        var effectiveSettings = settings ?? ReaderSettings.Defaults;
        return new ImageResolver(fetcher, () => effectiveSettings);
    }

    [Fact]
    public async Task A_valid_http_url_resolves_to_the_fetchers_local_path()
    {
        const string url = "https://example.com/favicon.ico";
        var resolver = CreateResolver(
            new Dictionary<string, CachedImage?> { [url] = new CachedImage("/tmp/favicon.ico", 512) },
            out var fetcher);

        var result = await resolver.ResolveAsync(url);

        Assert.Equal("/tmp/favicon.ico", result);
        Assert.Equal([url], fetcher.Requested);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_null_or_empty_url_returns_null(string? url)
    {
        var resolver = CreateResolver(new Dictionary<string, CachedImage?>(), out var fetcher);

        var result = await resolver.ResolveAsync(url);

        Assert.Null(result);
        Assert.Empty(fetcher.Requested);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("file:///etc/passwd")]
    public async Task A_disallowed_scheme_is_refused_without_reaching_the_fetcher(string url)
    {
        var resolver = CreateResolver(new Dictionary<string, CachedImage?>(), out var fetcher);

        var result = await resolver.ResolveAsync(url);

        Assert.Null(result);
        Assert.Empty(fetcher.Requested);
    }

    [Fact]
    public async Task A_fetch_failure_returns_null_rather_than_throwing()
    {
        const string url = "https://example.com/missing.png";
        var resolver = CreateResolver(
            new Dictionary<string, CachedImage?> { [url] = null },
            out var fetcher);

        var result = await resolver.ResolveAsync(url);

        Assert.Null(result);
        Assert.Equal([url], fetcher.Requested);
    }

    [Fact]
    public async Task CacheImages_disabled_short_circuits_before_any_fetch()
    {
        const string url = "https://example.com/favicon.ico";
        var settings = ReaderSettings.Defaults with { CacheImages = false };
        var resolver = CreateResolver(
            new Dictionary<string, CachedImage?> { [url] = new CachedImage("/tmp/favicon.ico", 512) },
            out var fetcher,
            settings);

        var result = await resolver.ResolveAsync(url);

        Assert.Null(result);
        Assert.Empty(fetcher.Requested);
    }
}
