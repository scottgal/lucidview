using System.Xml.Linq;

namespace LucidReader.Core.Opml;

public static class OpmlWriter
{
    public static string Write(
        IReadOnlyList<OpmlOutline> outlines,
        string title,
        DateTimeOffset nowUtc)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("opml",
                new XAttribute("version", "2.0"),
                new XElement("head",
                    new XElement("title", title),
                    new XElement("dateCreated", nowUtc.ToUniversalTime().ToString("r"))),
                new XElement("body", outlines.Select(ToElement))));

        // XDocument handles escaping, which is why the writer builds a tree
        // rather than concatenating strings: a feed URL with a query string
        // containing an ampersand is normal and must not corrupt the document.
        return document.Declaration + Environment.NewLine + document;
    }

    private static XElement ToElement(OpmlOutline outline)
    {
        var element = new XElement("outline", new XAttribute("text", outline.Title));

        if (outline.FeedUrl is { } feedUrl)
        {
            element.Add(new XAttribute("type", "rss"));
            element.Add(new XAttribute("xmlUrl", feedUrl));
            if (outline.SiteUrl is { } siteUrl)
                element.Add(new XAttribute("htmlUrl", siteUrl));
        }

        foreach (var child in outline.Children)
            element.Add(ToElement(child));

        return element;
    }
}
