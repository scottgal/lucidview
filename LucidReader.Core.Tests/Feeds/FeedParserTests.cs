using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

public class FeedParserTests
{
    private static readonly Uri Source = new("https://example.com/feed.xml");
    private readonly FeedParser _parser = new();

    private ParsedFeed Parse(string fixture) =>
        _parser.Parse(Fixtures.Feed(fixture), Source);

    [Fact]
    public void Rss2_yields_the_channel_title_and_every_item()
    {
        var feed = Parse("rss2-simple.xml");

        Assert.Equal("Example Blog", feed.Title);
        Assert.Equal("https://example.com/", feed.SiteUrl);
        Assert.Equal(2, feed.Items.Count);
        Assert.Equal("First post", feed.Items[0].Title);
        Assert.Equal("tag:example.com,2026:1", feed.Items[0].Guid);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-26T09:00:00Z"),
            feed.Items[0].PublishedUtc);
    }

    [Fact]
    public void Content_encoded_beats_the_description_for_article_content()
    {
        var feed = Parse("rss2-content-encoded.xml");

        var item = Assert.Single(feed.Items);
        Assert.Contains("second paragraph", item.ContentHtml);
        Assert.Equal("Just the first sentence.", item.Summary);
    }

    [Fact]
    public void Dublin_core_supplies_the_author_and_date_when_rss_does_not()
    {
        var feed = Parse("rss2-content-encoded.xml");

        var item = Assert.Single(feed.Items);
        Assert.Equal("Jo Bloggs", item.Author);
        Assert.Equal(DateTimeOffset.Parse("2026-08-27T09:00:00Z"), item.PublishedUtc);
    }

    [Fact]
    public void Atom_is_parsed_including_the_distinct_updated_date()
    {
        var feed = Parse("atom-simple.xml");

        Assert.Equal("Atom Example", feed.Title);
        var entry = Assert.Single(feed.Items);
        Assert.Equal("urn:uuid:1225c695-cfb8-4ebb-aaaa-80da344efa6a", entry.Guid);
        Assert.Equal("https://atom.example/entry-1", entry.Link);
        Assert.Equal("Sam Reader", entry.Author);
        Assert.Equal(DateTimeOffset.Parse("2026-08-26T09:00:00Z"), entry.PublishedUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-08-27T11:30:00Z"), entry.UpdatedUtc);
        Assert.Contains("full entry body", entry.ContentHtml);
    }

    [Fact]
    public void Rss1_rdf_is_parsed_despite_its_different_document_shape()
    {
        var feed = Parse("rdf-rss1.xml");

        Assert.Equal("RDF Example", feed.Title);
        var item = Assert.Single(feed.Items);
        Assert.Equal("An RDF item", item.Title);
        Assert.Equal("Pat Writer", item.Author);
        Assert.Equal(DateTimeOffset.Parse("2026-08-27T09:00:00Z"), item.PublishedUtc);
    }

    [Fact]
    public void An_unparseable_date_costs_the_item_its_date_and_nothing_else()
    {
        var feed = Parse("rss2-bad-dates.xml");

        Assert.Equal(3, feed.Items.Count);
        Assert.NotNull(feed.Items[0].PublishedUtc);
        Assert.NotNull(feed.Items[1].PublishedUtc);
        Assert.Null(feed.Items[2].PublishedUtc);
        Assert.Equal("Complete nonsense", feed.Items[2].Title);
    }

    [Fact]
    public void An_item_with_no_guid_is_returned_with_a_null_guid()
    {
        var feed = Parse("rss2-no-guid.xml");

        var item = Assert.Single(feed.Items);
        Assert.Null(item.Guid);
        Assert.Equal("https://noguid.example/article-1", item.Link);
    }

    [Fact]
    public void Relative_links_are_resolved_against_the_feed_url()
    {
        var feed = Parse("rss2-relative-links.xml");

        var item = Assert.Single(feed.Items);
        Assert.Equal("https://example.com/posts/article-1", item.Link);
    }

    [Fact]
    public void An_undeclared_entity_does_not_cost_us_the_other_items()
    {
        var feed = Parse("rss2-undeclared-entity.xml");

        Assert.Equal(2, feed.Items.Count);
        Assert.Contains(feed.Items, item => item.Title == "A perfectly fine item");
    }

    [Fact]
    public void Cdata_entities_survive_untouched_while_undeclared_entities_outside_are_repaired()
    {
        var feed = Parse("rss2-cdata-entities.xml");

        var item = Assert.Single(feed.Items);

        // The undeclared &reg; is legal, literal text inside CDATA and must not
        // be rewritten by the document-level entity recovery pass.
        Assert.Contains("&reg;", item.ContentHtml);

        // The undeclared &nbsp; outside CDATA is what triggered recovery in the
        // first place; it should still be repaired so the title is readable.
        Assert.Contains("Trouble outside CDATA", item.Title);
        Assert.DoesNotContain("&nbsp;", item.Title);
    }

    [Fact]
    public void A_bare_ampersand_is_repaired_rather_than_losing_the_document()
    {
        var feed = Parse("rss2-bare-ampersand.xml");

        var item = Assert.Single(feed.Items);
        Assert.Equal("Fish & Chips", item.Title);
        Assert.Contains("Bed & Breakfast", item.Summary);
    }

    [Fact]
    public void An_item_with_no_guid_link_or_title_is_skipped_and_counted()
    {
        var feed = Parse("rss2-no-identity.xml");

        var item = Assert.Single(feed.Items);
        Assert.Equal("A perfectly identifiable item", item.Title);
        Assert.Equal(1, feed.SkippedItemCount);
    }

    [Fact]
    public void An_empty_channel_parses_successfully_with_zero_items()
    {
        var feed = Parse("rss2-empty-channel.xml");

        Assert.Equal("Empty Channel", feed.Title);
        Assert.Empty(feed.Items);
        Assert.Equal(0, feed.SkippedItemCount);
    }

    [Fact]
    public void CanParse_rejects_an_html_page_served_instead_of_a_feed()
    {
        Assert.False(_parser.CanParse(Fixtures.Feed("not-a-feed.html")));
    }

    [Fact]
    public void Parsing_an_html_page_throws_a_feed_parse_exception()
    {
        Assert.Throws<FeedParseException>(
            () => _parser.Parse(Fixtures.Feed("not-a-feed.html"), Source));
    }

    [Theory]
    [InlineData("rss2-simple.xml")]
    [InlineData("rss2-content-encoded.xml")]
    [InlineData("atom-simple.xml")]
    [InlineData("rdf-rss1.xml")]
    [InlineData("rss2-bad-dates.xml")]
    [InlineData("rss2-no-guid.xml")]
    [InlineData("rss2-relative-links.xml")]
    [InlineData("rss2-undeclared-entity.xml")]
    [InlineData("rss2-empty-channel.xml")]
    [InlineData("rss2-cdata-entities.xml")]
    [InlineData("rss2-bare-ampersand.xml")]
    [InlineData("rss2-no-identity.xml")]
    public void CanParse_accepts_every_real_feed_in_the_corpus(string fixture)
    {
        Assert.True(_parser.CanParse(Fixtures.Feed(fixture)));
    }
}
