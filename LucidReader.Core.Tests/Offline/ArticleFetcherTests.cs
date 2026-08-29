using System.Net;
using LucidReader.Core.Offline;
using LucidReader.Core.Tests.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Offline;

public class ArticleFetcherTests
{
    // Regression test for the whole-branch review's finding that legacy
    // code-page registration (windows-1252 and friends) was a hidden static
    // side effect of FeedFetcher's own static constructor, which
    // ArticleFetcher.GetEncoding silently depended on. ArticleFetcher's only
    // reference to FeedFetcher was FeedFetcher.UserAgentString, a const the
    // compiler inlines - which does NOT trigger FeedFetcher's type
    // initializer - so the registration only ever ran in practice because a
    // real composition happened to construct a FeedFetcher first. This test
    // constructs only ArticleFetcher, never FeedFetcher, and proves decoding
    // still works: the fix moved the registration to a [ModuleInitializer] in
    // ModuleInitialization.cs, which runs unconditionally for the whole
    // assembly regardless of which type a caller touches first.
    [Fact]
    public async Task A_non_utf8_article_page_decodes_correctly_without_constructing_FeedFetcher()
    {
        var windows1252Bytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Feeds", "rss2-windows-1252.xml"));
        var handler = StubHttpHandler.ReturningBytes(
            windows1252Bytes, mediaType: "text/html", charset: "windows-1252");
        var fetcher = new ArticleFetcher(handler.CreateClient());

        var html = await fetcher.FetchHtmlAsync("https://example.com/article");

        Assert.NotNull(html);
        Assert.Contains('é', html); // 'é' -- 0xE9 in windows-1252
        Assert.DoesNotContain('�', html); // no replacement characters
    }
}
