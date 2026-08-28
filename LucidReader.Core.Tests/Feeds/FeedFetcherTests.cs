using System.Net;
using System.Text;
using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

public class FeedFetcherTests
{
    private const string Url = "https://example.com/feed.xml";

    [Fact]
    public async Task A_200_returns_the_body_and_the_validators()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<rss/>", etag: "\"abc\"",
            lastModified: "Thu, 27 Aug 2026 10:00:00 GMT");
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, null, null);

        var fetched = Assert.IsType<FeedFetchResult.Fetched>(result);
        Assert.Equal("<rss/>", fetched.Content);
        Assert.Equal("\"abc\"", fetched.ETag);
        Assert.Equal("Thu, 27 Aug 2026 10:00:00 GMT", fetched.LastModified);
    }

    [Fact]
    public async Task A_stored_etag_is_sent_as_if_none_match()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotModified);
        var fetcher = new FeedFetcher(handler.CreateClient());

        await fetcher.FetchAsync(Url, "\"abc\"", null);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("\"abc\"", request.Headers.IfNoneMatch.ToString());
    }

    [Fact]
    public async Task A_stored_last_modified_is_sent_as_if_modified_since()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotModified);
        var fetcher = new FeedFetcher(handler.CreateClient());

        await fetcher.FetchAsync(Url, null, "Thu, 27 Aug 2026 10:00:00 GMT");

        var request = Assert.Single(handler.Requests);
        Assert.NotNull(request.Headers.IfModifiedSince);
    }

    [Fact]
    public async Task A_304_returns_NotModified_and_no_body()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotModified);
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, "\"abc\"", null);

        Assert.IsType<FeedFetchResult.NotModified>(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Gone, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public async Task Error_statuses_are_classified_as_transient_or_permanent(
        HttpStatusCode status, bool expectedTransient)
    {
        var handler = StubHttpHandler.Returning(status);
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, null, null);

        var failed = Assert.IsType<FeedFetchResult.Failed>(result);
        Assert.Equal(expectedTransient, failed.IsTransient);
    }

    [Fact]
    public async Task A_network_exception_is_a_transient_failure_not_a_throw()
    {
        var handler = StubHttpHandler.Throwing(new HttpRequestException("connection refused"));
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, null, null);

        var failed = Assert.IsType<FeedFetchResult.Failed>(result);
        Assert.True(failed.IsTransient);
        Assert.Contains("connection refused", failed.Error);
    }

    [Fact]
    public async Task A_timeout_is_a_transient_failure()
    {
        var handler = StubHttpHandler.Throwing(new TaskCanceledException("timed out"));
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, null, null, CancellationToken.None);

        var failed = Assert.IsType<FeedFetchResult.Failed>(result);
        Assert.True(failed.IsTransient);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_rather_than_being_swallowed()
    {
        var handler = StubHttpHandler.Throwing(new TaskCanceledException("cancelled"));
        var fetcher = new FeedFetcher(handler.CreateClient());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fetcher.FetchAsync(Url, null, null, cts.Token));
    }

    [Fact]
    public async Task A_malformed_url_fails_permanently_rather_than_throwing()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<rss/>");
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync("not a url", null, null);

        var failed = Assert.IsType<FeedFetchResult.Failed>(result);
        Assert.False(failed.IsTransient);
    }

    [Fact]
    public async Task The_request_identifies_lucidREADER()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<rss/>");
        var fetcher = new FeedFetcher(handler.CreateClient());

        await fetcher.FetchAsync(Url, null, null);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("lucidREADER", request.Headers.UserAgent.ToString());
    }

    // --- Encoding ---
    //
    // ReadAsStringAsync() decodes using the Content-Type charset, falling
    // back to UTF-8 when there isn't one. Plenty of real feeds declare their
    // encoding only in the XML declaration and send no charset header at
    // all, so that fallback silently mojibakes non-ASCII titles. The parser
    // runs after this and can't recover from an already-wrong string, so
    // FeedFetcher has to get the bytes-to-string decode right itself.

    private static byte[] Windows1252FixtureBytes() =>
        File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Feeds", "rss2-windows-1252.xml"));

    [Fact]
    public async Task No_charset_header_falls_back_to_the_XML_declaration_encoding()
    {
        var handler = StubHttpHandler.ReturningBytes(Windows1252FixtureBytes(), charset: null);
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, null, null);

        var fetched = Assert.IsType<FeedFetchResult.Fetched>(result);
        Assert.Contains('é', fetched.Content); // 'é' -- 0xE9 in windows-1252
        Assert.DoesNotContain('�', fetched.Content); // no replacement characters
    }

    [Fact]
    public async Task A_charset_header_takes_precedence_over_the_XML_declaration()
    {
        // The body's XML declaration says windows-1252, but the HTTP header
        // says UTF-8. The header wins, so decoding windows-1252 bytes as
        // UTF-8 must produce replacement characters, not a clean 'é'.
        var handler = StubHttpHandler.ReturningBytes(Windows1252FixtureBytes(), charset: "utf-8");
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, null, null);

        var fetched = Assert.IsType<FeedFetchResult.Fetched>(result);
        Assert.Contains('�', fetched.Content);
    }

    [Fact]
    public async Task An_unknown_charset_name_falls_back_to_UTF8_without_throwing()
    {
        var utf8Body = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><rss><channel><title>ok</title></channel></rss>");
        var handler = StubHttpHandler.ReturningBytes(utf8Body, charset: "not-a-real-charset");
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, null, null);

        var fetched = Assert.IsType<FeedFetchResult.Fetched>(result);
        Assert.Contains("<title>ok</title>", fetched.Content);
    }

    [Fact]
    public async Task A_UTF8_feed_still_decodes_correctly()
    {
        const string body = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><rss><channel><title>café</title></channel></rss>";
        var handler = StubHttpHandler.ReturningBytes(Encoding.UTF8.GetBytes(body), charset: null);
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, null, null);

        var fetched = Assert.IsType<FeedFetchResult.Fetched>(result);
        Assert.Contains("café", fetched.Content);
    }
}
