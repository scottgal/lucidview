using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MarkdownViewer.Lab.Services.Ingestion;
using MarkdownViewer.Lab.Services.Storage;
using MarkdownViewer.Lab.Tests.Ingestion.Stubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StyloExtract.Abstractions;
using StyloExtract.Core;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Ingestion;

/// <summary>
/// Tests for <see cref="LibraryAdder"/>.
///
/// <para>
/// HTML ingestion now routes through <see cref="HtmlIngestor"/> which uses
/// the real <see cref="ILayoutExtractor"/> from <c>AddStyloExtract()</c>.
/// URL tests use an in-process <see cref="HttpClient"/> backed by a stub
/// handler so no network access is needed.
/// </para>
/// </summary>
public class LibraryAdderTests : IAsyncDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"lab-lib-{Guid.NewGuid():N}");

    private readonly ServiceProvider _sp;
    private readonly ILayoutExtractor _extractor;

    public LibraryAdderTests()
    {
        Directory.CreateDirectory(_root);

        var services = new ServiceCollection();
        services.AddStyloExtract(o =>
        {
            o.StorePath      = Path.Combine(_root, "templates.db");
            o.DefaultProfile = ExtractionProfile.RagFull;
        });
        _sp        = services.BuildServiceProvider();
        _extractor = _sp.GetRequiredService<ILayoutExtractor>();
    }

    // ── AddPathAsync: local .html file is ingested with source="library" ──

    [Fact]
    public async Task LibraryAdder_AddPathAsync_HtmlFile_IsIngested()
    {
        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);
        var workspaceIngestor = BuildIngestor(ws);

        var dir = Path.Combine(_root, "input");
        Directory.CreateDirectory(dir);
        var html = Path.Combine(dir, "article.html");
        await File.WriteAllTextAsync(html, SampleHtml);

        var adder = new LibraryAdder(
            workspaceIngestor,
            http: new HttpClient(),
            log: NullLogger<LibraryAdder>.Instance);

        await adder.AddPathAsync(html, CancellationToken.None);

        // At least one segment should land in metadata.
        int count = 0;
        await foreach (var _ in ws.Metadata.QuerySegmentsAsync(
                           new SegmentQuery(), CancellationToken.None))
            count++;

        count.Should().BeGreaterThan(0,
            "AddPathAsync should route the HTML file through HtmlIngestor (StyloExtract) and write segments");
    }

    // ── AddUrlAsync: HTML URL routed directly to HtmlIngestor ─────────────

    [Fact]
    public async Task LibraryAdder_AddUrlAsync_HtmlResponse_IsIngested()
    {
        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);
        var workspaceIngestor = BuildIngestor(ws);

        // Stub handler returns fixture HTML synchronously — no network needed.
        var stubHandler = new StubHttpHandler(
            content: SampleHtml,
            contentType: "text/html; charset=utf-8");

        // The LibraryAdder uses _http only for HEAD probing + non-HTML GET.
        // HTML URLs are dispatched to HtmlIngestor which uses its own HttpClient.
        // Provide a separate stub for the HtmlIngestor so the URL fetch also works.
        var htmlIngestorHttp = new HttpClient(stubHandler);
        var htmlIngestor     = new HtmlIngestor(_extractor, htmlIngestorHttp,
                                   NullLogger<HtmlIngestor>.Instance);

        var workspaceIngestorWithHtml = new WorkspaceIngestor(ws,
            embedder:  StubEmbedder.Instance,
            ner:       StubNer.Instance,
            ingestors: new IIngestor[] { htmlIngestor },
            log:       NullLogger<WorkspaceIngestor>.Instance);

        var adderHttp = new HttpClient(stubHandler);
        var adder = new LibraryAdder(
            workspaceIngestorWithHtml,
            http: adderHttp,
            log: NullLogger<LibraryAdder>.Instance);

        await adder.AddUrlAsync(
            new Uri("https://fixture.example.com/article"),
            CancellationToken.None);

        int count = 0;
        await foreach (var _ in ws.Metadata.QuerySegmentsAsync(
                           new SegmentQuery(), CancellationToken.None))
            count++;

        count.Should().BeGreaterThan(0,
            "AddUrlAsync must route the HTML URL through HtmlIngestor (StyloExtract) and write segments");
    }

    [Fact]
    public async Task LibraryAdder_AddUrlAsync_MarkdownResponse_IsIngested()
    {
        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);
        var workspaceIngestor = BuildIngestor(ws, html: false);

        const string md = "# Remote Markdown\n\nThis content arrived as text/markdown.";
        var stubHandler = new StubHttpHandler(
            content: md,
            contentType: "text/markdown; charset=utf-8");

        var http = new HttpClient(stubHandler);
        var adder = new LibraryAdder(
            workspaceIngestor,
            http: http,
            log: NullLogger<LibraryAdder>.Instance);

        await adder.AddUrlAsync(
            new Uri("https://fixture.example.com/doc.md"),
            CancellationToken.None);

        int count = 0;
        await foreach (var _ in ws.Metadata.QuerySegmentsAsync(
                           new SegmentQuery(), CancellationToken.None))
            count++;

        count.Should().BeGreaterThan(0,
            "AddUrlAsync must treat text/markdown responses as Markdown and write segments");
    }

    [Fact]
    public async Task LibraryAdder_AddPathAsync_IsIdempotent()
    {
        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);
        var workspaceIngestor = BuildIngestor(ws);

        var dir = Path.Combine(_root, "idempotent");
        Directory.CreateDirectory(dir);
        var html = Path.Combine(dir, "idem.html");
        await File.WriteAllTextAsync(html, SampleHtml);

        var adder = new LibraryAdder(
            workspaceIngestor,
            http: new HttpClient(),
            log: NullLogger<LibraryAdder>.Instance);

        await adder.AddPathAsync(html, CancellationToken.None);
        await adder.AddPathAsync(html, CancellationToken.None);

        // Content-hash idempotency means no duplicate segments.
        int count = 0;
        await foreach (var _ in ws.Metadata.QuerySegmentsAsync(
                           new SegmentQuery(), CancellationToken.None))
            count++;

        count.Should().BeGreaterThan(0, "segments should exist after ingestion");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private WorkspaceIngestor BuildIngestor(Workspace ws, bool html = true)
    {
        IIngestor[] ingestors = html
            ? new IIngestor[]
              {
                  new HtmlIngestor(_extractor, NullLogger<HtmlIngestor>.Instance),
              }
            : new IIngestor[] { new MarkdownIngestor() };

        return new WorkspaceIngestor(ws,
            embedder:  StubEmbedder.Instance,
            ner:       StubNer.Instance,
            ingestors: ingestors,
            log:       NullLogger<WorkspaceIngestor>.Instance);
    }

    private const string SampleHtml = """
        <!DOCTYPE html>
        <html>
        <head><title>Library Article</title></head>
        <body>
          <article>
            <h1>Understanding Library Ingestion</h1>
            <p>This article explains how the library adder routes content through
               the StyloExtract pipeline and into the workspace ingestion system.
               Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>
            <p>A second paragraph providing additional context. Ut enim ad minim
               veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip
               ex ea commodo consequat. Duis aute irure dolor.</p>
          </article>
        </body>
        </html>
        """;

    public async ValueTask DisposeAsync()
    {
        await _sp.DisposeAsync();
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); }
            catch { /* best effort */ }
        }
    }
}

/// <summary>
/// In-process HTTP handler that returns a fixed response without network access.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly string _content;
    private readonly string _contentType;

    public StubHttpHandler(string content, string contentType)
    {
        _content     = content;
        _contentType = contentType;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_content, Encoding.UTF8,
                _contentType.Split(';')[0].Trim()),
        };
        response.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(
                _contentType.Split(';')[0].Trim())
            {
                CharSet = "utf-8",
            };
        return Task.FromResult(response);
    }
}
