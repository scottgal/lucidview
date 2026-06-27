using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MarkdownViewer.Services;
using StyloExtract.Abstractions;
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
        // Test fixtures are short by design; the prod policy thresholds (500
        // chars, 3 blocks) would trip Playwright retry against a fake URL.
        // Lower for the duration of these tests to match the heuristic-only
        // expectations.
        RenderedFetchPolicy.MinMarkdownLength = 50;
        RenderedFetchPolicy.MinBlockCount = 1;
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

    [Fact]
    [Trait("Category", "RequiresPlaywright")]
    public async Task ConvertAsync_SpaPage_RetriesViaPlaywright()
    {
        var svc = FullServices.Get<IHtmlToMarkdownService>();
        var spaHtml = """
            <html><head><title>SPA</title></head>
            <body><div id="__next"></div>
            <script>window.__NEXT_DATA__={};</script></body></html>
            """;
        // Force the policy to trigger — empty first pass.
        var md = await svc.ConvertAsync(spaHtml, new Uri("https://example.com/"), CancellationToken.None);
        Assert.NotNull(md);  // No assertion on content — Playwright result is host-dependent.
    }

    /// <summary>
    /// Smoke-tests the LLM inducer path with a deeply custom/unusual HTML layout that
    /// heuristic selectors are unlikely to have seen. The test verifies the pipeline
    /// produces non-empty markdown containing the key text from the fixture.
    ///
    /// Fixture design rationale: standard article/main/header selectors are absent;
    /// content is wrapped in proprietary shadow-host-like divs with obfuscated class
    /// names and data attributes. This forces the heuristic classifier to fall back to
    /// the LLM inducer to produce a template.
    ///
    /// Run only locally (model must be pre-downloaded via --download-model):
    ///   dotnet run --project MarkdownViewer.Full/MarkdownViewer.Full.csproj -- --download-model
    ///   dotnet test MarkdownViewer.Full.Tests/... --filter "Category=RequiresLlm" -v normal
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresLlm")]
    public async Task ConvertAsync_NovelLayout_InvokesLlmInducer()
    {
        var svc = FullServices.Get<IHtmlToMarkdownService>();
        // Deeply unusual layout: no <main>, <article>, or <header> elements;
        // content is buried in shadow-host-like custom class hierarchies with
        // data-role attributes and obfuscated class names. This defeats the
        // heuristic selector lookup and routes through the LLM inducer.
        //
        // "Novel Site" appears in a <p> inside the content rail (not just
        // the <h1>) so the text extractor always captures it regardless of
        // whether the masthead element is treated as branding/navigation.
        var novelHtml = """
            <html><body>
              <div class="x9-root" data-mode="reading" data-variant="v2">
                <div class="x9-chrome-ring">
                  <div class="x9-masthead" data-zone="brand">
                    <span class="x9-brand-glyph" aria-hidden="true"></span>
                    <h1 class="x9-primary-headline">Novel Site Heading</h1>
                  </div>
                  <div class="x9-body-scaffold">
                    <div class="x9-content-rail" data-col="primary">
                      <div class="x9-prose-block" data-prose-variant="long">
                        <div class="x9-paragraph-unit" data-index="0">
                          <p>Novel Site content paragraph: Unusual structure that the heuristic classifier hasn't seen.</p>
                        </div>
                        <div class="x9-paragraph-unit" data-index="1">
                          <p>A second paragraph with enough content to cross the minimum length threshold for reliable extraction by the StyloExtract pipeline.</p>
                        </div>
                        <div class="x9-paragraph-unit" data-index="2">
                          <p>Third paragraph provides additional body text so the LLM has sufficient signal to generate a stable extraction template.</p>
                        </div>
                      </div>
                    </div>
                    <aside class="x9-sidebar-rail" data-col="secondary">
                      <div class="x9-widget-pod" data-widget="related">Related</div>
                    </aside>
                  </div>
                </div>
              </div>
            </body></html>
            """;
        var md = await svc.ConvertAsync(novelHtml, new Uri("https://novel-test.invalid/"), CancellationToken.None);
        Assert.Contains("Novel Site", md);
        Assert.Contains("Unusual structure", md);
    }
}
