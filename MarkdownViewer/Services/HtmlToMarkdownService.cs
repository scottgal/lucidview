using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using StyloExtract.Abstractions;
using StyloExtract.Heuristics;
using StyloExtract.Html;
using StyloExtract.Markdown;

namespace MarkdownViewer.Services;

public sealed class HtmlToMarkdownService
{
    private readonly IHtmlDomParser _parser = new AngleSharpHtmlDomParser();
    private readonly IDomCleaner _cleaner = new DomCleaner();
    private readonly IBlockSegmenter _segmenter = new BlockSegmenter();
    private readonly IBlockClassifier _classifier = HeuristicBlockClassifier.LoadFromEmbeddedResources();
    private readonly IMarkdownRenderer _renderer = new TypedMarkdownRenderer();

    public string Convert(string html, Uri? sourceUri = null)
    {
        var doc = _parser.Parse(html, sourceUri);
        PromoteHtmxLinks(doc);
        TagMermaidPres(doc);
        _cleaner.Clean(doc);
        var blocks = _classifier.Classify(_segmenter.Segment(doc));
        return _renderer.Render(blocks, ExtractionProfile.RagFull);
    }

    public static bool LooksLikeHtml(string body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        var span = body.AsSpan().TrimStart();
        return span.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || span.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    // HTMX anchors often omit href and put the URL in hx-get/hx-post. Copy it
    // back to href so the markdown renderer emits a real link.
    private static void PromoteHtmxLinks(IDocument doc)
    {
        foreach (var a in doc.QuerySelectorAll("a"))
        {
            if (!string.IsNullOrEmpty(a.GetAttribute("href"))) continue;
            var url = a.GetAttribute("hx-get") ?? a.GetAttribute("hx-post");
            if (string.IsNullOrEmpty(url)) continue;
            a.SetAttribute("href", url);
        }
    }

    // <pre class="mermaid">...</pre> -> wrap content in <code class="language-mermaid">
    // so the StyloExtract walker emits ```mermaid and the mermaid pipeline renders it.
    private static void TagMermaidPres(IDocument doc)
    {
        foreach (var pre in doc.QuerySelectorAll("pre.mermaid"))
        {
            if (pre.QuerySelector("code") is not null) continue;
            var source = pre.TextContent;
            var code = doc.CreateElement("code");
            code.SetAttribute("class", "language-mermaid");
            code.TextContent = source;
            pre.InnerHtml = string.Empty;
            pre.AppendChild(code);
        }
    }
}
