using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Full.Tests;

public class RenderedFetchPolicyTests
{
    [Fact]
    public void ShouldRetry_True_WhenMarkdownIsTiny()
    {
        Assert.True(RenderedFetchPolicy.ShouldRetry(
            firstPassHtml: "<html><body>blank</body></html>",
            firstPassMarkdown: ""));
    }

    [Fact]
    public void ShouldRetry_True_WhenSpaMarkerPresent()
    {
        // 220 chars of well-formed prose — passes the MinMarkdownLength check,
        // so only the SPA-marker branch can cause ShouldRetry to return true.
        var md = string.Concat(Enumerable.Repeat("This sentence is real content. ", 8));
        Assert.True(md.Length > 200);  // sanity: ensures the SPA branch is the gate
        Assert.True(RenderedFetchPolicy.ShouldRetry(
            firstPassHtml: "<html><body><div id=\"__next\">{}</div><script>window.__NEXT_DATA__={}</script></body></html>",
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
