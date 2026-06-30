using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MarkdownViewer.Lab.Services.Ingestion;

/// <summary>
/// Ingestor for HTML files (<c>.html</c> / <c>.htm</c>) and HTTP(S) URLs.
///
/// <para>
/// Pipeline: read HTML bytes → lightweight regex-based content extraction →
/// <see cref="SimpleSegmentSelector.Chunk"/>.
/// </para>
///
/// <para>
/// Implementation note: this uses a self-contained regex-based extraction
/// rather than the AngleSharp-backed <c>HtmlToMarkdownService</c> from lean
/// because the Lab project pulls in <c>Mostlylucid.LucidRAG.DocSummarizer.FullText.Lucene</c>
/// which upgrades AngleSharp to 1.5.x at runtime, conflicting with
/// <c>StyloExtract.Heuristics 2.0.0</c> which expects the 1.3.0 API.
/// The lightweight extractor is sufficient for the embedding pipeline's
/// purposes (clean prose text, not rendered GFM markdown).
/// </para>
///
/// <para>
/// Failure handling: if reading or extraction throws, the exception propagates
/// to <see cref="WorkspaceIngestor"/>. No silent fallbacks.
/// </para>
/// </summary>
public sealed class HtmlIngestor : IIngestor
{
    private readonly HttpClient? _http;
    private readonly ILogger<HtmlIngestor> _log;

    /// <summary>Constructs an <see cref="HtmlIngestor"/> for local file use.</summary>
    public HtmlIngestor(ILogger<HtmlIngestor> log)
        : this(http: null, log)
    {
    }

    /// <summary>Constructs an <see cref="HtmlIngestor"/> with an explicit <see cref="HttpClient"/> for URL fetching.</summary>
    public HtmlIngestor(HttpClient? http, ILogger<HtmlIngestor> log)
    {
        _http = http;
        _log  = log;
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
            _log.LogInformation("HtmlIngestor: reading local file {Path}", path);
            html = await File.ReadAllTextAsync(path, System.Text.Encoding.UTF8, ct);
        }

        var title = ExtractTitle(html, path, sourceUri);
        var text  = ExtractText(html);
        var segments = SimpleSegmentSelector.Chunk(text);

        return new IngestedDocument(path, "text/html", title, segments);
    }

    // ── HTML text extraction (no AngleSharp dependency) ───────────────────

    /// <summary>
    /// Extracts readable prose text from raw HTML using a multi-pass regex approach:
    /// 1. Remove script/style/noscript blocks.
    /// 2. Convert block elements to newlines to preserve paragraph structure.
    /// 3. Strip all remaining tags.
    /// 4. Decode HTML entities.
    /// 5. Collapse whitespace.
    /// </summary>
    internal static string ExtractText(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        // 1. Remove script, style, noscript content (including the tags).
        var text = Regex.Replace(html,
            @"<(script|style|noscript)[^>]*>.*?</(script|style|noscript)>",
            " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // 2. Replace block-level elements with newlines to preserve paragraphs.
        text = Regex.Replace(text,
            @"</(p|div|h[1-6]|article|section|blockquote|li|tr|td|th|br)[^>]*>",
            "\n", RegexOptions.IgnoreCase);

        text = Regex.Replace(text,
            @"<(br|hr)[^>]*/?>",
            "\n", RegexOptions.IgnoreCase);

        // 3. Strip remaining tags.
        text = Regex.Replace(text, "<[^>]+>", " ");

        // 4. Decode HTML entities.
        text = System.Net.WebUtility.HtmlDecode(text);

        // 5. Normalise whitespace: collapse multiple spaces/tabs on each line,
        //    then collapse blank-line runs to at most two newlines.
        var lines = text.Split('\n');
        var result = new System.Text.StringBuilder();
        bool lastBlank = false;
        foreach (var raw in lines)
        {
            var line = Regex.Replace(raw, @"[ \t]+", " ").Trim();
            if (string.IsNullOrEmpty(line))
            {
                if (!lastBlank) result.AppendLine();
                lastBlank = true;
            }
            else
            {
                result.AppendLine(line);
                lastBlank = false;
            }
        }

        return result.ToString().Trim();
    }

    /// <summary>
    /// Extracts the page title: tries <c>&lt;title&gt;</c>, then the first
    /// <c>&lt;h1&gt;</c>, then falls back to the file name or URL host.
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

        if (sourceUri is not null) return sourceUri.Host;

        try { return Path.GetFileNameWithoutExtension(path); }
        catch { return path; }
    }
}
