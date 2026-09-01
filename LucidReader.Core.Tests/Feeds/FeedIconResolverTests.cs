using System.Net;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// Icons used to be found in one place only, the Add Feed dialog, so every
/// subscription created any other way - the starter feeds, an OPML import, a
/// pasted feed address, the catalogue - showed the grey placeholder forever.
/// These cover the resolver that fixes that, and in particular the three things
/// it must not do: fetch when image caching is off, accept an address
/// FeedUrlPolicy refuses, or let a failing lookup become anything but null.
/// </summary>
public class FeedIconResolverTests
{
    private static FeedIconResolver Create(
        StubHttpHandler handler, ReaderSettings? settings = null) =>
        new(handler.CreateClient(), () => settings ?? ReaderSettings.Defaults);

    [Fact]
    public async Task The_feeds_own_icon_wins_and_costs_no_request()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html></html>");

        var icon = await Create(handler).ResolveAsync(
            "https://example.com/feed.xml",
            "https://example.com",
            "https://example.com/channel-image.png");

        Assert.Equal("https://example.com/channel-image.png", icon);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_sites_declared_icon_is_read_from_its_page()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK,
            """<html><head><link rel="icon" href="/assets/site.png"></head></html>""",
            mediaType: "text/html");

        var icon = await Create(handler).ResolveAsync(
            "https://example.com/feed.xml", "https://example.com", null);

        Assert.Equal("https://example.com/assets/site.png", icon);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_page_declaring_nothing_falls_back_to_the_favicon_guess()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<html><head></head></html>", mediaType: "text/html");

        var icon = await Create(handler).ResolveAsync(
            "https://example.com/feed.xml", "https://example.com", null);

        Assert.Equal("https://example.com/favicon.ico", icon);
    }

    /// <summary>
    /// With no site link recorded - which is every feed whose refresh has not
    /// adopted one yet - the guess goes to the feed's own host rather than
    /// producing nothing.
    /// </summary>
    [Fact]
    public async Task With_no_site_link_the_guess_uses_the_feeds_host()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<html></html>", mediaType: "text/html");

        var icon = await Create(handler).ResolveAsync(
            "https://feeds.example.com/atom.xml", null, null);

        Assert.Equal("https://feeds.example.com/favicon.ico", icon);
    }

    /// <summary>
    /// A page fetch that fails is not a failure of anything: there are two
    /// cheaper sources either side of it, and the guess still stands.
    /// </summary>
    [Fact]
    public async Task A_failing_page_fetch_still_yields_the_guess()
    {
        var handler = StubHttpHandler.Throwing(new HttpRequestException("no route"));

        var icon = await Create(handler).ResolveAsync(
            "https://example.com/feed.xml", "https://example.com", null);

        Assert.Equal("https://example.com/favicon.ico", icon);
    }

    /// <summary>
    /// ImageResolver refuses to fetch any icon while CacheImages is off, so an
    /// icon recorded here could never be shown. Nothing is looked up and
    /// nothing is returned.
    /// </summary>
    [Fact]
    public async Task Nothing_is_resolved_while_image_caching_is_off()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html></html>");

        var icon = await Create(handler, ReaderSettings.Defaults with { CacheImages = false })
            .ResolveAsync("https://example.com/feed.xml", "https://example.com",
                "https://example.com/channel-image.png");

        Assert.Null(icon);
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// The icon URL came out of remote content and will be fetched unattended,
    /// so it passes the same gate every other address in this app passes. The
    /// cloud metadata endpoint is the address that gate exists for.
    /// </summary>
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://127.0.0.1/icon.png")]
    [InlineData("http://192.168.1.1/icon.png")]
    [InlineData("javascript:alert(1)")]
    public async Task A_feed_declared_icon_the_policy_refuses_is_not_used(string declared)
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<html><head></head></html>", mediaType: "text/html");

        var icon = await Create(handler).ResolveAsync(
            "https://example.com/feed.xml", "https://example.com", declared);

        Assert.Equal("https://example.com/favicon.ico", icon);
    }

    [Fact]
    public async Task A_feed_url_that_is_not_http_yields_nothing()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html></html>");

        var icon = await Create(handler).ResolveAsync("not a url", null, null);

        Assert.Null(icon);
        Assert.Empty(handler.Requests);
    }
}
