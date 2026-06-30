using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MarkdownViewer.Lab.Services.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StyloExtract.Abstractions;
using StyloExtract.Core;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Ingestion;

/// <summary>
/// Tests for <see cref="HtmlIngestor"/>.
/// All HTML-ingestion tests exercise the real <see cref="ILayoutExtractor"/>
/// via <c>AddStyloExtract()</c> — the regex fallback was removed.
/// </summary>
public class HtmlIngestorTests : IAsyncDisposable
{
    private readonly string _storeDir =
        Path.Combine(Path.GetTempPath(), $"html-ingestor-store-{Guid.NewGuid():N}");

    private readonly ServiceProvider _sp;
    private readonly ILayoutExtractor _extractor;

    public HtmlIngestorTests()
    {
        Directory.CreateDirectory(_storeDir);

        var services = new ServiceCollection();
        services.AddStyloExtract(o =>
        {
            o.StorePath      = Path.Combine(_storeDir, "templates.db");
            o.DefaultProfile = ExtractionProfile.RagFull;
        });

        _sp        = services.BuildServiceProvider();
        _extractor = _sp.GetRequiredService<ILayoutExtractor>();
    }

    // ── CanHandle ─────────────────────────────────────────────────────────

    [Fact]
    public void HtmlIngestor_CanHandle_HtmlExtension()
    {
        var sut = new HtmlIngestor(_extractor, NullLogger<HtmlIngestor>.Instance);
        sut.CanHandle("article.html").Should().BeTrue();
        sut.CanHandle("page.htm").Should().BeTrue();
    }

    [Fact]
    public void HtmlIngestor_CanHandle_HttpsUrl()
    {
        var sut = new HtmlIngestor(_extractor, NullLogger<HtmlIngestor>.Instance);
        sut.CanHandle("https://example.com/article").Should().BeTrue();
        sut.CanHandle("http://example.com/page").Should().BeTrue();
    }

    [Fact]
    public void HtmlIngestor_CanHandle_ReturnsFalse_ForMarkdown()
    {
        var sut = new HtmlIngestor(_extractor, NullLogger<HtmlIngestor>.Instance);
        sut.CanHandle("readme.md").Should().BeFalse();
        sut.CanHandle("file.txt").Should().BeFalse();
    }

    // ── IngestAsync: local HTML file via StyloExtract ─────────────────────

    /// <summary>
    /// Smoke test: exercises the real <see cref="ILayoutExtractor.ExtractAsync"/>
    /// end-to-end on a fixture HTML string. Confirms segments are produced.
    /// </summary>
    [Fact]
    public async Task HtmlIngestor_IngestAsync_LocalFile_ReturnsSegments_ViaStyloExtract()
    {
        var dir = Path.Combine(_storeDir, $"ingest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "sample.html");
        await File.WriteAllTextAsync(path, SampleHtml);

        var sut = new HtmlIngestor(_extractor, NullLogger<HtmlIngestor>.Instance);

        // Act — real ILayoutExtractor call, no mocks.
        var doc = await sut.IngestAsync(path, CancellationToken.None);

        doc.Should().NotBeNull();
        doc.Path.Should().Be(path);
        doc.MimeType.Should().Be("text/html");
        doc.Segments.Should().NotBeEmpty(
            "StyloExtract must produce at least one segment from the fixture HTML");
        doc.Segments.Should().AllSatisfy(s =>
            s.Text.Should().NotBeNullOrWhiteSpace("every segment must carry content"));
    }

    [Fact]
    public async Task HtmlIngestor_IngestAsync_LocalFile_TitleExtractedFromH1()
    {
        var dir = Path.Combine(_storeDir, $"title-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "article.html");
        await File.WriteAllTextAsync(path, SampleHtml);

        var sut = new HtmlIngestor(_extractor, NullLogger<HtmlIngestor>.Instance);
        var doc = await sut.IngestAsync(path, CancellationToken.None);

        // Title should come from the <title> element or the H1.
        doc.Title.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task HtmlIngestor_IngestAsync_LocalFile_SegmentTextIsNonTrivial()
    {
        var dir = Path.Combine(_storeDir, $"seg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "content.html");
        await File.WriteAllTextAsync(path, SampleHtml);

        var sut = new HtmlIngestor(_extractor, NullLogger<HtmlIngestor>.Instance);
        var doc = await sut.IngestAsync(path, CancellationToken.None);

        doc.Segments.Should().AllSatisfy(s =>
            s.Text.Should().NotBeNullOrWhiteSpace("every segment must carry content"));
    }

    // ── Sample fixture ─────────────────────────────────────────────────────

    private const string SampleHtml = """
        <!DOCTYPE html>
        <html>
        <head><title>Test Article</title></head>
        <body>
          <article>
            <h1>The Quick Brown Fox</h1>
            <p>The quick brown fox jumps over the lazy dog. This is a test paragraph
               with enough content to be non-trivial. Lorem ipsum dolor sit amet,
               consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore
               et dolore magna aliqua.</p>
            <p>A second paragraph of content. Ut enim ad minim veniam, quis nostrud
               exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.
               Duis aute irure dolor in reprehenderit in voluptate velit esse cillum
               dolore eu fugiat nulla pariatur.</p>
          </article>
        </body>
        </html>
        """;

    public async ValueTask DisposeAsync()
    {
        await _sp.DisposeAsync();
        if (Directory.Exists(_storeDir))
        {
            try { Directory.Delete(_storeDir, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
