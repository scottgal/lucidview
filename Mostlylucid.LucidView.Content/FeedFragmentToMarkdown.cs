using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace MarkdownViewer.Services;

/// <summary>
/// Converts a FEED FRAGMENT to markdown.
///
/// WHY THIS IS NOT HtmlToMarkdownService. That service runs StyloExtract's
/// extraction pipeline, whose job is to find the article inside a full web
/// page and throw the navigation, the sidebars and the cookie banner away.
/// That is the right tool for a downloaded page and the wrong one for a feed
/// fragment, because a fragment has no chrome to strip: it IS the content.
/// Measured rather than assumed - the pipeline returns one newline for the
/// real APOD item body, and for the same fragment wrapped in article, main,
/// body and a full document with a title and an h1. Six shapes, no output.
/// The heuristics decide there is no article here, which for a page is right
/// and for a fragment is the whole content thrown away.
///
/// So the reading pane needs a converter that is faithful rather than clever:
/// no classification, no boilerplate removal, no judgement about what matters.
/// Everything the publisher wrote comes through.
///
/// WHAT IT FIXES. The reading pane used to assign a feed's HTML straight to a
/// markdown view. Nothing rendered as itself: an h2 was not a heading, an
/// anchor was not a link, and an img was nothing at all, so APOD, whose entire
/// content is one img inside one a, showed an article containing no picture.
///
/// RELATIVE ADDRESSES are resolved against the item's own URL, which is what
/// baseUri is for. AngleSharp's Href and Source properties return the resolved
/// absolute form once the document has a base, and absolute is not a nicety:
/// SafeLinkOpener requires UriKind.Absolute, so a relative href reached it as
/// a refusal and clicking the link did nothing whatsoever.
///
/// SCOPE. The tags a feed actually uses, and no more: headings, paragraphs,
/// links, images, emphasis, code, lists, block quotes, rules and line breaks.
/// A table comes through as its cell text rather than as a markdown table,
/// because a half-rendered pipe table is worse to read than plain sentences.
/// Anything unrecognised contributes its text, so an unexpected element loses
/// its formatting and never its content.
/// </summary>
public static class FeedFragmentToMarkdown
{
    private static readonly HtmlParser Parser = new();

    /// <summary>
    /// Returns markdown, or an empty string when the fragment held no text and
    /// no image. The caller decides what an empty article should say; this
    /// does not invent placeholder prose.
    /// </summary>
    public static string Convert(string? html, Uri? baseUri = null)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        // The base is given to the document rather than applied by hand, so
        // AngleSharp does the resolving and this code never has to reimplement
        // URL joining. A fragment is parsed as a body fragment, which is what
        // a feed description is.
        var document = baseUri is null
            ? Parser.ParseDocument($"<body>{html}</body>")
            : Parser.ParseDocument($"<base href=\"{baseUri.AbsoluteUri}\"><body>{html}</body>");

        var builder = new StringBuilder();
        if (document.Body is { } body) WriteBlocks(body, builder);

        return Tidy(builder.ToString());
    }

    /// <summary>
    /// Walks a container's children as BLOCKS, emitting each with a blank line
    /// after it. Anything that is not a recognised block is gathered as inline
    /// content, so a bare run of text and anchors between two paragraphs is
    /// not silently dropped for want of a wrapper.
    /// </summary>
    private static void WriteBlocks(INode container, StringBuilder output)
    {
        var pending = new StringBuilder();

        void FlushInline()
        {
            var text = Collapse(pending.ToString());
            pending.Clear();
            if (text.Length > 0) output.Append(text).Append("\n\n");
        }

        foreach (var node in container.ChildNodes)
        {
            if (node is not IElement element)
            {
                if (node.NodeType == NodeType.Text) pending.Append(node.TextContent);
                continue;
            }

            switch (element.LocalName)
            {
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                {
                    FlushInline();
                    var level = element.LocalName[1] - '0';
                    var text = Inline(element);
                    if (text.Length > 0)
                        output.Append(new string('#', level)).Append(' ').Append(text).Append("\n\n");
                    break;
                }

                case "p":
                {
                    FlushInline();
                    var text = Inline(element);
                    if (text.Length > 0) output.Append(text).Append("\n\n");
                    break;
                }

                case "ul" or "ol":
                    FlushInline();
                    WriteList(element, output, ordered: element.LocalName == "ol", depth: 0);
                    break;

                case "blockquote":
                {
                    FlushInline();
                    var inner = new StringBuilder();
                    WriteBlocks(element, inner);
                    foreach (var line in Tidy(inner.ToString()).Split('\n'))
                        output.Append("> ").Append(line).Append('\n');
                    output.Append('\n');
                    break;
                }

                case "pre":
                {
                    FlushInline();
                    // The text, not the inline conversion: a code block's
                    // asterisks and underscores are code, not emphasis.
                    output.Append("```\n").Append(element.TextContent.Trim('\n')).Append("\n```\n\n");
                    break;
                }

                case "hr":
                    FlushInline();
                    output.Append("---\n\n");
                    break;

                case "figure":
                    FlushInline();
                    WriteBlocks(element, output);
                    break;

                case "figcaption":
                {
                    FlushInline();
                    var caption = Inline(element);
                    // Italic, so a caption reads as one rather than as another
                    // paragraph of the article.
                    if (caption.Length > 0) output.Append('*').Append(caption).Append("*\n\n");
                    break;
                }

                case "img":
                    FlushInline();
                    output.Append(Image(element)).Append("\n\n");
                    break;

                case "br":
                    pending.Append("  \n");
                    break;

                // Containers that carry blocks rather than being one.
                case "div" or "section" or "article" or "main" or "header" or "footer"
                     or "aside" or "table" or "tbody" or "thead" or "tr":
                    FlushInline();
                    WriteBlocks(element, output);
                    break;

                case "td" or "th":
                {
                    // A cell's text as its own line. Not a pipe table on
                    // purpose: feeds use tables for layout as often as for
                    // data, and a malformed table renders worse than prose.
                    FlushInline();
                    var cell = Inline(element);
                    if (cell.Length > 0) output.Append(cell).Append("\n\n");
                    break;
                }

                // Elements a feed has no business carrying, and that would
                // otherwise contribute their source as text.
                case "script" or "style" or "noscript" or "iframe" or "form":
                    break;

                default:
                    pending.Append(Inline(element));
                    break;
            }
        }

        FlushInline();
    }

    private static void WriteList(IElement list, StringBuilder output, bool ordered, int depth)
    {
        var indent = new string(' ', depth * 2);
        var index = 1;

        foreach (var item in list.Children.Where(c => c.LocalName == "li"))
        {
            var marker = ordered ? $"{index++}. " : "- ";

            // The item's own inline content first, then any nested list, so
            // "a point" and its sub-points do not collapse into one line.
            var text = Inline(item, skipNestedLists: true);
            output.Append(indent).Append(marker).Append(text).Append('\n');

            foreach (var nested in item.Children.Where(c => c.LocalName is "ul" or "ol"))
                WriteList(nested, output, nested.LocalName == "ol", depth + 1);
        }

        output.Append('\n');
    }

    /// <summary>
    /// The inline markdown for an element's contents: emphasis, code, links
    /// and images, with everything else contributing its text.
    /// </summary>
    private static string Inline(INode node, bool skipNestedLists = false)
    {
        var builder = new StringBuilder();
        AppendInline(node, builder, skipNestedLists);
        return Collapse(builder.ToString());
    }

    private static void AppendInline(INode node, StringBuilder output, bool skipNestedLists)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == NodeType.Text)
            {
                output.Append(child.TextContent);
                continue;
            }

            if (child is not IElement element) continue;

            switch (element.LocalName)
            {
                case "a":
                {
                    var text = Inline(element);
                    var href = (element as IHtmlAnchorElement)?.Href;

                    // An anchor with no usable target contributes its text
                    // rather than an empty link, and an anchor wrapping only
                    // an image contributes the image: a link whose label is
                    // "![](...)" renders as neither.
                    if (string.IsNullOrWhiteSpace(href) || text.Length == 0) output.Append(text);
                    else if (text.StartsWith("![", StringComparison.Ordinal)) output.Append(text);
                    else output.Append('[').Append(text).Append("](").Append(href).Append(')');
                    break;
                }

                case "img":
                    output.Append(Image(element));
                    break;

                case "strong" or "b":
                {
                    var text = Inline(element);
                    if (text.Length > 0) output.Append("**").Append(text).Append("**");
                    break;
                }

                case "em" or "i":
                {
                    var text = Inline(element);
                    if (text.Length > 0) output.Append('*').Append(text).Append('*');
                    break;
                }

                case "code":
                {
                    var text = element.TextContent.Trim();
                    if (text.Length > 0) output.Append('`').Append(text).Append('`');
                    break;
                }

                case "br":
                    output.Append("  \n");
                    break;

                case "ul" or "ol" when skipNestedLists:
                    break;

                case "script" or "style" or "noscript":
                    break;

                default:
                    AppendInline(element, output, skipNestedLists);
                    break;
            }
        }
    }

    private static string Image(IElement element)
    {
        var source = (element as IHtmlImageElement)?.Source;
        if (string.IsNullOrWhiteSpace(source)) return string.Empty;

        // The alt text is the publisher's own description and is often the
        // only caption an image gets in a feed, so it is kept rather than
        // emitted as an empty label.
        var alt = Collapse(element.GetAttribute("alt") ?? string.Empty)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);

        return $"![{alt}]({source})";
    }

    /// <summary>
    /// Collapses runs of whitespace to single spaces, the way HTML itself
    /// treats them, while keeping the two-space-newline that means a hard
    /// break in markdown.
    /// </summary>
    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lastWasSpace = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '\n' && i >= 2 && text[i - 1] == ' ' && text[i - 2] == ' ')
            {
                builder.Append('\n');
                lastWasSpace = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Collapses runs of blank lines to one, so a fragment full of empty
    /// paragraphs does not render as a column of whitespace.
    /// </summary>
    private static string Tidy(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var output = new StringBuilder();
        var blanks = 0;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();

            if (trimmed.Length == 0)
            {
                blanks++;
                if (blanks > 1) continue;
                output.Append('\n');
                continue;
            }

            blanks = 0;
            output.Append(trimmed).Append('\n');
        }

        return output.ToString().Trim();
    }
}
