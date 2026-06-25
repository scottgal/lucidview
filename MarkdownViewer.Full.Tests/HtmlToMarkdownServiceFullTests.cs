using System.Threading;
using System.Threading.Tasks;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Full.Tests;

public class HtmlToMarkdownServiceFullTests
{
    [Fact]
    public async Task ConvertAsync_PlainHtml_ProducesNonEmptyMarkdown()
    {
        IHtmlToMarkdownService svc = new HtmlToMarkdownServiceFull();

        // StyloExtract's heuristic segmenter requires realistic web-page structure
        // (semantic landmarks + a sourceUri) to produce non-empty blocks, and the
        // RagFull renderer only emits MainContent blocks whose TextLength exceeds an
        // internal threshold (~100 chars). A bare <h1>+<p> with a few words yields
        // 0 rendered output even when the block is classified as MainContent.
        var html = """
            <!doctype html>
            <html lang="en">
            <head><title>Title</title></head>
            <body>
            <header><nav><a href="/">Home</a></nav></header>
            <main>
              <article>
                <h1>Title</h1>
                <p>Body paragraph. This is the first paragraph of the article with enough content to satisfy the RagFull renderer minimum text-length threshold.</p>
                <p>A second paragraph adds more substance so the block is reliably emitted across StyloExtract versions.</p>
              </article>
            </main>
            <footer><p>Footer</p></footer>
            </body>
            </html>
            """;

        var md = await svc.ConvertAsync(html, sourceUri: new Uri("https://example.com/post"), CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(md), "Markdown should not be empty.");
        Assert.Contains("Title", md);
        Assert.Contains("Body paragraph", md);
    }
}
