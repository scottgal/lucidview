using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// The image a feed names for an item.
///
/// Every shape asserted here was taken from a feed mylo actually subscribes to
/// by default. Before this existed the parser read none of them: an item's
/// picture could only come from OfflineDownloader scraping og:image off the
/// article page, so 53 of the 93 items in a fresh profile had an image URL
/// sitting in the XML that nothing ever looked at. BBC News names one on every
/// item through media:thumbnail and Ars Technica names one on every item
/// through media:content.
/// </summary>
public class FeedParserImageTests
{
    private static readonly Uri Source = new("https://example.test/feed.xml");

    private static ParsedItem ParseOne(string xml) =>
        Assert.Single(new FeedParser().Parse(xml, Source).Items);

    private static string Rss(string itemBody) =>
        $"""
         <?xml version="1.0" encoding="utf-8"?>
         <rss version="2.0"
              xmlns:media="http://search.yahoo.com/mrss/"
              xmlns:content="http://purl.org/rss/1.0/modules/content/"
              xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd">
           <channel>
             <title>Example</title>
             <item>
               <title>An item</title>
               <link>https://example.test/one</link>
               <guid>one</guid>
               {itemBody}
             </item>
           </channel>
         </rss>
         """;

    [Fact]
    public void Reads_media_content_the_way_Ars_Technica_publishes_it()
    {
        var item = ParseOne(Rss(
            """<media:content url="https://cdn.example.test/big.jpg" medium="image" width="1200" />"""));

        Assert.Equal("https://cdn.example.test/big.jpg", item.ImageUrl);
    }

    [Fact]
    public void Reads_media_thumbnail_the_way_BBC_News_publishes_it()
    {
        var item = ParseOne(Rss(
            """<media:thumbnail width="976" height="549" url="https://cdn.example.test/thumb.jpg" />"""));

        Assert.Equal("https://cdn.example.test/thumb.jpg", item.ImageUrl);
    }

    [Fact]
    public void Prefers_the_full_size_media_content_over_the_thumbnail()
    {
        // A feed offering both means the larger one is the better lead image,
        // and Ars Technica offers both on every item.
        var item = ParseOne(Rss(
            """
            <media:thumbnail url="https://cdn.example.test/small.jpg" />
            <media:content url="https://cdn.example.test/large.jpg" type="image/jpeg" />
            """));

        Assert.Equal("https://cdn.example.test/large.jpg", item.ImageUrl);
    }

    [Fact]
    public void Reads_a_media_content_wrapped_in_a_media_group()
    {
        // Media RSS lets a publisher wrap several renditions in a group, and
        // an item that does has no media:content child of its own.
        var item = ParseOne(Rss(
            """
            <media:group>
              <media:content url="https://cdn.example.test/grouped.jpg" medium="image" />
            </media:group>
            """));

        Assert.Equal("https://cdn.example.test/grouped.jpg", item.ImageUrl);
    }

    [Fact]
    public void Reads_an_image_enclosure()
    {
        var item = ParseOne(Rss(
            """<enclosure url="https://cdn.example.test/enclosed.png" type="image/png" length="1234" />"""));

        Assert.Equal("https://cdn.example.test/enclosed.png", item.ImageUrl);
    }

    [Fact]
    public void Ignores_an_enclosure_that_is_not_an_image()
    {
        // media:content and enclosure are also how a feed attaches audio and
        // video. Recording an MP3 as the item's picture would put a broken
        // image in the list, which is worse than no image at all.
        var item = ParseOne(Rss(
            """<enclosure url="https://cdn.example.test/episode.mp3" type="audio/mpeg" length="1234" />"""));

        Assert.Null(item.ImageUrl);
    }

    [Fact]
    public void Ignores_media_content_that_is_not_an_image()
    {
        var item = ParseOne(Rss(
            """<media:content url="https://cdn.example.test/clip.mp4" type="video/mp4" />"""));

        Assert.Null(item.ImageUrl);
    }

    [Fact]
    public void Reads_the_itunes_image_href()
    {
        var item = ParseOne(Rss(
            """<itunes:image href="https://cdn.example.test/cover.jpg" />"""));

        Assert.Equal("https://cdn.example.test/cover.jpg", item.ImageUrl);
    }

    [Fact]
    public void Falls_back_to_the_first_img_in_the_item_html()
    {
        var item = ParseOne(Rss(
            """
            <description>&lt;p&gt;Words&lt;/p&gt;&lt;img src="https://cdn.example.test/inline.jpg" alt="x"&gt;</description>
            """));

        Assert.Equal("https://cdn.example.test/inline.jpg", item.ImageUrl);
    }

    [Fact]
    public void Prefers_declared_metadata_over_an_image_buried_in_the_html()
    {
        var item = ParseOne(Rss(
            """
            <media:thumbnail url="https://cdn.example.test/declared.jpg" />
            <description>&lt;img src="https://cdn.example.test/inline.jpg"&gt;</description>
            """));

        Assert.Equal("https://cdn.example.test/declared.jpg", item.ImageUrl);
    }

    [Fact]
    public void Resolves_a_relative_image_against_the_feed_url()
    {
        var item = ParseOne(Rss("""<media:thumbnail url="/images/card.jpg" />"""));

        Assert.Equal("https://example.test/images/card.jpg", item.ImageUrl);
    }

    [Fact]
    public void Refuses_an_image_the_url_policy_rejects()
    {
        // The same gate every other address from remote content goes through.
        // This one will be fetched unattended by the image cache, so a feed
        // naming a loopback or link-local address must not become a request.
        foreach (var hostile in new[]
                 {
                     "http://127.0.0.1/secret.png",
                     "http://169.254.169.254/latest/meta-data",
                     "file:///etc/passwd"
                 })
        {
            var item = ParseOne(Rss($"""<media:thumbnail url="{hostile}" />"""));
            Assert.Null(item.ImageUrl);
        }
    }

    [Fact]
    public void Names_no_image_when_the_feed_names_none()
    {
        var item = ParseOne(Rss("<description>Just words.</description>"));

        Assert.Null(item.ImageUrl);
    }

    [Fact]
    public void Reads_an_atom_enclosure_link()
    {
        // Atom has no enclosure element; it says the relationship on a link.
        var xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Example</title>
              <entry>
                <id>one</id>
                <title>An entry</title>
                <link href="https://example.test/one" />
                <link rel="enclosure" type="image/jpeg" href="https://cdn.example.test/atom.jpg" />
              </entry>
            </feed>
            """;

        Assert.Equal("https://cdn.example.test/atom.jpg", ParseOne(xml).ImageUrl);
    }

    [Fact]
    public void Reads_media_thumbnail_in_an_atom_entry()
    {
        var xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:media="http://search.yahoo.com/mrss/">
              <title>Example</title>
              <entry>
                <id>one</id>
                <title>An entry</title>
                <link href="https://example.test/one" />
                <media:thumbnail url="https://cdn.example.test/atom-thumb.jpg" />
              </entry>
            </feed>
            """;

        Assert.Equal("https://cdn.example.test/atom-thumb.jpg", ParseOne(xml).ImageUrl);
    }
}
