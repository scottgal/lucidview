using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// Spellings of a blocked address that are not the obvious spelling. Each one
/// reaches exactly the same place as a form the policy already refuses, so
/// each has to be refused too.
/// </summary>
public class FeedUrlPolicyHostShapeTests
{
    [Theory]
    [InlineData("http://169.254.169.254./feed")]
    [InlineData("http://localhost./feed")]
    [InlineData("http://LocalHost./feed")]
    [InlineData("http://127.0.0.1.:18077/secret")]
    [InlineData("http://10.0.0.5./feed")]
    public void A_trailing_dot_does_not_get_a_host_past_the_check(string url) =>
        AssertRefused(url);

    [Theory]
    [InlineData("http://[64:ff9b::7f00:1]/feed")]        // NAT64 loopback
    [InlineData("http://[64:ff9b::a9fe:a9fe]/feed")]     // NAT64 link-local metadata
    [InlineData("http://[64:ff9b::c0a8:101]/feed")]      // NAT64 192.168.1.1
    [InlineData("http://[2002:7f00:1::]/feed")]          // 6to4 loopback
    [InlineData("http://[2002:a9fe:a9fe::]/feed")]       // 6to4 link-local metadata
    [InlineData("http://[::127.0.0.1]/feed")]            // v4-compatible loopback
    public void An_ipv4_address_hidden_inside_an_ipv6_one_is_refused(string url) =>
        AssertRefused(url);

    /// <summary>
    /// The shapes the reviewer confirmed were already refused. They stay in
    /// the suite so the normalising above cannot quietly reopen one: Uri
    /// itself canonicalises these to dotted quads before the policy sees them.
    /// </summary>
    [Theory]
    [InlineData("http://2130706433/feed")]        // decimal 127.0.0.1
    [InlineData("http://0x7f000001/feed")]        // hex 127.0.0.1
    [InlineData("http://017700000001/feed")]      // octal 127.0.0.1
    [InlineData("http://127.1/feed")]             // dotted shorthand
    [InlineData("http://[::ffff:127.0.0.1]/feed")] // v4-mapped
    [InlineData("http://[::ffff:169.254.169.254]/feed")]
    public void The_numeric_spellings_stay_refused(string url) => AssertRefused(url);

    /// <summary>
    /// A NAT64 or 6to4 address wrapping an ordinary public IPv4 address is
    /// still an ordinary public address, so narrowing these shapes must not
    /// turn into a blanket ban on the prefixes.
    /// </summary>
    [Theory]
    [InlineData("http://[64:ff9b::5db8:d822]/feed")]  // 93.184.216.34
    [InlineData("http://[2002:5db8:d822::]/feed")]
    public void A_public_ipv4_address_inside_an_ipv6_one_is_still_accepted(string url) =>
        Assert.True(FeedUrlPolicy.IsAllowed(url), url);

    [Fact]
    public void An_ordinary_hostname_ending_in_a_dot_is_still_accepted() =>
        Assert.True(FeedUrlPolicy.IsAllowed("https://example.com./feed.xml"));

    private static void AssertRefused(string url)
    {
        Assert.False(FeedUrlPolicy.TryValidate(url, out var uri, out var reason), url);
        Assert.Null(uri);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }
}
