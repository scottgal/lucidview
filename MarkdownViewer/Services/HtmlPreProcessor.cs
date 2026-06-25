using AngleSharp.Dom;
using StyloExtract.Abstractions;
using StyloExtract.Html;

namespace MarkdownViewer.Services;

/// <summary>
/// Shared pre-processing steps applied to raw HTML before markdown conversion.
/// Used by both lean's <see cref="HtmlToMarkdownService"/> and FULL's
/// <see cref="HtmlToMarkdownServiceFull"/> so logic is not duplicated.
/// </summary>
public static class HtmlPreProcessor
{
    private static readonly IHtmlDomParser Parser = new AngleSharpHtmlDomParser();

    /// <summary>
    /// Parse <paramref name="html"/>, apply all pre-processing transforms, and
    /// return the serialised outer HTML of the modified document.
    /// </summary>
    public static string Apply(string html)
    {
        var doc = Parser.Parse(html, null);
        PromoteHtmxLinks(doc);
        TagMermaidPres(doc);
        return doc.DocumentElement.OuterHtml;
    }

    // HTMX anchors often omit href and put the URL in hx-get/hx-post. Copy it
    // back to href so the markdown renderer emits a real link.
    public static void PromoteHtmxLinks(IDocument doc)
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
    public static void TagMermaidPres(IDocument doc)
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
