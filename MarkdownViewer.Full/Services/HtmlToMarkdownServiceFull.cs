using System.Diagnostics;
using System.Threading;
using StyloExtract.Abstractions;
using StyloExtract.Core;
using StyloExtract.Playwright;

namespace MarkdownViewer.Services;

/// <summary>
/// FULL-edition HTML→Markdown converter. Uses <see cref="ILayoutExtractor"/> from
/// StyloExtract.Core which adds template-learning on top of the heuristic baseline:
/// after the first visit to a host its structural fingerprint is stored in the SQLite
/// template store and subsequent requests use the learned extractor rather than
/// re-running full heuristics.
///
/// Task 5 extension: when the first-pass extraction returns too little content or
/// SPA markers are detected, automatically retries via a Playwright-rendered fetch.
///
/// Task 8 extension: wraps each conversion with Stopwatch timing and pushes a
/// <see cref="LastExtractionInfo"/> record to <see cref="ExtractionTelemetry"/> so
/// the status bar and F2 details panel have live dogfood signal.
/// </summary>
public sealed class HtmlToMarkdownServiceFull : IHtmlToMarkdownService
{
    /// <summary>
    /// User-toggleable extraction mode. Read = RagFull (article body, captions,
    /// related links). Scan = Sitemap (title + nav + breadcrumb only — for
    /// browser-mode navigation across a site).
    /// </summary>
    public enum Mode { Read, Scan }

    /// <summary>
    /// Process-wide current mode. Toggle from the FULL header UI. Re-loading
    /// the current URL applies the new mode at the next ExtractAsync.
    /// </summary>
    public static Mode CurrentMode { get; set; } = Mode.Read;

    private static int _browsersEnsured;  // 0 = not yet, 1 = done

    private static async Task EnsureBrowsersOnceAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _browsersEnsured, 1) == 1) return;
        try
        {
            await Task.Run(() => PlaywrightInstaller.EnsureBrowsersInstalled("chromium"), ct);
        }
        catch
        {
            // Reset so a future call can retry after a transient failure.
            Interlocked.Exchange(ref _browsersEnsured, 0);
            throw;
        }
    }

    private readonly ILayoutExtractor _extractor;
    private readonly IRenderedHtmlFetcher _renderedFetcher;
    private readonly ExtractionTelemetry _telemetry;

    public HtmlToMarkdownServiceFull(
        ILayoutExtractor extractor,
        IRenderedHtmlFetcher renderedFetcher,
        ExtractionTelemetry telemetry)
    {
        _extractor = extractor;
        _renderedFetcher = renderedFetcher;
        _telemetry = telemetry;
    }

    public async Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var fetcher = "Http";
        var llmDuration = TimeSpan.Zero;
        var llmFired = false;

        // Stage 1: Fetch — the caller (LoadWebPage in MainWindow) already did
        // the HTTP fetch before calling us. As of alpha.16, DownloadWebPageAsync
        // emits its own Fetch+Http+<streaming-verdict> stage with the real fetch
        // duration, so we DO NOT overwrite it here — that would clobber the
        // streaming gateway verdict in the status bar. The Playwright retry
        // path below still emits its own Fetch stage explicitly.

        // Stage 2: Match — pre-process + extract under the current Mode's profile.
        var profile = CurrentMode == Mode.Scan
            ? ExtractionProfile.Sitemap
            : ExtractionProfile.RagFull;
        var extractOpts = new ExtractionOptions { Profile = profile };
        _telemetry.EmitStage(ExtractionStage.Match, started: true, detail: profile.ToString());
        var matchSw = Stopwatch.StartNew();
        var pre = HtmlPreProcessor.Apply(html);
        var result = await _extractor.ExtractAsync(pre, sourceUri, extractOpts, cancellationToken: ct);
        var md = result.Markdown;
        llmFired = result.LlmInductionFired;
        _telemetry.EmitStage(ExtractionStage.Match, started: false,
            detail: result.Match?.Status.ToString() ?? "Unknown", duration: matchSw.Elapsed);

        // Auto-retry via Playwright when:
        //  (a) the static fetch looks empty / SPA-shell / sparse-blocks, OR
        //  (b) StyloExtract just inducted a Novel template — the LLM template
        //      trainer will get a much better skeleton from the JS-rendered DOM
        //      than from the static HTML's nav-heavy markup, AND the first
        //      user-visible extraction will already use the Playwright result
        //      instead of the often-wrong static-HTML "first guess".
        // EnsureBrowsersOnceAsync wraps the sync PlaywrightInstaller.EnsureBrowsersInstalled
        // in Task.Run (no async overload in preview package).
        // Deterministic-first: only retry under Playwright when the static
        // extraction looks genuinely broken (empty / SPA / sparse). Forcing
        // Playwright on every Novel match poisons quality on server-rendered
        // sites (blogs, docs, mostlylucid) where the heuristic does fine work
        // and the JS-rendered DOM has extra bloat that confuses the inducer.
        var shouldRetry = sourceUri is not null
            && RenderedFetchPolicy.ShouldRetry(html, md, result.Blocks.Count);
        if (shouldRetry)
        {
            // Re-enter the Fetch stage for the Playwright pass.
            _telemetry.EmitStage(ExtractionStage.Fetch, started: true, detail: "Playwright");
            var pwSw = Stopwatch.StartNew();
            await EnsureBrowsersOnceAsync(ct);
            var rendered = await _renderedFetcher.FetchAsync(sourceUri!, new RenderOptions
            {
                // Capture the initial DOM before client-side JS routers fire — BBC
                // News auto-navigates /news → /articles/<id> on NetworkIdle, which
                // hides the page the user requested.
                WaitUntil = PlaywrightWaitUntil.Load,
            }, ct);
            _telemetry.EmitStage(ExtractionStage.Fetch, started: false,
                detail: "Playwright", duration: pwSw.Elapsed);

            // Re-enter Match against the rendered DOM.
            _telemetry.EmitStage(ExtractionStage.Match, started: true);
            var matchSw2 = Stopwatch.StartNew();
            var renderedPre = HtmlPreProcessor.Apply(rendered.Html);
            var renderedResult = await _extractor.ExtractAsync(renderedPre, rendered.FinalUri, extractOpts, cancellationToken: ct);
            llmDuration = matchSw2.Elapsed;
            llmFired = renderedResult.LlmInductionFired;
            md = renderedResult.Markdown;
            fetcher = "Playwright";
            result = renderedResult;
            _telemetry.EmitStage(ExtractionStage.Match, started: false,
                detail: renderedResult.Match?.Status.ToString() ?? "Unknown", duration: matchSw2.Elapsed);
        }

        // StyloExtract concatenates link + image markdown without separation:
        //   - [Headline](/news/articles/xxx)![alt](https://image.jpg)
        //   - [Other](/x)Attribution[Business](/news)Posted2h![alt](url)
        // LiveMarkdown renders inline images at text-line height (~16 px) → thin
        // colored strips. Move every non-line-start `![` onto its own block by
        // inserting a blank line before it.
        md = System.Text.RegularExpressions.Regex.Replace(md, @"(?<!\n)!\[", "\n\n![");

        sw.Stop();

        // Dogfood debug: dump latest markdown to disk for inspection.
        try
        {
            var dumpDir = Path.Combine(AppPaths.LocalState, "extractions");
            Directory.CreateDirectory(dumpDir);
            var safeHost = sourceUri?.Host ?? "local";
            var stamp = DateTime.UtcNow.ToString("HHmmss");
            File.WriteAllText(Path.Combine(dumpDir, $"{safeHost}-{stamp}.md"), md);
        }
        catch { }

        _telemetry.Record(new LastExtractionInfo(
            When: DateTime.UtcNow,
            Source: sourceUri,
            MatchStatus: result.Match?.Status.ToString() ?? "Unknown",
            TemplateId: result.Match?.TemplateId ?? Guid.Empty,
            TemplateVersion: result.Match?.TemplateVersion ?? 0,
            Fetcher: fetcher,
            TotalDuration: sw.Elapsed,
            LlmInductionFired: llmFired,
            LlmDuration: llmDuration,
            BlockCount: result.Blocks.Count,
            OutputCharacterCount: md.Length));

        return md;
    }

    private static string BuildEmptyRenderExplanation(Uri? sourceUri, int htmlLength)
    {
        var host = sourceUri?.Host ?? "this site";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# No extractable content");
        sb.AppendLine();
        sb.AppendLine($"**{host}** sent us {htmlLength / 1024:N0} KB of HTML but our heuristic " +
            "extractor identified zero content blocks. The page didn't render empty because " +
            "lucidVIEW is broken — it rendered empty because the page itself doesn't carry " +
            "extractable article content in a shape our pipeline understands.");
        sb.AppendLine();
        sb.AppendLine($"## What this usually means");
        sb.AppendLine();
        sb.AppendLine("- **Authentication wall.** Reddit (anonymous), Twitter / X, LinkedIn, " +
            "and most paywalled news sites serve a minimal logged-out shell to non-authenticated " +
            "visitors. The real content is gated behind a sign-in.");
        sb.AppendLine("- **JavaScript-only content.** The HTML response is a framework shell; " +
            "the article body is fetched and injected by JS after the page loads. lucidVIEW's " +
            "Playwright auto-retry already attempted this for you; it still produced nothing, " +
            "so the JS pathway is gated by something we can't reach (auth, anti-bot, geofence).");
        sb.AppendLine("- **Anti-scraper / anti-bot protection.** Cloudflare interstitials, " +
            "captchas, and shaped responses for non-residential IPs all produce short minimal " +
            "DOMs that have no article body to extract.");
        sb.AppendLine("- **Unusual layout.** Some sites build the page from many short widgets " +
            "and never present a single \"article body\" the heuristic can lock onto.");
        sb.AppendLine();
        sb.AppendLine($"## What you can try");
        sb.AppendLine();
        if (sourceUri is not null)
        {
            sb.AppendLine($"- Open [{sourceUri}]({sourceUri}) in a regular browser. If it loads " +
                "fine there but empty here, the site is gating us out and there's nothing " +
                "lucidVIEW can do client-side.");
        }
        sb.AppendLine("- Look for a print / text / lite version of the URL (e.g. `text.npr.org`, " +
            "`lite.cnn.com`, the site's RSS feed, or `?print=1` query).");
        sb.AppendLine("- If you're the site operator: serving a static SSR fallback or honouring " +
            "`Accept: text/markdown` would let lucidVIEW render this page perfectly.");
        sb.AppendLine();
        sb.AppendLine($"## The conversion engine");
        sb.AppendLine();
        sb.AppendLine($"lucidVIEW uses **StyloExtract**, a separate open-source library, for the " +
            "HTML → Markdown conversion. Coverage improvements happen there, not in lucidVIEW: " +
            "[github.com/scottgal/styloextract](https://github.com/scottgal/styloextract).");
        return sb.ToString();
    }
}
