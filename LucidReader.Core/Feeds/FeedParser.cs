using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using LucidReader.Core.Model;

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

    /// <summary>
    /// Media RSS, which is how most publishers that put a picture in the feed
    /// actually do it. BBC News uses media:thumbnail on every item and Ars
    /// Technica uses media:content and media:thumbnail on every item.
    /// </summary>
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";

    /// <summary>
    /// Podcast feeds carry itunes:image, and enough general-interest feeds
    /// emit it as a per-item picture that it is worth reading as a last
    /// resort before giving up.
    /// </summary>
    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

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
            skipped,
            RssChannelIcon(channel, sourceUri));
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
            skipped,
            channel is null ? null : RssChannelIcon(channel, sourceUri));
    }

    private static ParsedFeed ParseAtom(XElement root, Uri sourceUri)
    {
        var (items, skipped) = ParseItems(
            root.Elements(Atom + "entry"), element => ParseAtomEntry(element, sourceUri));

        return new ParsedFeed(
            Trimmed(root.Element(Atom + "title")?.Value),
            ResolveLink(AtomLink(root), sourceUri),
            items,
            skipped,
            // icon before logo: RFC 4287 says icon is the small one meant to be
            // shown beside the feed's name, which is exactly the sidebar's use,
            // where logo is a wide banner that would be cropped to nothing.
            ResolveLink(
                Trimmed(root.Element(Atom + "icon")?.Value)
                ?? Trimmed(root.Element(Atom + "logo")?.Value),
                sourceUri));
    }

    /// <summary>
    /// An RSS channel's own image, from &lt;image&gt;&lt;url&gt;. Handles the
    /// RSS 1.0 spelling as well, where the elements live in the RSS 1.0
    /// namespace and the image is a sibling of the channel referenced by
    /// rdf:resource - the url element is read wherever it sits rather than
    /// following the reference, which is enough for the one string wanted here.
    /// </summary>
    private static string? RssChannelIcon(XElement channel, Uri sourceUri)
    {
        var image = channel.Element("image") ?? channel.Element(Rss1 + "image");

        // RSS 1.0 keeps <image> beside <channel> rather than inside it.
        if (image is null && channel.Parent is { } root)
            image = root.Element(Rss1 + "image") ?? root.Element("image");

        if (image is null) return null;

        var url = Trimmed(image.Element("url")?.Value ?? image.Element(Rss1 + "url")?.Value);
        return ResolveLink(url, sourceUri);
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
            ContentHtml = encoded ?? description,
            Categories = NormaliseCategories(
                element.Elements("category")
                    .Concat(element.Elements(Rss1 + "category"))
                    .Select(category => category.Value)),
            // The encoded body first, since a full article's own markup is a
            // better source of a lead picture than a summary that may be one
            // sentence of plain text.
            ImageUrl = ExtractImageUrl(element, encoded ?? description, sourceUri)
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
            ContentHtml = content ?? summary,
            // "term" is the machine-readable name and is required by RFC 4287;
            // "label" is the optional human-readable spelling of the same
            // thing. term first, label only as a fallback, so a feed that
            // emits both does not produce two tags for one category.
            Categories = NormaliseCategories(
                element.Elements(Atom + "category")
                    .Select(category =>
                        (string?)category.Attribute("term")
                        ?? (string?)category.Attribute("label"))),
            // Atom has no enclosure element: it spells the same thing as a
            // link with rel="enclosure", so that is checked here in addition
            // to everything ExtractImageUrl looks at, which is otherwise
            // namespace-agnostic and applies to both formats.
            ImageUrl = AtomEnclosureImage(element, sourceUri)
                       ?? ExtractImageUrl(element, content ?? summary, sourceUri)
        });
    }

    /// <summary>
    /// Turns whatever the publisher wrote in its category elements into names
    /// the tag store will take.
    ///
    /// Every candidate goes through <see cref="TagName.TryNormalise"/>, which
    /// is the one place the trimming, the whitespace collapsing, the comma and
    /// control-character rules and the length limit are decided and tested. A
    /// category that normalises to nothing - an empty element, a run of
    /// whitespace - is dropped rather than stored as a blank tag, and so is
    /// one the rules refuse for a stated reason: a feed is not a person who
    /// can be told why, and a publisher's over-long or comma-bearing category
    /// is not worth failing the whole item's import over.
    ///
    /// De-duplicated case-insensitively, through TagName.AreSame, because
    /// that is the identity the tag store uses: a feed emitting both "dotnet"
    /// and "DotNet" on one item means one tag, and asking the store to add it
    /// twice would be two writes for one row.
    /// </summary>
    private static IReadOnlyList<string> NormaliseCategories(IEnumerable<string?> raw)
    {
        List<string>? names = null;

        foreach (var candidate in raw)
        {
            if (!TagName.TryNormalise(candidate, out var name, out _)) continue;

            names ??= [];
            if (names.Any(existing => TagName.AreSame(existing, name))) continue;
            names.Add(name);
        }

        return names ?? (IReadOnlyList<string>)[];
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

    /// <summary>
    /// An Atom entry's enclosure image, which is a link element rather than an
    /// element of its own: RFC 4287 has no enclosure tag and says the
    /// relationship instead.
    /// </summary>
    private static string? AtomEnclosureImage(XElement element, Uri sourceUri)
    {
        foreach (var link in element.Elements(Atom + "link"))
        {
            var relation = Trimmed((string?)link.Attribute("rel"));
            if (relation?.Equals("enclosure", StringComparison.OrdinalIgnoreCase) != true) continue;

            var type = Trimmed((string?)link.Attribute("type"));
            if (type?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true) continue;

            var href = Trimmed((string?)link.Attribute("href"));
            if (href is not null) return Allowed(href, sourceUri);
        }

        return null;
    }

    /// <summary>
    /// The picture a feed names for one item, or null.
    ///
    /// Ordered cheapest and most reliable first, and every source below was
    /// chosen because a real feed in mylo's own starter set uses it:
    ///
    ///   1. media:content, when it is an image. The full-size picture, and the
    ///      one Ars Technica supplies.
    ///   2. media:thumbnail. A smaller crop, and what BBC News supplies. It
    ///      comes second because where a feed offers both, the larger one is
    ///      the better lead image.
    ///   3. enclosure with an image type. The RSS 2.0 way, still common.
    ///   4. itunes:image, whose URL lives in an href rather than in the text.
    ///   5. The first img in the item's own HTML, which costs a scan of a
    ///      string already in memory and rescues feeds that name no image
    ///      anywhere in their metadata.
    ///
    /// media:group is searched as well as the item itself, because Media RSS
    /// lets a publisher wrap several renditions of the same picture in one and
    /// an item that does so has no media:content child of its own.
    ///
    /// Every candidate goes through FeedUrlPolicy. This is an address that
    /// arrived in remote content and will be fetched unattended by the image
    /// cache, which is exactly the case that gate exists for, and a feed
    /// naming a loopback or link-local address must not turn into a request.
    /// </summary>
    private static string? ExtractImageUrl(XElement element, string? html, Uri sourceUri)
    {
        var containers = new[] { element }.Concat(element.Elements(Media + "group"));

        foreach (var container in containers)
        {
            var fromMedia =
                FirstImageAttribute(container.Elements(Media + "content"), "url", RequireImageType)
                ?? FirstImageAttribute(container.Elements(Media + "thumbnail"), "url", _ => true);

            if (fromMedia is not null) return Allowed(fromMedia, sourceUri);
        }

        var fromEnclosure = FirstImageAttribute(
            element.Elements("enclosure").Concat(element.Elements(Rss1 + "enclosure")),
            "url",
            RequireImageType);

        if (fromEnclosure is not null) return Allowed(fromEnclosure, sourceUri);

        var fromItunes = Trimmed((string?)element.Element(Itunes + "image")?.Attribute("href"));
        if (fromItunes is not null) return Allowed(fromItunes, sourceUri);

        var fromHtml = FirstHtmlImage(html);
        return fromHtml is null ? null : Allowed(fromHtml, sourceUri);

        // "image/png" and friends, and also a bare medium="image", which is
        // how a publisher says the same thing without committing to a MIME
        // type. An element carrying neither is not assumed to be a picture:
        // media:content is also how a feed attaches audio and video, and
        // recording an MP3 as an item's image would put a broken image in the
        // list instead of no image at all.
        static bool RequireImageType(XElement candidate)
        {
            var type = Trimmed((string?)candidate.Attribute("type"));
            var medium = Trimmed((string?)candidate.Attribute("medium"));

            return medium?.Equals("image", StringComparison.OrdinalIgnoreCase) == true
                   || type?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
        }

        static string? FirstImageAttribute(
            IEnumerable<XElement> candidates, string attribute, Func<XElement, bool> accept)
        {
            foreach (var candidate in candidates)
            {
                if (!accept(candidate)) continue;

                var value = Trimmed((string?)candidate.Attribute(attribute));
                if (value is not null) return value;
            }

            return null;
        }
    }

    /// <summary>
    /// Resolves a candidate image address against the feed's own URL and then
    /// puts it through the policy gate, returning null when it does not pass.
    /// </summary>
    private static string? Allowed(string? value, Uri sourceUri)
    {
        var resolved = ResolveLink(value, sourceUri);

        return FeedUrlPolicy.TryValidate(resolved, out var uri, out _) && uri is not null
            ? uri.ToString()
            : null;
    }

    /// <summary>
    /// The src of the first img element in a fragment of feed HTML.
    ///
    /// A regex rather than a parse, deliberately. This runs on every item of
    /// every refresh, the answer is a nicety rather than something correctness
    /// depends on, and the alternative is handing every description in every
    /// feed to a full HTML parser to retrieve one attribute. A fragment this
    /// misreads yields no image, which is exactly what the caller had before
    /// this method existed.
    /// </summary>
    private static string? FirstHtmlImage(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var match = HtmlImagePattern().Match(html);
        return match.Success ? Trimmed(match.Groups["src"].Value) : null;
    }

    [GeneratedRegex(
        """<img\b[^>]*?\bsrc\s*=\s*(?:"(?<src>[^"]*)"|'(?<src>[^']*)'|(?<src>[^\s>]+))""",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex HtmlImagePattern();

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
