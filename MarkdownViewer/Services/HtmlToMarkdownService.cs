using StyloExtract.Abstractions;
using StyloExtract.Heuristics;
using StyloExtract.Html;
using StyloExtract.Markdown;

namespace MarkdownViewer.Services;

public sealed class HtmlToMarkdownService : IHtmlToMarkdownService
{
    private readonly IHtmlDomParser _parser = new AngleSharpHtmlDomParser();
    private readonly IDomCleaner _cleaner = new DomCleaner();
    private readonly IBlockSegmenter _segmenter = new BlockSegmenter();
    private readonly IBlockClassifier _classifier = HeuristicBlockClassifier.LoadFromEmbeddedResources();
    private readonly IMarkdownRenderer _renderer = new TypedMarkdownRenderer();

    public string Convert(string html, Uri? sourceUri = null)
    {
        var doc = _parser.Parse(html, sourceUri);
        HtmlPreProcessor.PromoteHtmxLinks(doc);
        HtmlPreProcessor.TagMermaidPres(doc);
        _cleaner.Clean(doc);
        var blocks = _classifier.Classify(_segmenter.Segment(doc));
        return _renderer.Render(blocks, ExtractionProfile.RagFull);
    }

    public Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default)
        => Task.FromResult(Convert(html, sourceUri));

    public static bool LooksLikeHtml(string body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        var span = body.AsSpan().TrimStart();
        return span.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || span.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }
}
