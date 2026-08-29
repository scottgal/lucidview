using System.Text;
using LucidReader.Core.Opml;
using Xunit;

namespace LucidReader.Core.Tests.Opml;

public class OpmlReaderTests
{
    private static string Opml(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Opml", name));

    private static string DeeplyNestedOpml(int depth)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?><opml version=\"2.0\">")
            .Append("<head><title>Deep</title></head><body>");
        for (var i = 0; i < depth; i++)
            builder.Append("<outline text=\"L").Append(i).Append("\">");
        builder.Append("<outline text=\"Leaf\" xmlUrl=\"https://leaf.example/feed.xml\"/>");
        for (var i = 0; i < depth; i++)
            builder.Append("</outline>");
        builder.Append("</body></opml>");
        return builder.ToString();
    }

    [Fact]
    public void A_flat_export_yields_one_outline_per_feed()
    {
        var outlines = OpmlReader.Parse(Opml("flat.opml"));

        Assert.Equal(2, outlines.Count);
        Assert.Equal("Example Blog", outlines[0].Title);
        Assert.Equal("https://example.com/feed.xml", outlines[0].FeedUrl);
        Assert.Equal("https://example.com/", outlines[0].SiteUrl);
        Assert.Empty(outlines[0].Children);
    }

    [Fact]
    public void A_foldered_export_preserves_the_structure()
    {
        var outlines = OpmlReader.Parse(Opml("foldered.opml"));

        Assert.Equal(3, outlines.Count);

        var news = outlines[0];
        Assert.Equal("News", news.Title);
        Assert.Null(news.FeedUrl);
        Assert.Equal(2, news.Children.Count);

        var loose = outlines[2];
        Assert.Equal("Loose feed", loose.Title);
        Assert.NotNull(loose.FeedUrl);
    }

    [Fact]
    public void A_title_attribute_is_accepted_when_text_is_missing()
    {
        var outlines = OpmlReader.Parse(Opml("awkward.opml"));

        Assert.Equal("Titled not texted", outlines[0].Title);
    }

    [Fact]
    public void A_missing_type_attribute_does_not_disqualify_a_feed()
    {
        var outlines = OpmlReader.Parse(Opml("awkward.opml"));

        Assert.Equal("https://a.example/feed.xml", outlines[0].FeedUrl);
    }

    [Fact]
    public void Nesting_deeper_than_one_level_is_preserved_by_the_reader()
    {
        var outlines = OpmlReader.Parse(Opml("awkward.opml"));

        var outer = outlines.Single(o => o.Title == "Outer");
        var inner = Assert.Single(outer.Children);
        Assert.Equal("Inner", inner.Title);
        Assert.Equal("Deep feed", Assert.Single(inner.Children).Title);
    }

    [Fact]
    public void A_feed_outline_with_children_keeps_both_the_feed_and_its_children()
    {
        var outlines = OpmlReader.Parse(Opml("nested-feed.opml"));

        var parent = Assert.Single(outlines);
        Assert.Equal("https://parent.example/feed.xml", parent.FeedUrl);
        var child = Assert.Single(parent.Children);
        Assert.Equal("https://child.example/feed.xml", child.FeedUrl);
    }

    [Fact]
    public void Nesting_deeper_than_the_depth_limit_is_rejected()
    {
        var opml = DeeplyNestedOpml(150);

        Assert.Throws<OpmlParseException>(() => OpmlReader.Parse(opml));
    }

    [Fact]
    public void An_outline_with_neither_a_feed_nor_children_is_still_returned()
    {
        var outlines = OpmlReader.Parse(Opml("awkward.opml"));

        var empty = outlines.Single(o => o.Title == "Empty container");
        Assert.Null(empty.FeedUrl);
        Assert.Empty(empty.Children);
    }

    [Fact]
    public void A_document_that_is_not_opml_is_rejected()
    {
        Assert.Throws<OpmlParseException>(() => OpmlReader.Parse(Opml("not-opml.xml")));
    }

    [Fact]
    public void Malformed_xml_is_rejected_with_a_clear_exception()
    {
        Assert.Throws<OpmlParseException>(() => OpmlReader.Parse("<opml><body>"));
    }

    [Fact]
    public void Writing_then_reading_round_trips()
    {
        var original = OpmlReader.Parse(Opml("foldered.opml"));

        var written = OpmlWriter.Write(original, "Subscriptions",
            DateTimeOffset.Parse("2026-08-29T10:00:00Z"));
        var reread = OpmlReader.Parse(written);

        Assert.Equal(original.Count, reread.Count);
        Assert.Equal(original[0].Title, reread[0].Title);
        Assert.Equal(original[0].Children.Count, reread[0].Children.Count);
        Assert.Equal(original[2].FeedUrl, reread[2].FeedUrl);
    }

    [Fact]
    public void Written_opml_escapes_characters_that_would_break_the_document()
    {
        var outlines = new List<OpmlOutline>
        {
            new("Ampersands & \"quotes\" & <angles>", "https://x.example/feed.xml?a=1&b=2", null, [])
        };

        var written = OpmlWriter.Write(outlines, "T", DateTimeOffset.Parse("2026-08-29T10:00:00Z"));

        var reread = OpmlReader.Parse(written);
        Assert.Equal("Ampersands & \"quotes\" & <angles>", reread[0].Title);
        Assert.Equal("https://x.example/feed.xml?a=1&b=2", reread[0].FeedUrl);
    }
}
