using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Full.Tests;

/// <summary>
/// Class fixture that sets LUCIDVIEW_STATE_DIR once before any tests in the class
/// run and tears it down once all tests finish. This ensures the single-init
/// AppPaths.LocalState and FullServices.Provider statics read the same temp path.
/// </summary>
public sealed class FullServicesFixture : IDisposable
{
    public string TempDir { get; } = Path.Combine(Path.GetTempPath(), $"lvfull-{Guid.NewGuid():N}");

    public FullServicesFixture()
    {
        Directory.CreateDirectory(TempDir);
        Environment.SetEnvironmentVariable("LUCIDVIEW_STATE_DIR", TempDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LUCIDVIEW_STATE_DIR", null);
        try { Directory.Delete(TempDir, recursive: true); } catch { }
    }
}

[Collection("FullServices")]
public class HtmlToMarkdownServiceFullTests : IClassFixture<FullServicesFixture>
{
    private readonly FullServicesFixture _fixture;

    public HtmlToMarkdownServiceFullTests(FullServicesFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConvertAsync_PlainHtml_ProducesNonEmptyMarkdown()
    {
        var svc = FullServices.Get<IHtmlToMarkdownService>();
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

        var md = await svc.ConvertAsync(html, new Uri("https://example.com/page"), CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(md));
        Assert.Contains("Title", md);
    }

    [Fact]
    public async Task ConvertAsync_PopulatesTemplateStore()
    {
        var storePath = Path.Combine(_fixture.TempDir, "styloextract-templates.db");

        // Capture pre-call size — the IClassFixture-shared store may already exist
        // from the prior test. The assertion below verifies THIS call grew it.
        long preCallLength = File.Exists(storePath) ? new FileInfo(storePath).Length : 0;

        var svc = FullServices.Get<IHtmlToMarkdownService>();
        var html = "<!doctype html><html><head><title>Distinct</title></head>"
                 + "<body><header><h1>Distinct H1</h1></header>"
                 + "<main><article><p>" + new string('x', 300) + "</p></article></main>"
                 + "<footer>f</footer></body></html>";

        await svc.ConvertAsync(html, new Uri("https://distinct-host-task4-fix.invalid/page"), CancellationToken.None);

        Assert.True(File.Exists(storePath), $"Template store should exist at {storePath}");
        long postCallLength = new FileInfo(storePath).Length;
        Assert.True(postCallLength > preCallLength,
            $"Template store size should grow after this extraction (pre={preCallLength}, post={postCallLength}).");
    }
}
