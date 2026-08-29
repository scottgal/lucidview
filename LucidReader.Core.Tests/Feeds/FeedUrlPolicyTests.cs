using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// The shapes here are the ones an OPML file would use to turn "import my
/// subscriptions" into a request against something on the machine or the local
/// network, plus the ordinary public feed that has to keep working.
/// </summary>
public class FeedUrlPolicyTests
{
    [Theory]
    [InlineData("https://xkcd.com/atom.xml")]
    [InlineData("http://example.com/feed")]
    [InlineData("https://example.com:8443/feed.xml")]
    [InlineData("https://news.bbc.co.uk/rss.xml?edition=uk")]
    public void A_public_web_feed_is_accepted(string url)
    {
        Assert.True(FeedUrlPolicy.TryValidate(url, out var uri, out var reason), reason);
        Assert.NotNull(uri);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/feed.xml")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/xml,<rss/>")]
    public void A_scheme_other_than_http_or_https_is_refused(string url) =>
        AssertRefused(url);

    [Theory]
    [InlineData("https://attacker:token@internal.example/feed.xml")]
    [InlineData("http://admin@example.com/feed.xml")]
    public void An_address_with_embedded_credentials_is_refused(string url) =>
        AssertRefused(url);

    [Theory]
    [InlineData("http://127.0.0.1:8080/admin/shutdown")]
    [InlineData("http://localhost:5000/feed")]
    [InlineData("http://LOCALHOST/feed")]
    [InlineData("http://[::1]/feed")]
    public void A_loopback_address_is_refused(string url) => AssertRefused(url);

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/iam/security-credentials/")]
    [InlineData("http://169.254.1.1/feed")]
    [InlineData("http://[fe80::1]/feed")]
    public void A_link_local_address_is_refused(string url) => AssertRefused(url);

    [Theory]
    [InlineData("http://10.0.0.5/feed")]
    [InlineData("http://172.16.4.1/feed")]
    [InlineData("http://172.31.255.254/feed")]
    [InlineData("http://192.168.1.1/feed")]
    public void A_private_network_address_is_refused(string url) => AssertRefused(url);

    [Theory]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    public void An_address_just_outside_the_private_range_is_accepted(string host) =>
        Assert.True(FeedUrlPolicy.IsAllowed($"http://{host}/feed"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/feed.xml")]
    public void An_address_that_is_not_a_complete_web_url_is_refused(string? url) =>
        AssertRefused(url);

    /// <summary>
    /// A control character is how a second target gets smuggled past a check
    /// that only reads the visible part of the string.
    /// </summary>
    [Fact]
    public void An_address_containing_a_control_character_is_refused() =>
        AssertRefused("https://example.com/feed\u0000https://evil.example/");

    private static void AssertRefused(string? url)
    {
        Assert.False(FeedUrlPolicy.TryValidate(url, out var uri, out var reason));
        Assert.Null(uri);
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.False(FeedUrlPolicy.IsAllowed(url));
    }
}
