using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MarkdownViewer.Lab.Services.Ingestion;

/// <summary>
/// Routes drag-drop file paths and URL pastes into the ingestion pipeline
/// with <c>source="library"</c>.
///
/// <para>
/// URL handling for HTML: <see cref="AddUrlAsync"/> passes the URL string
/// directly to <see cref="WorkspaceIngestor.IngestAsync"/>, which dispatches
/// to <see cref="HtmlIngestor"/>. The <see cref="HtmlIngestor"/> is responsible
/// for its own fetch (via its injected <see cref="HttpClient"/>) and for running
/// <c>ILayoutExtractor.ExtractAsync</c> on the result.
/// </para>
///
/// <para>
/// URL handling for non-HTML content (e.g. <c>text/markdown</c>): the response
/// is fetched here with an <c>Accept: text/markdown, text/html</c> header, written
/// to a temp file, and dispatched as Markdown via <see cref="MarkdownIngestor"/>.
/// </para>
///
/// <para>
/// Idempotency: content-hash deduplication in <see cref="WorkspaceIngestor"/>
/// means calling <see cref="AddPathAsync"/> or <see cref="AddUrlAsync"/>
/// twice with the same content produces only one segment row per unique hash.
/// </para>
/// </summary>
public sealed class LibraryAdder
{
    private readonly WorkspaceIngestor _ingestor;
    private readonly HttpClient _http;
    private readonly ILogger<LibraryAdder> _log;

    public LibraryAdder(
        WorkspaceIngestor ingestor,
        HttpClient http,
        ILogger<LibraryAdder> log)
    {
        _ingestor = ingestor;
        _http     = http;
        _log      = log;
    }

    /// <summary>
    /// Ingests a local file path with <c>source="library"</c>.
    /// Routing is handled by <see cref="WorkspaceIngestor"/> dispatching to the
    /// registered <see cref="IIngestor"/> for the file extension.
    /// </summary>
    public Task AddPathAsync(string path, CancellationToken ct)
    {
        _log.LogInformation("LibraryAdder: adding path {Path}", path);
        return _ingestor.IngestAsync(path, source: "library", ct);
    }

    /// <summary>
    /// Routes <paramref name="url"/> into the ingestion pipeline.
    ///
    /// <para>
    /// For HTML URLs: passes the URL string directly to
    /// <see cref="WorkspaceIngestor.IngestAsync"/>, which dispatches to
    /// <see cref="HtmlIngestor"/>. The ingestor fetches and calls
    /// <c>ILayoutExtractor.ExtractAsync</c> internally.
    /// </para>
    ///
    /// <para>
    /// For non-HTML URLs: fetches the body here and writes a temp
    /// <c>.md</c> file, then passes to <see cref="WorkspaceIngestor"/>.
    /// </para>
    /// </summary>
    public async Task AddUrlAsync(Uri url, CancellationToken ct)
    {
        _log.LogInformation("LibraryAdder: processing URL {Url}", url);

        // Probe the content-type first with a lightweight HEAD request.
        // If HEAD is not supported, fall back to GET with early content-type
        // sniffing before reading the body.
        string? contentType = null;
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            using var headResp = await _http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct);
            if (headResp.IsSuccessStatusCode)
                contentType = headResp.Content.Headers.ContentType?.MediaType;
        }
        catch
        {
            // HEAD not supported or network error — fall through to GET.
        }

        if (contentType is null || IsHtml(contentType))
        {
            // HTML path: pass the URL string to WorkspaceIngestor so HtmlIngestor
            // handles its own fetch and runs ILayoutExtractor end-to-end.
            _log.LogDebug("LibraryAdder: routing {Url} to HtmlIngestor (StyloExtract path)", url);
            await _ingestor.IngestAsync(url.ToString(), source: "library", ct);
            return;
        }

        // Non-HTML path (text/markdown, text/plain, etc.): fetch body and write
        // a temp .md file for MarkdownIngestor.
        _log.LogDebug("LibraryAdder: fetching non-HTML {ContentType} from {Url}", contentType, url);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "text/markdown, text/html");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        resp.EnsureSuccessStatusCode();

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        var tmpPath = Path.Combine(Path.GetTempPath(),
            $"lucidlab-library-{Guid.NewGuid():N}.md");
        try
        {
            await File.WriteAllBytesAsync(tmpPath, bytes, ct);
            await _ingestor.IngestAsync(tmpPath, source: "library", ct);
        }
        finally
        {
            TryDelete(tmpPath);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool IsHtml(string contentType)
        => contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);

    private void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { _log.LogTrace(ex, "LibraryAdder: failed to delete temp file {Path}", path); }
    }
}
