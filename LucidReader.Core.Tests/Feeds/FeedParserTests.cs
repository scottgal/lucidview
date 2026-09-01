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
    [InlineData("rss2-categories.xml")]
    [InlineData("atom-categories.xml")]
    public void CanParse_accepts_every_real_feed_in_the_corpus(string fixture)
    {
        Assert.True(_parser.CanParse(Fixtures.Feed(fixture)));
    }

    // =====================================================================
    // Publisher categories.
    // =====================================================================

    [Fact]
    public void Rss_category_elements_become_the_items_categories()
    {
        var feed = Parse("rss2-categories.xml");

        Assert.Equal(
            ["AI", "Architecture", "ASP.NET", "Patterns", "Performance", "StyloBot"],
            feed.Items[0].Categories);
    }

    /// <summary>
    /// Order is the publisher's, and the first spelling wins: "ai" arrives
    /// after "AI" and is the same tag by TagName's rules, so it must not
    /// produce a second entry or change the one already there.
    /// </summary>
    [Fact]
    public void A_repeated_category_is_collapsed_onto_its_first_spelling()
    {
        var categories = Parse("rss2-categories.xml").Items[0].Categories;

        Assert.Equal(6, categories.Count);
        Assert.Contains("AI", categories);
        Assert.DoesNotContain("ai", categories);
    }

    /// <summary>
    /// The padded "  Performance  " and the plain "Performance" are one tag
    /// after TagName's trimming, which is the point of running publisher
    /// categories through the same rules a typed name goes through.
    /// </summary>
    [Fact]
    public void A_padded_category_is_trimmed_and_not_stored_twice()
    {
        var categories = Parse("rss2-categories.xml").Items[0].Categories;

        Assert.Single(categories, name => name == "Performance");
    }

    [Fact]
    public void Categories_the_name_rules_refuse_are_dropped_rather_than_stored()
    {
        var categories = Parse("rss2-categories.xml").Items[1].Categories;

        // An empty element, a whitespace-only one, one of 33 characters and
        // one carrying a comma: only the usable name survives, and the item
        // is still parsed rather than skipped over any of them.
        Assert.Equal(["Usable"], categories);
    }

    [Fact]
    public void An_item_with_no_category_elements_has_an_empty_list_rather_than_null()
    {
        var feed = Parse("rss2-simple.xml");

        Assert.NotNull(feed.Items[0].Categories);
        Assert.Empty(feed.Items[0].Categories);
    }

    [Fact]
    public void Atom_categories_come_from_term_and_fall_back_to_label()
    {
        var feed = Parse("atom-categories.xml");

        // "dotnet" (term) rather than "DotNet" (label) for the one that
        // carries both, and one entry for it, not two. "Weeknotes" has no
        // term at all and is the fallback case.
        Assert.Equal(["Avalonia", "dotnet", "Weeknotes"], feed.Items[0].Categories);
    }

    [Fact]
    public void An_atom_category_with_neither_term_nor_label_yields_nothing()
    {
        var feed = Parse("atom-categories.xml");

        Assert.Empty(feed.Items[1].Categories);
    }

    // --- The feed's own icon ---
    //
    // The cheapest of the three sources FeedIconResolver tries, because it
    // arrives inside a document the refresh has already fetched and parsed.
    // Parsed here rather than by the resolver so a scraped subscription, which
    // never goes near a feed document, simply has none.

    [Fact]
    public void An_rss_channel_image_becomes_the_feeds_icon()
    {
        var feed = _parser.Parse(
            """
            <rss version="2.0"><channel>
              <title>Example</title><link>https://example.com</link>
              <image><url>/logo.png</url><title>Example</title></image>
              <item><guid>1</guid><title>One</title></item>
            </channel></rss>
            """, Source);

        // Resolved against the feed's own address, like every other link here.
        Assert.Equal("https://example.com/logo.png", feed.IconUrl);
    }

    /// <summary>
    /// icon before logo: RFC 4287's icon is the small square meant to sit
    /// beside the feed's name, which is the sidebar's use exactly, where logo
    /// is a banner that would be cropped to nothing.
    /// </summary>
    [Fact]
    public void An_atom_icon_is_preferred_over_a_logo()
    {
        var feed = _parser.Parse(
            """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Example</title>
              <icon>https://example.com/icon.png</icon>
              <logo>https://example.com/banner.png</logo>
              <entry><id>1</id><title>One</title></entry>
            </feed>
            """, Source);

        Assert.Equal("https://example.com/icon.png", feed.IconUrl);
    }

    [Fact]
    public void An_atom_logo_is_used_when_there_is_no_icon()
    {
        var feed = _parser.Parse(
            """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Example</title>
              <logo>https://example.com/banner.png</logo>
              <entry><id>1</id><title>One</title></entry>
            </feed>
            """, Source);

        Assert.Equal("https://example.com/banner.png", feed.IconUrl);
    }

    [Fact]
    public void A_feed_declaring_no_image_has_a_null_icon()
    {
        Assert.Null(Parse("rss2-simple.xml").IconUrl);
    }
}
