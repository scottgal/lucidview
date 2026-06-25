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
/// </summary>
public sealed class HtmlToMarkdownServiceFull : IHtmlToMarkdownService
{
    private readonly ILayoutExtractor _extractor;
    private readonly IRenderedHtmlFetcher _renderedFetcher;

    public HtmlToMarkdownServiceFull(ILayoutExtractor extractor, IRenderedHtmlFetcher renderedFetcher)
    {
        _extractor = extractor;
        _renderedFetcher = renderedFetcher;
    }

    public async Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default)
    {
        // Apply shared pre-processing (HTMX link promotion, mermaid <pre> wrapping)
        // before handing the HTML to the layout extractor.
        var pre = HtmlPreProcessor.Apply(html);
        var result = await _extractor.ExtractAsync(pre, sourceUri, cancellationToken: ct);
        var md = result.Markdown;

        // Auto-retry via Playwright when first-pass extraction looks empty or SPA-detected.
        // Note: PlaywrightInstaller.EnsureBrowsersInstalledAsync does not exist in the
        // preview package — only the sync EnsureBrowsersInstalled is available.
        // We wrap it in Task.Run so this path stays awaitable.
        if (sourceUri is not null && RenderedFetchPolicy.ShouldRetry(html, md))
        {
            await Task.Run(() => PlaywrightInstaller.EnsureBrowsersInstalled("chromium"), ct);
            var rendered = await _renderedFetcher.FetchAsync(sourceUri, new RenderOptions(), ct);
            var renderedPre = HtmlPreProcessor.Apply(rendered.Html);
            var renderedResult = await _extractor.ExtractAsync(renderedPre, rendered.FinalUri, cancellationToken: ct);
            md = renderedResult.Markdown;
        }

        return md;
    }
}
