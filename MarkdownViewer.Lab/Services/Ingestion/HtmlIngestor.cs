using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StyloExtract.Abstractions;

namespace MarkdownViewer.Lab.Services.Ingestion;

/// <summary>
/// Ingestor for HTML files (<c>.html</c> / <c>.htm</c>) and HTTP(S) URLs.
///
/// <para>
/// Pipeline: read HTML bytes → <see cref="ILayoutExtractor.ExtractAsync"/> →
/// <see cref="SimpleSegmentSelector.Chunk"/> on the extracted Markdown.
/// </para>
///
/// <para>
/// StyloExtract is the primary extraction path. The resolved AngleSharp version
/// is 1.4.1-beta.506 — the claimed version conflict does not exist in the
/// current package graph.
/// </para>
///
/// <para>
/// Failure handling: if reading or extraction throws, the exception propagates
/// to <see cref="WorkspaceIngestor"/>. No silent fallbacks.
/// </para>
/// </summary>
public sealed class HtmlIngestor : IIngestor
{
    private readonly ILayoutExtractor _extractor;
    private readonly HttpClient? _http;
    private readonly ILogger<HtmlIngestor> _log;

    /// <summary>
    /// Constructs an <see cref="HtmlIngestor"/> for local file use (no URL fetching).
    /// </summary>
    public HtmlIngestor(ILayoutExtractor extractor, ILogger<HtmlIngestor> log)
        : this(extractor, http: null, log)
    {
    }

    /// <summary>
    /// Constructs an <see cref="HtmlIngestor"/> with an optional <see cref="HttpClient"/>
    /// for URL fetching.
    /// </summary>
    public HtmlIngestor(ILayoutExtractor extractor, HttpClient? http, ILogger<HtmlIngestor> log)
    {
        _extractor = extractor;
        _http      = http;
        _log       = log;
    }

    /// <inheritdoc/>
    public bool CanHandle(string path)
    {
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
         || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return true;

        return path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".htm",  StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<IngestedDocument> IngestAsync(string path, CancellationToken ct)
    {
        string html;
        Uri? sourceUri = null;

        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
         || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            sourceUri = new Uri(path);
            _log.LogInformation("HtmlIngestor: fetching {Uri}", sourceUri);
            var http = _http ?? new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, sourceUri);
            req.Headers.TryAddWithoutValidation("Accept", "text/markdown, text/html");
            using var resp = await http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            html = await resp.Content.ReadAsStringAsync(ct);
        }
        else
        {
            sourceUri = new Uri(path, UriKind.Absolute);
            _log.LogInformation("HtmlIngestor: reading local file {Path}", path);
            html = await File.ReadAllTextAsync(path, System.Text.Encoding.UTF8, ct);
        }

        _log.LogDebug("HtmlIngestor: running StyloExtract.ILayoutExtractor on {Uri}", sourceUri);

        var opts = new ExtractionOptions { Profile = ExtractionProfile.RagFull };
        var result = await _extractor.ExtractAsync(html, sourceUri, opts, ct);

        _log.LogDebug("HtmlIngestor: match={Status} markdown={Chars}chars",
            result.Match.Status, result.Markdown?.Length ?? 0);

        var markdown = result.Markdown ?? string.Empty;
        var title    = ExtractTitle(html, path, sourceUri);
        var segments = SimpleSegmentSelector.Chunk(markdown);

        return new IngestedDocument(path, "text/html", title, segments);
    }

    // ── Title extraction ─────────────────────────────────────────────────

    /// <summary>
    /// Extracts the page title: tries <c>&lt;title&gt;</c>, then the first
    /// <c>&lt;h1&gt;</c>, then falls back to the file name or URL host.
    /// StyloExtract provides clean prose in <c>result.Markdown</c> but does
    /// not expose a dedicated title property, so this helper reads from the
    /// raw HTML.
    /// </summary>
    internal static string ExtractTitle(string html, string path, Uri? sourceUri)
    {
        var tm = Regex.Match(html, @"<title[^>]*>(.*?)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (tm.Success)
        {
            var t = System.Net.WebUtility.HtmlDecode(tm.Groups[1].Value).Trim();
            if (!string.IsNullOrEmpty(t)) return t;
        }

        var hm = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (hm.Success)
        {
            var t = Regex.Replace(hm.Groups[1].Value, "<[^>]+>", "").Trim();
            t = System.Net.WebUtility.HtmlDecode(t);
            if (!string.IsNullOrEmpty(t)) return t;
        }

        if (sourceUri is not null)
        {
            if (sourceUri.IsFile)
            {
                try { return Path.GetFileNameWithoutExtension(sourceUri.LocalPath); }
                catch { /* fall through */ }
            }
            return sourceUri.Host;
        }

        try { return Path.GetFileNameWithoutExtension(path); }
        catch { return path; }
    }
}
