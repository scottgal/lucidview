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
/// URL handling: HTTP-fetches the URL with <c>Accept: text/markdown, text/html</c>.
/// Responses with a <c>text/html</c> content-type are handed to
/// <see cref="HtmlIngestor"/>; all other responses (including
/// <c>text/markdown</c>) are treated as Markdown and handed to
/// <see cref="MarkdownIngestor"/> via a temp file.
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
    /// HTTP-fetches <paramref name="url"/>, detects whether the response is
    /// HTML or Markdown, and ingests accordingly with <c>source="library"</c>.
    /// </summary>
    public async Task AddUrlAsync(Uri url, CancellationToken ct)
    {
        _log.LogInformation("LibraryAdder: fetching {Url}", url);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "text/markdown, text/html");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        resp.EnsureSuccessStatusCode();

        var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);

        if (IsHtml(contentType))
        {
            // Write to a temp file so HtmlIngestor can read it via the standard
            // IIngestor interface.  The temp path also becomes the document Path
            // stored in metadata.
            var tmpPath = Path.Combine(Path.GetTempPath(),
                $"lucidlab-library-{Guid.NewGuid():N}.html");
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
        else
        {
            // Treat as Markdown (text/markdown or text/plain).
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
