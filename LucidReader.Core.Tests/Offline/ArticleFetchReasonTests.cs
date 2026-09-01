using System.Net;
using LucidReader.Core.Offline;
using LucidReader.Core.Tests.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Offline;

/// <summary>
/// Why an article could not be downloaded, which used to be unknowable.
///
/// Every failure returned null and the downloader recorded the same sentence
/// for all of them, "Could not fetch &lt;url&gt;". That reads as this app
/// failing, and the most common cause is not this app at all: a publisher
/// that answers 403 to anything without a browser User-Agent. Ars Technica
/// does exactly that, which put that message on eighteen of its twenty
/// articles while the feed itself was being read perfectly well.
/// </summary>
public class ArticleFetchReasonTests
{
    private static ArticleFetcher FetcherFor(HttpStatusCode status, string? mediaType = null) =>
        new(StubHttpHandler.Returning(status, body: "<html><body>x</body></html>",
            mediaType: mediaType ?? "text/html").CreateClient());

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_refusal_says_it_is_the_publisher_s_decision(HttpStatusCode status)
    {
        var attempt = await FetcherFor(status)
            .TryFetchArticleAsync("https://example.com/article");

        Assert.Null(attempt.Article);
        Assert.NotNull(attempt.Reason);

        // The words that matter: a user has to be able to tell that the
        // download was refused rather than broken.
        Assert.Contains("does not allow automated", attempt.Reason);
        Assert.Contains(((int)status).ToString(), attempt.Reason);
    }

    [Fact]
    public async Task Rate_limiting_says_it_is_temporary()
    {
        var attempt = await FetcherFor(HttpStatusCode.TooManyRequests)
            .TryFetchArticleAsync("https://example.com/article");

        Assert.Null(attempt.Article);
        Assert.Contains("rate limiting", attempt.Reason);

        // Separated from 403 on purpose: retrying later is the right response
        // to this one and pointless for a refusal.
        Assert.Contains("retried later", attempt.Reason);
    }

    [Fact]
    public async Task A_missing_page_says_it_is_gone()
    {
        var attempt = await FetcherFor(HttpStatusCode.NotFound)
            .TryFetchArticleAsync("https://example.com/article");

        Assert.Null(attempt.Article);
        Assert.Contains("gone", attempt.Reason);
    }

    [Fact]
    public async Task Another_status_is_reported_as_itself()
    {
        var attempt = await FetcherFor(HttpStatusCode.InternalServerError)
            .TryFetchArticleAsync("https://example.com/article");

        Assert.Null(attempt.Article);
        Assert.Contains("500", attempt.Reason);
    }

    [Fact]
    public async Task A_page_that_is_not_an_article_says_what_it_was()
    {
        var attempt = await FetcherFor(HttpStatusCode.OK, mediaType: "application/pdf")
            .TryFetchArticleAsync("https://example.com/article.pdf");

        Assert.Null(attempt.Article);
        Assert.Contains("application/pdf", attempt.Reason);
    }

    [Fact]
    public async Task An_address_the_policy_refuses_says_so()
    {
        var attempt = await FetcherFor(HttpStatusCode.OK)
            .TryFetchArticleAsync("http://127.0.0.1/admin");

        Assert.Null(attempt.Article);
        Assert.False(string.IsNullOrWhiteSpace(attempt.Reason));
    }

    [Fact]
    public async Task A_success_carries_no_reason()
    {
        var attempt = await FetcherFor(HttpStatusCode.OK)
            .TryFetchArticleAsync("https://example.com/article");

        Assert.NotNull(attempt.Article);
        Assert.Null(attempt.Reason);
    }
}
