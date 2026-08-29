using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

public class SiteMetadataExtractorTests
{
    private static readonly Uri Base = new("https://example.com/blog/post");

    private static string Html(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", name));

    [Fact]
    public void An_icon_link_is_found_and_resolved_to_an_absolute_url()
    {
        var meta = SiteMetadataExtractor.Extract(Html("metadata-rich.html"), Base);

        Assert.Equal("https://example.com/icons/favicon-32.png", meta.IconUrl);
    }

    [Fact]
    public void An_open_graph_image_is_found()
    {
        var meta = SiteMetadataExtractor.Extract(Html("metadata-rich.html"), Base);

        Assert.Equal("https://cdn.example.com/card.jpg", meta.ImageUrl);
    }

    [Fact]
    public void An_open_graph_description_is_found()
    {
        var meta = SiteMetadataExtractor.Extract(Html("metadata-rich.html"), Base);

        Assert.Equal("A description from OpenGraph.", meta.Description);
    }

    [Fact]
    public void A_twitter_image_is_used_when_open_graph_is_absent()
    {
        var meta = SiteMetadataExtractor.Extract(Html("metadata-twitter.html"), Base);

        Assert.Equal("https://example.com/cards/twitter.png", meta.ImageUrl);
    }

    [Fact]
    public void A_shortcut_icon_relative_to_the_page_resolves_against_the_base()
    {
        var meta = SiteMetadataExtractor.Extract(Html("metadata-twitter.html"), Base);

        Assert.Equal("https://example.com/blog/favicon.ico", meta.IconUrl);
    }

    [Fact]
    public void A_plain_meta_description_is_used_when_open_graph_is_absent()
    {
        var meta = SiteMetadataExtractor.Extract(Html("metadata-twitter.html"), Base);

        Assert.Equal("A plain meta description.", meta.Description);
    }

    [Fact]
    public void A_page_with_no_metadata_yields_nulls_rather_than_throwing()
    {
        var meta = SiteMetadataExtractor.Extract(Html("metadata-sparse.html"), Base);

        Assert.Null(meta.IconUrl);
        Assert.Null(meta.ImageUrl);
        Assert.Null(meta.Description);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("file:///etc/passwd")]
    public void An_image_or_icon_with_a_dangerous_scheme_is_refused(string url)
    {
        var html = $"<html><head><meta property=\"og:image\" content=\"{url}\">" +
                   $"<link rel=\"icon\" href=\"{url}\"></head></html>";

        var meta = SiteMetadataExtractor.Extract(html, Base);

        Assert.Null(meta.ImageUrl);
        Assert.Null(meta.IconUrl);
    }

    [Fact]
    public void Entities_in_a_metadata_url_are_decoded()
    {
        var html = "<html><head><meta property=\"og:image\" " +
                   "content=\"https://cdn.example.com/c.jpg?a=1&amp;b=2\"></head></html>";

        var meta = SiteMetadataExtractor.Extract(html, Base);

        Assert.Equal("https://cdn.example.com/c.jpg?a=1&b=2", meta.ImageUrl);
    }

    [Fact]
    public void Garbage_input_does_not_throw()
    {
        var meta = SiteMetadataExtractor.Extract("<<<not html", Base);

        Assert.Null(meta.IconUrl);
    }
}
