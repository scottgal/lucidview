using StyloExtract.Abstractions;

namespace MarkdownViewer.Services;

/// <summary>
/// FULL-edition HTML→Markdown converter. Uses <see cref="ILayoutExtractor"/> from
/// StyloExtract.Core which adds template-learning on top of the heuristic baseline:
/// after the first visit to a host its structural fingerprint is stored in the SQLite
/// template store and subsequent requests use the learned extractor rather than
/// re-running full heuristics.
/// </summary>
public sealed class HtmlToMarkdownServiceFull : IHtmlToMarkdownService
{
    private readonly ILayoutExtractor _extractor;

    public HtmlToMarkdownServiceFull(ILayoutExtractor extractor)
    {
        _extractor = extractor;
    }

    public async Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default)
    {
        // Apply shared pre-processing (HTMX link promotion, mermaid <pre> wrapping)
        // before handing the HTML to the layout extractor.
        var pre = HtmlPreProcessor.Apply(html);
        var result = await _extractor.ExtractAsync(pre, sourceUri, cancellationToken: ct);
        return result.Markdown;
    }
}
