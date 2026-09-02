using System.Text.RegularExpressions;
using AngleSharp.Dom;
using StyloExtract.Abstractions;
using StyloExtract.Html;

namespace MarkdownViewer.Services;

/// <summary>
/// Shared pre-processing steps applied to raw HTML before markdown conversion.
/// Used by both lean's <see cref="HtmlToMarkdownService"/> and FULL's
/// <see cref="HtmlToMarkdownServiceFull"/> so logic is not duplicated.
/// </summary>
public static partial class HtmlPreProcessor
{
    /// <summary>
    /// A leading URI scheme, per RFC 3986: a letter followed by letters,
    /// digits, plus, minus or dot, then a colon. Deliberately anchored, so
    /// "notes/2026:review.html" is not mistaken for one.
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex SchemePattern();

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

    /// <summary>
    /// Rewrites every href and src to its absolute form, resolved against the
    /// document's own address.
    ///
    /// The parse is already given a source URI, and AngleSharp resolves
    /// against it perfectly well - but only through the typed Href and Source
    /// PROPERTIES. The markdown renderer reads the raw attribute, which still
    /// holds whatever the publisher wrote, so "/posts/two" reached the reader
    /// as "/posts/two" and an article's own links went nowhere: SafeLinkOpener
    /// requires an absolute http or https URL, correctly, so clicking one did
    /// nothing at all.
    ///
    /// Copying the resolved value back onto the attribute is what closes that
    /// gap, and it does it once for every consumer of this pipeline rather
    /// than at each place a link is later used.
    ///
    /// Anything that does not resolve is left exactly as written. A fragment
    /// ("#section"), a mailto:, a javascript: or a malformed address are all
    /// better passed through untouched than rewritten into something plausible
    /// but wrong; the link gate downstream is what decides whether to open
    /// them, and it already refuses everything that is not http or https.
    /// </summary>
    public static void ResolveRelativeUrls(IDocument doc, Uri? baseUri)
    {
        // The base is taken as a parameter rather than read from the document.
        // The parse is given a source URI, but it does not become the
        // document's BaseUri: that stays "about:", and resolving against it
        // turned "another/page.html" into "about:another/page.html", which is
        // worse than leaving it alone. Measured, after trying it the other
        // way round.
        if (baseUri is null) return;

        foreach (var anchor in doc.QuerySelectorAll("a[href]"))
            Absolutise(anchor, "href", baseUri);

        foreach (var image in doc.QuerySelectorAll("img[src]"))
            Absolutise(image, "src", baseUri);

        static void Absolutise(IElement element, string attribute, Uri baseUri)
        {
            var written = element.GetAttribute(attribute);
            if (string.IsNullOrWhiteSpace(written)) return;

            // Already carries a scheme, so it addresses something in its own
            // right: http, https, and equally mailto, javascript and data,
            // all of which are left exactly as the publisher wrote them for
            // the link gate downstream to judge.
            //
            // Tested with a regex rather than Uri.TryCreate(UriKind.Absolute),
            // which is the obvious way to ask and is wrong on Unix: there,
            // "/root/page.html" parses as an ABSOLUTE uri with the file
            // scheme, so every root-relative link on the site looked absolute
            // and was skipped. That is exactly the shape a publisher's own
            // internal links take, so the check that was meant to protect
            // mailto: was silently exempting most of the links on the page.
            if (SchemePattern().IsMatch(written)) return;

            // A bare fragment addresses this document, not another one.
            // Rewriting "#section" to an absolute URL would turn an in-page
            // jump into a navigation away from the article.
            if (written.StartsWith('#')) return;

            if (!Uri.TryCreate(baseUri, written, out var absolute)) return;

            element.SetAttribute(attribute, absolute.ToString());
        }
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
