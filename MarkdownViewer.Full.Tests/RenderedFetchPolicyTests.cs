using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Full.Tests;

public class RenderedFetchPolicyTests
{
    [Fact]
    public void ShouldRetry_False_WhenEmptyButNotASpa()
    {
        // Policy change: empty markdown is NOT enough on its own. The HTML
        // must also be a recognisable SPA shell. Without that, Playwright
        // can only expose hidden / debug DOM that corrupts the result
        // (MS Learn YAML, Notion __PROPS__, etc.), so we keep the static
        // extraction and don't risk it.
        Assert.False(RenderedFetchPolicy.ShouldRetry(
            firstPassHtml: "<html><body>blank</body></html>",
            firstPassMarkdown: ""));
    }

    [Fact]
    public void ShouldRetry_True_WhenSpaMarkerPresent_AndExtractionEmpty()
    {
        // SPA framework marker + empty extraction = the static body is a
        // hydration shell. Playwright is the right move.
        Assert.True(RenderedFetchPolicy.ShouldRetry(
            firstPassHtml: "<html><body><div id=\"__next\">{}</div><script>window.__NEXT_DATA__={}</script></body></html>",
            firstPassMarkdown: ""));
    }

    [Fact]
    public void ShouldRetry_False_WhenSpaMarkerPresent_ButExtractionAlreadyHasContent()
    {
        // Some SPAs server-side render enough for the heuristic to work.
        // When the static extraction already produced substantial text,
        // don't bother with Playwright — the cost is high and the win
        // (DOM bloat exposing hidden state) is negative.
        var md = string.Concat(Enumerable.Repeat("Real article prose with substance. ", 8));
        Assert.False(RenderedFetchPolicy.ShouldRetry(
            firstPassHtml: "<html><body><div id=\"__next\">{}</div><script>window.__NEXT_DATA__={}</script></body>" + md + "</html>",
            firstPassMarkdown: md));
    }

    [Fact]
    public void ShouldRetry_False_OnFullArticle()
    {
        var md = string.Concat(Enumerable.Repeat("Paragraph of content. ", 50));
        Assert.False(RenderedFetchPolicy.ShouldRetry(
            firstPassHtml: "<html><body><article>...</article></body></html>",
            firstPassMarkdown: md));
    }
}
