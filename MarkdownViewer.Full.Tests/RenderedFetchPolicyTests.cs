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
        Assert.True(RenderedFetchPolicy.ShouldRetry(
            firstPassHtml: "<html><body><div id=\"__next\">{}</div><script>window.__NEXT_DATA__={}</script></body></html>",
            firstPassMarkdown: "# H\n\nsome content here"));
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
