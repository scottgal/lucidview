using System.Net;
using LucidReader.Core.Offline;
using LucidReader.Core.Tests.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Offline;

/// <summary>
/// The URL here is item.Link, straight out of feed XML, and auto-download is
/// on by default with every new item marked pending, so this fetch happens
/// with no user action at all. A scheme check alone let a subscribed feed
/// aim it anywhere on the machine or the local network.
/// </summary>
public class ArticleFetcherPolicyTests
{
    [Theory]
    [InlineData("http://127.0.0.1:9200/_cluster/settings")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://192.168.1.1/setup.cgi?reboot=1")]
    [InlineData("http://localhost./admin")]
    [InlineData("http://[64:ff9b::7f00:1]/admin")]
    [InlineData("http://user:pass@example.com/article")]
    public async Task An_item_link_naming_a_refused_address_is_never_requested(string url)
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<html><body>internal</body></html>", mediaType: "text/html");
        var fetcher = new ArticleFetcher(handler.CreateClient());

        Assert.Null(await fetcher.FetchArticleAsync(url));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_ordinary_article_link_is_still_fetched()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<html><body>article</body></html>", mediaType: "text/html");
        var fetcher = new ArticleFetcher(handler.CreateClient());

        var fetched = await fetcher.FetchArticleAsync("https://example.com/article");

        Assert.NotNull(fetched);
        Assert.Single(handler.Requests);
    }
}
