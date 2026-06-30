using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MarkdownViewer.Lab.Services.Ingestion;
using MarkdownViewer.Lab.Services.Storage;
using MarkdownViewer.Lab.Tests.Ingestion.Stubs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Ingestion;

public class WorkspaceIngestorTests : IAsyncDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"lab-ingest-{Guid.NewGuid():N}");

    // ── Happy path: Markdown ingestion writes all four substrates ──────────

    [Fact]
    public async Task IngestMarkdown_PopulatesAllFourSubstrates()
    {
        Directory.CreateDirectory(_root);
        var fixture = Path.Combine(_root, "sample.md");
        await File.WriteAllTextAsync(fixture,
            "# Hello\n\nFirst paragraph here.\n\nSecond paragraph here.");

        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);

        var ingestor = new WorkspaceIngestor(ws,
            embedder:  StubEmbedder.Instance,
            ner:       StubNer.Instance,
            ingestors: new IIngestor[] { new MarkdownIngestor() },
            log:       NullLogger<WorkspaceIngestor>.Instance);

        await ingestor.IngestAsync(fixture, source: "folder", CancellationToken.None);

        // Metadata: at least one segment recorded.
        var segments = new List<SegmentRow>();
        await foreach (var s in ws.Metadata.QuerySegmentsAsync(
                           new SegmentQuery(), CancellationToken.None))
            segments.Add(s);

        segments.Should().NotBeEmpty("at least one segment should be written");

        // Evidence: every segment's text must resolve.
        foreach (var s in segments)
        {
            var text = await ws.Evidence.GetAsync(s.ContentHash, CancellationToken.None);
            text.Should().NotBeNull($"evidence for hash {s.ContentHash.ToHex()} must resolve");
        }
    }

    [Fact]
    public async Task IngestMarkdown_Idempotent_NoDuplicateSegments()
    {
        Directory.CreateDirectory(_root);
        var fixture = Path.Combine(_root, "idempotent.md");
        await File.WriteAllTextAsync(fixture, "# Idempotency\n\nSame content every time.");

        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);

        var ingestor = new WorkspaceIngestor(ws,
            embedder:  StubEmbedder.Instance,
            ner:       StubNer.Instance,
            ingestors: new IIngestor[] { new MarkdownIngestor() },
            log:       NullLogger<WorkspaceIngestor>.Instance);

        // Ingest twice.
        await ingestor.IngestAsync(fixture, source: "folder", CancellationToken.None);
        await ingestor.IngestAsync(fixture, source: "folder", CancellationToken.None);

        var segments = new List<SegmentRow>();
        await foreach (var s in ws.Metadata.QuerySegmentsAsync(
                           new SegmentQuery(), CancellationToken.None))
            segments.Add(s);

        // Content-hash idempotency: no duplicate segments for the same content.
        var hashes = new HashSet<ulong>();
        foreach (var seg in segments)
            hashes.Add(seg.ContentHash.Value).Should().BeTrue(
                $"duplicate content hash {seg.ContentHash.ToHex()} should not appear twice");
    }

    // ── Personal-corpus filter ─────────────────────────────────────────────

    [Fact]
    public async Task IngestMarkdown_PersonalSource_FilteredByDefault()
    {
        Directory.CreateDirectory(_root);
        var fixture = Path.Combine(_root, "personal.md");
        await File.WriteAllTextAsync(fixture, "# Private\n\nPersonal notes.");

        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);

        var ingestor = new WorkspaceIngestor(ws,
            embedder:  StubEmbedder.Instance,
            ner:       StubNer.Instance,
            ingestors: new IIngestor[] { new MarkdownIngestor() },
            log:       NullLogger<WorkspaceIngestor>.Instance);

        await ingestor.IngestAsync(fixture, source: "personal:default", CancellationToken.None);

        // Default query (IncludePersonal=false) should return nothing.
        var defaultSegments = new List<SegmentRow>();
        await foreach (var s in ws.Metadata.QuerySegmentsAsync(
                           new SegmentQuery(IncludePersonal: false), CancellationToken.None))
            defaultSegments.Add(s);
        defaultSegments.Should().BeEmpty("personal segments must be filtered by default");

        // Explicit opt-in should expose them.
        var personalSegments = new List<SegmentRow>();
        await foreach (var s in ws.Metadata.QuerySegmentsAsync(
                           new SegmentQuery(IncludePersonal: true), CancellationToken.None))
            personalSegments.Add(s);
        personalSegments.Should().NotBeEmpty("personal segments must be visible with IncludePersonal=true");
    }

    // ── SimpleSegmentSelector unit tests ──────────────────────────────────

    [Fact]
    public void SimpleSegmentSelector_EmptyText_ReturnsEmpty()
    {
        var result = SimpleSegmentSelector.Chunk(string.Empty);
        result.Should().BeEmpty();
    }

    [Fact]
    public void SimpleSegmentSelector_SingleParagraph_ReturnsSingleSegment()
    {
        var text = "Hello world this is a single paragraph with no blank lines.";
        var result = SimpleSegmentSelector.Chunk(text);
        result.Should().HaveCount(1);
        result[0].Ordinal.Should().Be(0);
        result[0].Text.Should().Be(text.Trim());
    }

    [Fact]
    public void SimpleSegmentSelector_TwoParagraphs_MergesIfUnderTarget()
    {
        // Two short paragraphs well under 400 tokens should merge into one segment.
        var text = "First short paragraph.\n\nSecond short paragraph.";
        var result = SimpleSegmentSelector.Chunk(text, targetTokens: 400);
        result.Should().HaveCount(1, "two short paragraphs should merge under the 400-token target");
    }

    [Fact]
    public void SimpleSegmentSelector_LargeParagraphs_SplitsIntoMultiple()
    {
        // Generate content well over 400 tokens separated by a blank line.
        var longPara = string.Join(" ", System.Linq.Enumerable.Repeat("word", 300));
        var text = longPara + "\n\n" + longPara;
        var result = SimpleSegmentSelector.Chunk(text, targetTokens: 400);
        result.Should().HaveCountGreaterThan(1,
            "content over 400 tokens split by a blank line should produce multiple segments");
    }

    // ── Stub-reader NotSupportedException tests ───────────────────────────

    [Fact]
    public void PdfIngestor_CanHandle_Pdf()
    {
        var ingestor = new PdfIngestor();
        ingestor.CanHandle("document.pdf").Should().BeTrue();
        ingestor.CanHandle("doc.md").Should().BeFalse();
    }

    [Fact]
    public async Task PdfIngestor_IngestAsync_ThrowsNotSupportedException()
    {
        var ingestor = new PdfIngestor();
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => ingestor.IngestAsync("doc.pdf", CancellationToken.None));
        ex.Message.Should().Be(PdfIngestor.NotSupportedMessage);
    }

    [Fact]
    public void DocxIngestor_CanHandle_Docx()
    {
        var ingestor = new DocxIngestor();
        ingestor.CanHandle("report.docx").Should().BeTrue();
        ingestor.CanHandle("doc.md").Should().BeFalse();
    }

    [Fact]
    public async Task DocxIngestor_IngestAsync_ThrowsNotSupportedException()
    {
        var ingestor = new DocxIngestor();
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => ingestor.IngestAsync("report.docx", CancellationToken.None));
        ex.Message.Should().Be(DocxIngestor.NotSupportedMessage);
    }

    [Fact]
    public void GutenbergIngestor_CanHandle_GutenbergUrl()
    {
        var ingestor = new GutenbergIngestor();
        ingestor.CanHandle("https://www.gutenberg.org/files/1342/1342-0.txt").Should().BeTrue();
        ingestor.CanHandle("document.pdf").Should().BeFalse();
    }

    [Fact]
    public async Task GutenbergIngestor_IngestAsync_ThrowsNotSupportedException()
    {
        var ingestor = new GutenbergIngestor();
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => ingestor.IngestAsync(
                "https://www.gutenberg.org/files/1342/1342-0.txt", CancellationToken.None));
        ex.Message.Should().Be(GutenbergIngestor.NotSupportedMessage);
    }

    // ── No ingestor registered ─────────────────────────────────────────────

    [Fact]
    public async Task WorkspaceIngestor_UnknownExtension_ThrowsInvalidOperation()
    {
        Directory.CreateDirectory(_root);
        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);

        var ingestor = new WorkspaceIngestor(ws,
            embedder:  StubEmbedder.Instance,
            ner:       StubNer.Instance,
            ingestors: Array.Empty<IIngestor>(),
            log:       NullLogger<WorkspaceIngestor>.Instance);

        var act = async () => await ingestor.IngestAsync(
            "file.xyz", source: "folder", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    public async ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        await Task.CompletedTask;
    }
}
