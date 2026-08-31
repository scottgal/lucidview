using System.Net;
using System.Text;

namespace LucidReader.Core.Feeds;

/// <summary>
/// One element in a scanned page: its tag, the attributes worth keeping, its
/// element children in document order, and the text that sits under it.
///
/// Deliberately not a DOM. Nothing here models namespaces, entities in
/// content beyond a single decode pass, CSS, or any of the recovery rules a
/// browser applies to broken markup. It models the two questions
/// <see cref="ArticleListDetector"/> asks and nothing else: "what are this
/// element's children, in order" and "what text and links are under it".
/// </summary>
internal sealed class HtmlElement
{
    public required string Tag { get; init; }

    /// <summary>
    /// Only the attributes the detector reads. Keeping every attribute of
    /// every element on a 300KB page is a lot of strings to hold for values
    /// nothing looks at, and the set below is closed and small.
    /// </summary>
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<HtmlElement> Children { get; } = [];

    public HtmlElement? Parent { get; set; }

    /// <summary>
    /// Text written directly inside this element, before or between its
    /// children. Descendant text lives on the descendants;
    /// <see cref="AppendText"/> on the walk in <see cref="TextContent"/> is
    /// what puts the two back together.
    /// </summary>
    public StringBuilder OwnText { get; } = new();

    public string? Attribute(string name) =>
        Attributes.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// The class attribute split into tokens, lower-cased and sorted, joined
    /// by a space. This is the "shape" half of a sibling's signature: two
    /// rows of a list written by the same template carry the same classes in
    /// whatever order the template emitted them, and sorting means an ordering
    /// difference does not split one group into two.
    /// </summary>
    public string ClassSignature
    {
        get
        {
            if (_classSignature is not null) return _classSignature;

            var raw = Attribute("class");
            if (string.IsNullOrWhiteSpace(raw)) return _classSignature = string.Empty;

            var tokens = raw
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);

            return _classSignature = string.Join(' ', tokens);
        }
    }

    private string? _classSignature;
    private string? _textContent;

    /// <summary>
    /// All the text under this element, collapsed to single spaces. Cached,
    /// because the detector reads it repeatedly for the same element while
    /// scoring, and computing it walks the whole subtree each time.
    /// </summary>
    public string TextContent
    {
        get
        {
            if (_textContent is not null) return _textContent;

            var builder = new StringBuilder();
            Collect(this, builder);
            return _textContent = CollapseWhitespace(builder.ToString());
        }
    }

    private static void Collect(HtmlElement element, StringBuilder builder)
    {
        builder.Append(element.OwnText);
        foreach (var child in element.Children)
        {
            Collect(child, builder);
            builder.Append(' ');
        }
    }

    public IEnumerable<HtmlElement> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var deeper in child.Descendants()) yield return deeper;
        }
    }

    /// <summary>
    /// The element that follows this one under the same parent, or null when
    /// this is the last child. Hacker News is why this exists: the story link
    /// and the story's age live in two sibling table rows, so the date for a
    /// candidate is not always inside the candidate.
    /// </summary>
    public HtmlElement? NextSibling
    {
        get
        {
            if (Parent is null) return null;
            var index = Parent.Children.IndexOf(this);
            return index >= 0 && index + 1 < Parent.Children.Count
                ? Parent.Children[index + 1]
                : null;
        }
    }

    internal static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}

/// <summary>
/// Turns a page of HTML into an element tree good enough to ask structural
/// questions of, without taking an HTML parser as a dependency.
///
/// FeedAutodiscovery makes the case for the regex it uses: it reads link
/// elements out of a head, and a parser would be heavier than the job. This
/// class exists because the article-list detector cannot make that same case.
/// Its central question is "are these elements siblings of the same shape",
/// which is a question about tree structure, and no amount of regex over flat
/// text answers it.
///
/// What this deliberately does NOT do, because the detector does not need it:
/// no character-reference handling beyond one WebUtility.HtmlDecode pass per
/// text run and attribute value, no foster parenting, no adoption agency
/// algorithm, no template or foreign-content rules, no attribute retained
/// beyond the small set in KeptAttributes. Markup a browser would recover from
/// differently simply produces a slightly different tree here, and the worst
/// consequence of that is a candidate group that scores lower than it should,
/// which costs the user a manual paste of a feed URL.
/// </summary>
internal static class HtmlOutline
{
    /// <summary>
    /// The largest page this will scan. Callers already bound their downloads
    /// (FeedAutodiscovery caps a fetch at 8MB), so this is the second, cheaper
    /// bound: parsing is linear but the tree is not free, and a page an order
    /// of magnitude past any real article index is one to decline rather than
    /// to spend memory on.
    /// </summary>
    public const int MaxHtmlLength = 4 * 1024 * 1024;

    /// <summary>
    /// The largest tree this will build. A page can be within
    /// MaxHtmlLength and still be pathological (deeply nested generated
    /// markup), so the node count is bounded too. Reaching it stops the scan
    /// and returns what was built, which is a partial tree the detector reads
    /// exactly as it reads any other.
    /// </summary>
    public const int MaxElements = 120_000;

    /// <summary>
    /// How deep the open-element stack is allowed to get. Beyond this the
    /// scan keeps reading text but stops nesting, so a page built of ten
    /// thousand unclosed divs cannot turn into a ten-thousand-deep recursion
    /// in TextContent.
    /// </summary>
    private const int MaxDepth = 200;

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr"
    };

    /// <summary>
    /// Elements whose content is raw text rather than markup. Everything up to
    /// the matching close tag is skipped: a "&lt;/div&gt;" inside a script
    /// string would otherwise close a real element.
    /// </summary>
    private static readonly HashSet<string> RawTextElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "textarea", "title", "noscript"
    };

    /// <summary>
    /// The attributes kept on every element. href and datetime are read
    /// directly; class drives grouping; title and aria-label carry a date on
    /// sites that put the machine-readable form there (Hacker News does);
    /// id is kept because it is occasionally the only thing distinguishing
    /// otherwise identical rows.
    /// </summary>
    private static readonly string[] KeptAttributes =
        ["href", "class", "datetime", "title", "id", "aria-label", "rel", "content", "property", "name"];

    /// <summary>
    /// Opening one of these implies the end of any currently-open element
    /// listed against it. Without this a table of rows with no closing
    /// &lt;/tr&gt; - which is legal HTML and which real sites emit - comes out
    /// as one row nested inside another rather than as a run of siblings, and
    /// the whole repetition signal disappears.
    /// </summary>
    private static readonly Dictionary<string, string[]> ImpliedEnds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["li"] = ["li"],
            ["p"] = ["p"],
            ["dt"] = ["dt", "dd"],
            ["dd"] = ["dt", "dd"],
            ["option"] = ["option"],
            ["td"] = ["td", "th"],
            ["th"] = ["td", "th"],
            ["tr"] = ["tr", "td", "th"],
            ["thead"] = ["thead", "tbody", "tfoot", "tr", "td", "th"],
            ["tbody"] = ["thead", "tbody", "tfoot", "tr", "td", "th"],
            ["tfoot"] = ["thead", "tbody", "tfoot", "tr", "td", "th"]
        };

    public static HtmlElement Parse(string html)
    {
        var root = new HtmlElement { Tag = "#document" };
        if (string.IsNullOrEmpty(html) || html.Length > MaxHtmlLength) return root;

        var open = new List<HtmlElement> { root };
        var elementCount = 0;
        var index = 0;

        while (index < html.Length)
        {
            var lessThan = html.IndexOf('<', index);
            if (lessThan < 0)
            {
                AppendText(open[^1], html, index, html.Length);
                break;
            }

            if (lessThan > index) AppendText(open[^1], html, index, lessThan);

            // Comments, doctypes, CDATA and processing instructions carry
            // nothing the detector reads, so each is skipped whole rather
            // than being turned into a node.
            if (Matches(html, lessThan, "<!--"))
            {
                var end = html.IndexOf("-->", lessThan + 4, StringComparison.Ordinal);
                index = end < 0 ? html.Length : end + 3;
                continue;
            }

            if (lessThan + 1 < html.Length && (html[lessThan + 1] == '!' || html[lessThan + 1] == '?'))
            {
                var end = html.IndexOf('>', lessThan + 1);
                index = end < 0 ? html.Length : end + 1;
                continue;
            }

            if (Matches(html, lessThan, "</"))
            {
                var end = html.IndexOf('>', lessThan + 2);
                if (end < 0) break;

                var name = ReadTagName(html, lessThan + 2, end);
                if (name.Length > 0) CloseTag(open, name);
                index = end + 1;
                continue;
            }

            if (lessThan + 1 >= html.Length || !char.IsAsciiLetter(html[lessThan + 1]))
            {
                // A bare "<" in text, e.g. "a < b". Not a tag; keep it as text.
                AppendText(open[^1], html, lessThan, lessThan + 1);
                index = lessThan + 1;
                continue;
            }

            var tagEnd = FindTagEnd(html, lessThan);
            if (tagEnd < 0) break;

            var tagName = ReadTagName(html, lessThan + 1, tagEnd);
            if (tagName.Length == 0)
            {
                index = tagEnd + 1;
                continue;
            }

            var selfClosing = IsSelfClosing(html, lessThan, tagEnd);

            if (RawTextElements.Contains(tagName))
            {
                index = SkipRawText(html, tagEnd + 1, tagName);
                continue;
            }

            if (ImpliedEnds.TryGetValue(tagName, out var closes)) CloseImplied(open, closes);

            if (elementCount >= MaxElements) return root;
            elementCount++;

            var element = new HtmlElement { Tag = tagName };
            ReadAttributes(html, lessThan, tagEnd, element);

            var parent = open[^1];
            element.Parent = parent;
            parent.Children.Add(element);

            if (!selfClosing && !VoidElements.Contains(tagName) && open.Count < MaxDepth)
                open.Add(element);

            index = tagEnd + 1;
        }

        return root;
    }

    /// <summary>
    /// Finds the '&gt;' that ends a start tag, stepping over any that appear
    /// inside a quoted attribute value. A title or an aria-label containing an
    /// angle bracket is common enough that treating the first '&gt;' as the
    /// tag end truncates the tag and loses its remaining attributes.
    /// </summary>
    private static int FindTagEnd(string html, int start)
    {
        var quote = '\0';
        for (var i = start + 1; i < html.Length; i++)
        {
            var c = html[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }

            if (c is '"' or '\'') quote = c;
            else if (c == '>') return i;
        }

        return -1;
    }

    private static int SkipRawText(string html, int from, string tagName)
    {
        var closing = "</" + tagName;
        var at = html.IndexOf(closing, from, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return html.Length;

        var end = html.IndexOf('>', at);
        return end < 0 ? html.Length : end + 1;
    }

    private static void CloseTag(List<HtmlElement> open, string name)
    {
        for (var i = open.Count - 1; i >= 1; i--)
        {
            if (!open[i].Tag.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            open.RemoveRange(i, open.Count - i);
            return;
        }

        // A close tag for something that was never opened. Ignored: acting on
        // it would close an unrelated element and scramble the tree.
    }

    private static void CloseImplied(List<HtmlElement> open, string[] closes)
    {
        while (open.Count > 1
               && closes.Contains(open[^1].Tag, StringComparer.OrdinalIgnoreCase))
            open.RemoveAt(open.Count - 1);
    }

    private static void AppendText(HtmlElement target, string html, int from, int to)
    {
        if (to <= from) return;

        var slice = html.AsSpan(from, to - from);
        if (slice.IsWhiteSpace())
        {
            target.OwnText.Append(' ');
            return;
        }

        var text = slice.ToString();
        target.OwnText.Append(text.Contains('&') ? WebUtility.HtmlDecode(text) : text);
        target.OwnText.Append(' ');
    }

    /// <summary>
    /// Whether a start tag closed itself, deciding it the way the HTML tokeniser
    /// does rather than by looking at the last character.
    ///
    /// A trailing slash only closes a tag when the tokeniser is between
    /// attributes when it reaches it. Inside an unquoted attribute value a slash
    /// is an ordinary character, so "&lt;a href=https://danluu.com/bug-blind/&gt;"
    /// is an ordinary open tag and not a void one. Reading the last character
    /// instead made every such anchor childless, which moved its title text onto
    /// the enclosing list item and left the detector with a run of links that
    /// had no text at all. Sites that write unquoted hrefs are exactly the plain
    /// hand-rolled index pages this detector exists for.
    /// </summary>
    private static bool IsSelfClosing(string html, int tagStart, int tagEnd)
    {
        if (tagEnd <= tagStart || html[tagEnd - 1] != '/') return false;

        var i = tagStart + 1;
        while (i < tagEnd && !char.IsWhiteSpace(html[i]) && html[i] != '/') i++;

        while (i < tagEnd)
        {
            while (i < tagEnd && (char.IsWhiteSpace(html[i]) || html[i] == '/')) i++;
            if (i >= tagEnd) return true;

            while (i < tagEnd && !char.IsWhiteSpace(html[i]) && html[i] != '=' && html[i] != '/') i++;
            while (i < tagEnd && char.IsWhiteSpace(html[i])) i++;
            if (i >= tagEnd || html[i] != '=') continue;

            i++;
            while (i < tagEnd && char.IsWhiteSpace(html[i])) i++;
            if (i < tagEnd && (html[i] == '"' || html[i] == '\''))
            {
                var quote = html[i++];
                while (i < tagEnd && html[i] != quote) i++;
                if (i < tagEnd) i++;
                continue;
            }

            while (i < tagEnd && !char.IsWhiteSpace(html[i]) && html[i] != '>') i++;

            // The unquoted value ran to the end of the tag, so the trailing
            // slash is the last character of the value, not a tag terminator.
            if (i >= tagEnd) return false;
        }

        return true;
    }

    private static string ReadTagName(string html, int from, int limit)
    {
        var i = from;
        while (i < limit && (char.IsAsciiLetterOrDigit(html[i]) || html[i] is '-' or ':')) i++;
        return i > from ? html[from..i].ToLowerInvariant() : string.Empty;
    }

    private static void ReadAttributes(string html, int tagStart, int tagEnd, HtmlElement element)
    {
        var i = tagStart + 1;
        while (i < tagEnd && !char.IsWhiteSpace(html[i])) i++;

        while (i < tagEnd)
        {
            while (i < tagEnd && (char.IsWhiteSpace(html[i]) || html[i] == '/')) i++;
            if (i >= tagEnd) return;

            var nameStart = i;
            while (i < tagEnd && !char.IsWhiteSpace(html[i]) && html[i] != '=' && html[i] != '/') i++;
            if (i == nameStart) return;

            var name = html[nameStart..i];

            while (i < tagEnd && char.IsWhiteSpace(html[i])) i++;

            var value = string.Empty;
            if (i < tagEnd && html[i] == '=')
            {
                i++;
                while (i < tagEnd && char.IsWhiteSpace(html[i])) i++;
                if (i < tagEnd && (html[i] == '"' || html[i] == '\''))
                {
                    var quote = html[i++];
                    var valueStart = i;
                    while (i < tagEnd && html[i] != quote) i++;
                    value = html[valueStart..i];
                    if (i < tagEnd) i++;
                }
                else
                {
                    var valueStart = i;
                    while (i < tagEnd && !char.IsWhiteSpace(html[i]) && html[i] != '>') i++;
                    value = html[valueStart..i];
                }
            }

            if (!KeptAttributes.Contains(name, StringComparer.OrdinalIgnoreCase)
                && !IsDateDataAttribute(name)) continue;

            element.Attributes[name] = value.Contains('&')
                ? WebUtility.HtmlDecode(value).Trim()
                : value.Trim();
        }
    }

    /// <summary>
    /// Keeps data attributes that name a date or a time, whatever the site
    /// calls them: data-published-date, data-date, data-timestamp. There is no
    /// standard for these, but a site that puts a machine-readable date on its
    /// list rows almost always puts it in one, and a machine-readable date is
    /// worth far more to the detector than the rendered string next to it.
    /// The name test is what keeps this from becoming "retain every attribute
    /// on the page".
    /// </summary>
    private static bool IsDateDataAttribute(string name) =>
        name.StartsWith("data-", StringComparison.OrdinalIgnoreCase)
        && (name.Contains("date", StringComparison.OrdinalIgnoreCase)
            || name.Contains("time", StringComparison.OrdinalIgnoreCase));

    private static bool Matches(string html, int at, string token) =>
        at + token.Length <= html.Length
        && html.AsSpan(at, token.Length).SequenceEqual(token);
}
