using System.Xml.Linq;

namespace LucidReader.Core.Opml;

public static class OpmlReader
{
    // A guard against pathological or malicious nesting: without a limit,
    // recursive descent risks a StackOverflowException, which cannot be
    // caught and takes the whole process down rather than failing the
    // import cleanly. No real export nests anywhere near this deep.
    private const int MaxDepth = 100;

    public static IReadOnlyList<OpmlOutline> Parse(string opml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(opml);
        }
        catch (Exception ex)
        {
            throw new OpmlParseException("The file is not well-formed XML.", ex);
        }

        var root = document.Root
            ?? throw new OpmlParseException("The file has no root element.");

        if (!string.Equals(root.Name.LocalName, "opml", StringComparison.OrdinalIgnoreCase))
            throw new OpmlParseException(
                $"Expected an <opml> root element but found <{root.Name.LocalName}>.");

        var body = root.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, "body", StringComparison.OrdinalIgnoreCase))
            ?? throw new OpmlParseException("The OPML file has no <body> element.");

        return ReadOutlines(body);
    }

    private static IReadOnlyList<OpmlOutline> ReadOutlines(XElement parent, int depth = 0)
    {
        if (depth > MaxDepth)
            throw new OpmlParseException(
                $"The OPML file is nested more than {MaxDepth} levels deep.");

        var results = new List<OpmlOutline>();

        foreach (var element in parent.Elements().Where(e =>
                     string.Equals(e.Name.LocalName, "outline", StringComparison.OrdinalIgnoreCase)))
        {
            // Exporters disagree about which attribute holds the label.
            var title = Attribute(element, "text")
                        ?? Attribute(element, "title")
                        ?? Attribute(element, "xmlUrl")
                        ?? "Untitled";

            // The type attribute is often missing or wrong. The presence of an
            // xmlUrl is what actually decides whether this is a subscription.
            var feedUrl = Attribute(element, "xmlUrl");
            var siteUrl = Attribute(element, "htmlUrl");

            results.Add(new OpmlOutline(title, feedUrl, siteUrl, ReadOutlines(element, depth + 1)));
        }

        return results;
    }

    private static string? Attribute(XElement element, string name)
    {
        var attribute = element.Attributes().FirstOrDefault(a =>
            string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
        var value = attribute?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
