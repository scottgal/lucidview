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

        var fetched = await fetcher.FetchArticleAsync("https://example.com/article");

        Assert.NotNull(fetched);
        Assert.Contains('é', fetched.Body); // 'é' -- 0xE9 in windows-1252
        Assert.DoesNotContain('�', fetched.Body); // no replacement characters
    }

    // --- Markdown source in preference to converted HTML ---
    //
    // A site that publishes the markdown its pages were written from can hand
    // that over instead, and using it directly beats converting the rendered
    // HTML back. Two generic mechanisms, no per-site knowledge: an Accept
    // header that lists markdown below HTML, and a markdown alternate link.

    [Fact]
    public async Task Html_is_still_asked_for_ahead_of_markdown()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<html><body>page</body></html>", mediaType: "text/html");
        var fetcher = new ArticleFetcher(handler.CreateClient());

        await fetcher.FetchArticleAsync("https://example.com/article");

        // Joined back up because HttpHeaders splits a comma-separated value
        // into one entry per media range on the way in.
        var accept = string.Join(",", Assert.Single(handler.Requests).Headers.GetValues("Accept"));
        Assert.Contains("text/markdown", accept, StringComparison.Ordinal);
        Assert.Contains("text/html,", accept, StringComparison.Ordinal);
        // Markdown carries an explicit q-value and HTML does not, which is
        // what keeps HTML the default for every site that serves both.
        // Normalised because HttpHeaders re-spaces the parameters on parse.
        Assert.Contains("text/markdown; q=0.8", accept, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_markdown_response_is_used_as_written()
    {
        const string source = "# A title\n\nSome *emphasised* prose.\n";
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, source, mediaType: "text/markdown");
        var fetcher = new ArticleFetcher(handler.CreateClient());

        var fetched = await fetcher.FetchArticleAsync("https://example.com/article");

        Assert.NotNull(fetched);
        Assert.Equal(ArticleBodyKind.Markdown, fetched.Kind);
        Assert.Equal(source, fetched.Body);
    }

    [Fact]
    public async Task An_x_markdown_response_counts_as_markdown_too()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "# Title\n", mediaType: "text/x-markdown");
        var fetcher = new ArticleFetcher(handler.CreateClient());

        var fetched = await fetcher.FetchArticleAsync("https://example.com/article");

        Assert.Equal(ArticleBodyKind.Markdown, fetched!.Kind);
    }

    [Fact]
    public async Task An_html_response_is_reported_as_html_so_the_caller_still_converts_it()
    {
        const string page = "<html><body><p>Prose.</p></body></html>";
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, page, mediaType: "text/html");
        var fetcher = new ArticleFetcher(handler.CreateClient());

        var fetched = await fetcher.FetchArticleAsync("https://example.com/article");

        Assert.NotNull(fetched);
        Assert.Equal(ArticleBodyKind.Html, fetched.Kind);
        Assert.Equal(page, fetched.Body);
    }

    [Fact]
    public async Task A_markdown_alternate_link_is_followed_and_its_body_used()
    {
        const string source = "# From the source\n\nThe author's own text.\n";
        var handler = new StubHttpHandler(request =>
            request.RequestUri!.AbsolutePath == "/article.md"
                ? StubHttpHandler.Response(source, "text/markdown")
                : StubHttpHandler.Response(
                    """
                    <html><head>
                      <link rel="alternate" type="text/markdown" href="/article.md">
                    </head><body><p>Rendered.</p></body></html>
                    """,
                    "text/html"));
        var fetcher = new ArticleFetcher(handler.CreateClient());

        var fetched = await fetcher.FetchArticleAsync("https://example.com/article");

        Assert.NotNull(fetched);
        Assert.Equal(ArticleBodyKind.Markdown, fetched.Kind);
        Assert.Equal(source, fetched.Body);
    }

    [Fact]
    public async Task A_markdown_alternate_that_answers_with_html_leaves_the_page_we_already_have()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Response(
            """
            <html><head>
              <link rel="alternate" type="text/markdown" href="/article.md">
            </head><body><p>Rendered.</p></body></html>
            """,
            "text/html"));
        var fetcher = new ArticleFetcher(handler.CreateClient());

        var fetched = await fetcher.FetchArticleAsync("https://example.com/article");

        Assert.Equal(ArticleBodyKind.Html, fetched!.Kind);
    }

    [Fact]
    public async Task A_markdown_alternate_pointing_at_a_private_address_is_not_fetched()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Response(
            """
            <html><head>
              <link rel="alternate" type="text/markdown" href="http://169.254.169.254/latest/meta-data">
            </head><body><p>Rendered.</p></body></html>
            """,
            "text/html"));
        var fetcher = new ArticleFetcher(handler.CreateClient());

        var fetched = await fetcher.FetchArticleAsync("https://example.com/article");

        Assert.Equal(ArticleBodyKind.Html, fetched!.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_response_that_is_neither_markdown_nor_html_nor_xml_is_still_refused()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "a,b,c\n1,2,3\n", mediaType: "text/csv");
        var fetcher = new ArticleFetcher(handler.CreateClient());

        Assert.Null(await fetcher.FetchArticleAsync("https://example.com/article"));
    }
}
