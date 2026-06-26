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

        // Apply shared pre-processing (HTMX link promotion, mermaid <pre> wrapping)
        // before handing the HTML to the layout extractor.
        var pre = HtmlPreProcessor.Apply(html);
        var result = await _extractor.ExtractAsync(pre, sourceUri, cancellationToken: ct);
        var md = result.Markdown;
        llmFired = result.LlmInductionFired;

        // Auto-retry via Playwright when:
        //  (a) the static fetch looks empty / SPA-shell / sparse-blocks, OR
        //  (b) StyloExtract just inducted a Novel template — the LLM template
        //      trainer will get a much better skeleton from the JS-rendered DOM
        //      than from the static HTML's nav-heavy markup, AND the first
        //      user-visible extraction will already use the Playwright result
        //      instead of the often-wrong static-HTML "first guess".
        // EnsureBrowsersOnceAsync wraps the sync PlaywrightInstaller.EnsureBrowsersInstalled
        // in Task.Run (no async overload in preview package).
        var matchStatus = result.Match?.Status.ToString() ?? "Unknown";
        var shouldRetry = sourceUri is not null
            && (RenderedFetchPolicy.ShouldRetry(html, md, result.Blocks.Count)
                || matchStatus == "Novel");
        if (shouldRetry)
        {
            await EnsureBrowsersOnceAsync(ct);
            var rendered = await _renderedFetcher.FetchAsync(sourceUri, new RenderOptions(), ct);
            var renderedPre = HtmlPreProcessor.Apply(rendered.Html);
            var lsw = Stopwatch.StartNew();
            var renderedResult = await _extractor.ExtractAsync(renderedPre, rendered.FinalUri, cancellationToken: ct);
            llmDuration = lsw.Elapsed;
            llmFired = renderedResult.LlmInductionFired;
            md = renderedResult.Markdown;
            fetcher = "Playwright";
            result = renderedResult;
        }

        // StyloExtract concatenates link + image markdown without separation:
        //   - [Headline](/news/articles/xxx)![alt](https://image.jpg)
        //   - [Other](/x)Attribution[Business](/news)Posted2h![alt](url)
        // LiveMarkdown renders inline images at text-line height (~16 px) → thin
        // colored strips. Move every non-line-start `![` onto its own block by
        // inserting a blank line before it.
        md = System.Text.RegularExpressions.Regex.Replace(md, @"(?<!\n)!\[", "\n\n![");

        sw.Stop();
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
}
