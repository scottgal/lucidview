using MarkdownViewer.Services;

namespace MarkdownViewer.Tests;

public class HtmlToMarkdownServiceTests
{
    private readonly HtmlToMarkdownService _service = new();

    [Fact]
    public void MermaidPre_GetsLanguageTaggedFence()
    {
        const string html = """
            <!doctype html><html><body>
            <h1>Title</h1>
            <p>Intro</p>
            <pre class="mermaid">flowchart LR
                A --> B
            </pre>
            <p>Outro</p>
            </body></html>
            """;

        var md = _service.Convert(html, new Uri("https://example.com/post"));

        Assert.Contains("```mermaid", md);
        Assert.Contains("flowchart LR", md);
        Assert.Contains("A --> B", md);
    }

    [Fact]
    public void HtmxOnlyAnchor_GetsHrefFromHxGet()
    {
        const string html = """
            <!doctype html><html><body>
            <article>
                <h1>Post</h1>
                <p>Click <a hx-get="/blog/foo" hx-target="#x">here</a> for foo.</p>
            </article>
            </body></html>
            """;

        var md = _service.Convert(html, new Uri("https://example.com/"));

        Assert.Contains("[here](/blog/foo)", md);
    }

    [Fact]
    public void HrefAlreadyPresent_NotOverwrittenByHxGet()
    {
        const string html = """
            <!doctype html><html><body>
            <article>
                <h1>Post</h1>
                <p><a href="/real" hx-get="/htmx-only">link</a></p>
            </article>
            </body></html>
            """;

        var md = _service.Convert(html, new Uri("https://example.com/"));

        Assert.Contains("[link](/real)", md);
        Assert.DoesNotContain("/htmx-only", md);
    }
}
