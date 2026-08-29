using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LucidReader.Core.Feeds;

/// <summary>
/// Reads RSS 2.0, RSS 1.0/RDF and Atom with one LINQ-to-XML pass.
///
/// System.ServiceModel.Syndication is deliberately not used: it throws on a
/// malformed pubDate and loses the entire document, it does not surface
/// content:encoded, and it does not read RDF. Failing per item instead of per
/// document is the whole point of this class.
/// </summary>
public sealed partial class FeedParser : IFeedParser
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Rss1 = "http://purl.org/rss/1.0/";

    public bool CanParse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;

        var document = TryLoad(content);
        if (document?.Root is null) return false;

        var root = document.Root.Name;
        return root.LocalName is "rss" or "RDF"
               || (root.LocalName == "feed" && root.Namespace == Atom);
    }

    public ParsedFeed Parse(string content, Uri sourceUri)
    {
        var document = TryLoad(content)
            ?? throw new FeedParseException("The response is not well-formed XML.");

        var root = document.Root
            ?? throw new FeedParseException("The response has no root element.");

        return root.Name.LocalName switch
        {
            "rss" => ParseRss2(root, sourceUri),
            "RDF" => ParseRdf(root, sourceUri),
            "feed" when root.Name.Namespace == Atom => ParseAtom(root, sourceUri),
            _ => throw new FeedParseException(
                $"Unrecognised feed root element <{root.Name.LocalName}>.")
        };
    }

    /// <summary>
    /// Loads strictly, then retries once with undeclared entities (named, like
    /// &amp;nbsp;, or a bare stray &amp;) escaped to something legal. A single
    /// bad ampersand is not a reason to throw away a user's whole feed - and by
    /// far the most common way a real-world feed is broken is a raw "Fish &amp;
    /// Chips" in a title, not a fancy named entity.
    /// </summary>
    private static XDocument? TryLoad(string content)
    {
        try
        {
            return XDocument.Parse(content, LoadOptions.None);
        }
        catch (XmlException)
        {
            try
            {
                return XDocument.Parse(ReplaceUndeclaredEntities(content), LoadOptions.None);
            }
            catch (XmlException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Repairs undeclared entities and bare ampersands, but only in markup -
    /// never inside a CDATA section. Entities are never expanded inside CDATA,
    /// so something like &lt;![CDATA[&amp;reg;]]&gt; is already legal, literal
    /// text; rewriting it would corrupt an article body that a publisher wrote
    /// correctly. The document is split on CDATA boundaries and only the
    /// non-CDATA segments are repaired.
    /// </summary>
    private static string ReplaceUndeclaredEntities(string content)
    {
        var segments = CDataPattern().Split(content);

        // Split with a single capturing group alternates [outside, cdata,
        // outside, cdata, ...]: even indices are markup, odd indices are the
        // CDATA sections (delimiters included), which are left untouched.
        for (var i = 0; i < segments.Length; i += 2)
            segments[i] = EscapeEntities(segments[i]);

        return string.Concat(segments);
    }

    private static string EscapeEntities(string markup) =>
        UndeclaredEntityPattern().Replace(markup, match =>
        {
            var name = match.Groups[1].Value;

            // A bare ampersand with nothing entity-shaped after it - by far the
            // most common way a real feed is broken.
            if (name.Length == 0) return "&amp;";

            return ("&" + name) switch
            {
                "&nbsp;" => "&#160;",
                "&copy;" => "&#169;",
                "&mdash;" => "&#8212;",
                "&ndash;" => "&#8211;",
                "&hellip;" => "&#8230;",
                "&rsquo;" => "&#8217;",
                "&lsquo;" => "&#8216;",
                "&ldquo;" => "&#8220;",
                "&rdquo;" => "&#8221;",
                "&trade;" => "&#8482;",
                "&pound;" => "&#163;",
                "&euro;" => "&#8364;",
                // Anything else undeclared becomes a literal ampersand, which
                // is always valid and never loses the surrounding text.
                _ => "&amp;" + name
            };
        });

    /// <summary>
    /// An ampersand that does not begin a legal XML reference (one of the five
    /// predefines, or a numeric/hex reference): either a bare ampersand, or an
    /// undeclared named entity like &amp;nbsp;, captured without the leading
    /// "&amp;" in group 1 when a name is present.
    /// </summary>
    [GeneratedRegex(@"&(?!(?:amp|lt|gt|quot|apos);|#\d+;|#x[0-9a-fA-F]+;)([a-zA-Z][a-zA-Z0-9]*;)?")]
    private static partial Regex UndeclaredEntityPattern();

    /// <summary>
    /// A CDATA section, delimiters included, captured as a single group so
    /// Regex.Split keeps it (rather than discarding it) alongside the markup
    /// either side of it.
    /// </summary>
    [GeneratedRegex(@"(<!\[CDATA\[.*?\]\]>)", RegexOptions.Singleline)]
    private static partial Regex CDataPattern();

    private static ParsedFeed ParseRss2(XElement root, Uri sourceUri)
    {
        var channel = root.Element("channel")
            ?? throw new FeedParseException("RSS feed has no <channel> element.");

        var (items, skipped) = ParseItems(
            channel.Elements("item"), element => ParseRssItem(element, sourceUri));

        return new ParsedFeed(
            Trimmed(channel.Element("title")?.Value),
            ResolveLink(channel.Element("link")?.Value, sourceUri),
            items,
            skipped);
    }

    private static ParsedFeed ParseRdf(XElement root, Uri sourceUri)
    {
        var channel = root.Element(Rss1 + "channel") ?? root.Element("channel");

        // RDF items are siblings of <channel>, not children of it.
        var itemElements = root.Elements(Rss1 + "item").Concat(root.Elements("item"));
        var (items, skipped) = ParseItems(
            itemElements, element => ParseRssItem(element, sourceUri));

        return new ParsedFeed(
            Trimmed(channel?.Element(Rss1 + "title")?.Value ?? channel?.Element("title")?.Value),
            ResolveLink(
                channel?.Element(Rss1 + "link")?.Value ?? channel?.Element("link")?.Value,
                sourceUri),
            items,
            skipped);
    }

    private static ParsedFeed ParseAtom(XElement root, Uri sourceUri)
    {
        var (items, skipped) = ParseItems(
            root.Elements(Atom + "entry"), element => ParseAtomEntry(element, sourceUri));

        return new ParsedFeed(
            Trimmed(root.Element(Atom + "title")?.Value),
            ResolveLink(AtomLink(root), sourceUri),
            items,
            skipped);
    }

    /// <summary>
    /// Parses each item independently. SkippedItemCount counts only items the
    /// parser could not usefully read, which is precisely:
    ///   1. an item with no guid, no link and no title - nothing the storage
    ///      layer can deduplicate on and nothing worth showing a user, so
    ///      ParseRssItem/ParseAtomEntry throw FeedParseException for it, or
    ///   2. a genuinely unexpected failure while reading the element, caught
    ///      here as a safety net.
    /// It does NOT count an item with an unparseable date: that item is still
    /// added to Items with a null PublishedUtc. An unreadable date costs the
    /// item its sort position, nothing more; the item itself is still worth
    /// showing. One skipped item never costs the caller the rest of the feed.
    /// </summary>
    private static (IReadOnlyList<ParsedItem> Items, int Skipped) ParseItems(
        IEnumerable<XElement> elements,
        Func<XElement, ParsedItem> parse)
    {
        var items = new List<ParsedItem>();
        var skipped = 0;

        foreach (var element in elements)
        {
            try
            {
                items.Add(parse(element));
            }
            catch (Exception)
            {
                skipped++;
            }
        }

        return (items, skipped);
    }

    private static ParsedItem ParseRssItem(XElement element, Uri sourceUri)
    {
        // RSS 1.0 puts its children in the RSS 1.0 namespace; RSS 2.0 uses none.
        string? Child(string name) =>
            element.Element(name)?.Value ?? element.Element(Rss1 + name)?.Value;

        var description = Trimmed(Child("description"));
        var encoded = Trimmed(element.Element(Content + "encoded")?.Value);

        return RequireIdentity(new ParsedItem
        {
            Guid = Trimmed(Child("guid")),
            Link = ResolveLink(Child("link"), sourceUri),
            Title = Trimmed(Child("title")),
            Author = Trimmed(element.Element(Dc + "creator")?.Value ?? Child("author")),
            PublishedUtc = FeedDateParser.TryParse(
                Child("pubDate") ?? element.Element(Dc + "date")?.Value),
            UpdatedUtc = FeedDateParser.TryParse(element.Element(Dc + "date")?.Value),
            Summary = description,
            // content:encoded is the full article when a publisher offers one.
            ContentHtml = encoded ?? description
        });
    }

    private static ParsedItem ParseAtomEntry(XElement element, Uri sourceUri)
    {
        var summary = Trimmed(element.Element(Atom + "summary")?.Value);
        var content = Trimmed(element.Element(Atom + "content")?.Value);

        return RequireIdentity(new ParsedItem
        {
            Guid = Trimmed(element.Element(Atom + "id")?.Value),
            Link = ResolveLink(AtomLink(element), sourceUri),
            Title = Trimmed(element.Element(Atom + "title")?.Value),
            Author = Trimmed(element.Element(Atom + "author")?.Element(Atom + "name")?.Value),
            PublishedUtc = FeedDateParser.TryParse(element.Element(Atom + "published")?.Value)
                           ?? FeedDateParser.TryParse(element.Element(Atom + "updated")?.Value),
            UpdatedUtc = FeedDateParser.TryParse(element.Element(Atom + "updated")?.Value),
            Summary = summary,
            ContentHtml = content ?? summary
        });
    }

    /// <summary>
    /// An item with no guid, no link and no title has no usable identity: the
    /// storage layer cannot deduplicate it and there is nothing to show a
    /// user. Throwing here (rather than returning it) is what makes it count
    /// toward SkippedItemCount instead of silently becoming an empty row.
    /// </summary>
    private static ParsedItem RequireIdentity(ParsedItem item) =>
        item.Guid is null && item.Link is null && item.Title is null
            ? throw new FeedParseException(
                "Item has no guid, link or title and cannot be identified or shown.")
            : item;

    /// <summary>
    /// The alternate link, or the first link with no rel, which is what most
    /// Atom feeds actually emit.
    /// </summary>
    private static string? AtomLink(XElement element)
    {
        var links = element.Elements(Atom + "link").ToList();
        var alternate = links.FirstOrDefault(link =>
            (string?)link.Attribute("rel") == "alternate");
        var bare = links.FirstOrDefault(link => link.Attribute("rel") is null);
        return (string?)(alternate ?? bare)?.Attribute("href");
    }

    private static string? ResolveLink(string? value, Uri sourceUri)
    {
        var trimmed = Trimmed(value);
        if (trimmed is null) return null;

        return Uri.TryCreate(sourceUri, trimmed, out var absolute)
            ? absolute.ToString()
            : trimmed;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
