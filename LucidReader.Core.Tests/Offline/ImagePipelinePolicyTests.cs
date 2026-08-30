using System.Text;
using LucidReader.Core.Model;
using LucidReader.Services;
using Xunit;

namespace LucidReader.Core.Tests.Offline;

/// <summary>
/// Image URLs come from article HTML, from og:image and twitter:image, and
/// from favicon links, all of them remote and none of them chosen by the
/// user. Both resolve paths fire during automatic offline download, before
/// anything has been opened, so both need the host rules and not just the
/// scheme allowlist.
/// </summary>
public class ImagePipelinePolicyTests
{
    private static readonly Uri BaseUri = new("https://example.com/article");

    [Theory]
    [InlineData("http://192.168.1.1/setup.cgi?reboot=1")]
    [InlineData("http://127.0.0.1:9200/_cluster/settings")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://169.254.169.254./latest/meta-data/")]
    [InlineData("http://[2002:7f00:1::]/pic.png")]
    public async Task An_article_image_on_a_refused_host_is_never_fetched(string url)
    {
        var fetcher = new FakeRemoteImageFetcher(new Dictionary<string, CachedImage?>());
        var cache = new AvaloniaArticleImageCache(fetcher, () => ReaderSettings.Defaults);

        var markdown = $"![img]({url})\n\n<img src=\"{url}\">";
        var result = await cache.RewriteAsync(markdown, BaseUri);

        Assert.Empty(fetcher.Requested);
        Assert.Contains(url, result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://192.168.1.1/setup.cgi?reboot=1")]
    [InlineData("http://127.0.0.1/favicon.ico")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    public async Task A_favicon_or_social_image_on_a_refused_host_is_never_fetched(string url)
    {
        var fetcher = new FakeRemoteImageFetcher(new Dictionary<string, CachedImage?>());
        var resolver = new ImageResolver(fetcher, () => ReaderSettings.Defaults);

        Assert.Null(await resolver.ResolveAsync(url));
        Assert.Empty(fetcher.Requested);
    }

    /// <summary>
    /// The markdown is publisher-controlled and the rewrite ran over every
    /// match it found, so one item could cost thousands of outbound requests.
    /// </summary>
    [Fact]
    public async Task The_number_of_images_one_article_may_fetch_is_capped()
    {
        var results = new Dictionary<string, CachedImage?>();
        var markdown = new StringBuilder();
        for (var i = 0; i < 500; i++)
        {
            var url = $"https://cdn.example/pic{i}.png";
            results[url] = new CachedImage($"/tmp/pic{i}.png", 100);
            markdown.Append("![x](").Append(url).Append(")\n\n");
        }

        var fetcher = new FakeRemoteImageFetcher(results);
        var cache = new AvaloniaArticleImageCache(fetcher, () => ReaderSettings.Defaults);

        await cache.RewriteAsync(markdown.ToString(), BaseUri);

        Assert.InRange(fetcher.Requested.Count, 1, 100);
    }

    [Fact]
    public async Task An_ordinary_public_image_is_still_fetched_and_rewritten()
    {
        const string url = "https://cdn.example/pic.png";
        var fetcher = new FakeRemoteImageFetcher(
            new Dictionary<string, CachedImage?> { [url] = new CachedImage("/tmp/pic.png", 100) });
        var cache = new AvaloniaArticleImageCache(fetcher, () => ReaderSettings.Defaults);

        var result = await cache.RewriteAsync($"![x]({url})", BaseUri);

        Assert.Contains("/tmp/pic.png", result, StringComparison.Ordinal);
        Assert.Equal([url], fetcher.Requested);
    }
}
