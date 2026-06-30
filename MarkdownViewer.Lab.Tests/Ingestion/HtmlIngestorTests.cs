using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MarkdownViewer.Lab.Services.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Ingestion;

/// <summary>
/// Tests for <see cref="HtmlIngestor"/>.
/// </summary>
public class HtmlIngestorTests
{
    // ── CanHandle ─────────────────────────────────────────────────────────

    [Fact]
    public void HtmlIngestor_CanHandle_HtmlExtension()
    {
        var sut = new HtmlIngestor(NullLogger<HtmlIngestor>.Instance);
        sut.CanHandle("article.html").Should().BeTrue();
        sut.CanHandle("page.htm").Should().BeTrue();
    }

    [Fact]
    public void HtmlIngestor_CanHandle_HttpsUrl()
    {
        var sut = new HtmlIngestor(NullLogger<HtmlIngestor>.Instance);
        sut.CanHandle("https://example.com/article").Should().BeTrue();
        sut.CanHandle("http://example.com/page").Should().BeTrue();
    }

    [Fact]
    public void HtmlIngestor_CanHandle_ReturnsFalse_ForMarkdown()
    {
        var sut = new HtmlIngestor(NullLogger<HtmlIngestor>.Instance);
        sut.CanHandle("readme.md").Should().BeFalse();
        sut.CanHandle("file.txt").Should().BeFalse();
    }

    // ── IngestAsync: local HTML file ──────────────────────────────────────

    [Fact]
    public async Task HtmlIngestor_IngestAsync_LocalFile_ReturnsSegments()
    {
        // Arrange: write a simple HTML fixture
        var dir = Path.Combine(Path.GetTempPath(), $"html-ingestor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "sample.html");
        await File.WriteAllTextAsync(path, SampleHtml);

        try
        {
            var sut = new HtmlIngestor(NullLogger<HtmlIngestor>.Instance);

            // Act
            var doc = await sut.IngestAsync(path, CancellationToken.None);

            // Assert
            doc.Should().NotBeNull();
            doc.Path.Should().Be(path);
            doc.MimeType.Should().Be("text/html");
            doc.Segments.Should().NotBeEmpty("HTML with real content should produce at least one segment");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task HtmlIngestor_IngestAsync_LocalFile_TitleExtractedFromH1()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"html-ingestor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "article.html");
        await File.WriteAllTextAsync(path, SampleHtml);

        try
        {
            var sut = new HtmlIngestor(NullLogger<HtmlIngestor>.Instance);
            var doc = await sut.IngestAsync(path, CancellationToken.None);

            // Title should come from the <title> element or the H1.
            doc.Title.Should().NotBeNullOrEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task HtmlIngestor_IngestAsync_LocalFile_SegmentTextIsNonTrivial()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"html-ingestor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "content.html");
        await File.WriteAllTextAsync(path, SampleHtml);

        try
        {
            var sut = new HtmlIngestor(NullLogger<HtmlIngestor>.Instance);
            var doc = await sut.IngestAsync(path, CancellationToken.None);

            // Every segment should have non-trivial text.
            doc.Segments.Should().AllSatisfy(s =>
                s.Text.Should().NotBeNullOrWhiteSpace("every segment must carry content"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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
}
