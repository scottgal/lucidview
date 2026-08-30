using System.Net.Http.Headers;
using System.Reflection;
using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// The one string every outbound request in this app identifies itself as.
/// A site owner reading it in their logs has to be able to tell what hit them
/// and go and read about it, and the header has to be legal while doing it.
/// </summary>
public class UserAgentTests
{
    [Fact]
    public void It_is_a_valid_user_agent_header()
    {
        Assert.True(
            ProductInfoHeaderValue.TryParse(FeedFetcher.UserAgentString, out _)
            || HeaderParsesAsAUserAgent(FeedFetcher.UserAgentString));
    }

    private static bool HeaderParsesAsAUserAgent(string value)
    {
        using var request = new HttpRequestMessage();
        return request.Headers.UserAgent.TryParseAdd(value);
    }

    [Fact]
    public void The_product_token_has_no_spaces_in_it()
    {
        var token = FeedFetcher.UserAgentString.Split(' ')[0];

        Assert.StartsWith("mylo/", token);
        Assert.DoesNotContain(' ', token);
    }

    [Fact]
    public void It_says_what_kind_of_thing_it_is()
    {
        Assert.Contains("(rss reader;", FeedFetcher.UserAgentString);
    }

    [Fact]
    public void It_links_to_the_product_readme()
    {
        Assert.Contains("+" + FeedFetcher.ReadmeUrl, FeedFetcher.UserAgentString);
        Assert.EndsWith(")", FeedFetcher.UserAgentString);
        Assert.StartsWith("https://github.com/scottgal/lucidview/", FeedFetcher.ReadmeUrl);
    }

    /// <summary>
    /// The version is read off the assembly rather than written down twice, so
    /// it cannot drift from what the build produced. This asserts that they
    /// still agree, which is the whole reason it is not a literal.
    /// </summary>
    [Fact]
    public void The_version_is_the_assemblys_own()
    {
        var version = typeof(FeedFetcher).Assembly.GetName().Version!;
        var expected = $"mylo/{version.Major}.{version.Minor}.{version.Build} ";

        Assert.StartsWith(expected, FeedFetcher.UserAgentString);
    }

    /// <summary>
    /// It also must not be the 1.0.0 an assembly with no version of its own
    /// reports, which is what LucidReader.Core did before it was given one.
    /// </summary>
    [Fact]
    public void The_version_is_not_the_unversioned_default()
    {
        Assert.DoesNotContain("mylo/1.0.0 ", FeedFetcher.UserAgentString);
    }

    /// <summary>
    /// Every class that makes a request sends this same field. Asserted by
    /// reflection over the source-visible fetchers rather than by making
    /// requests, since the point is that none of them carries a string of its
    /// own.
    /// </summary>
    [Theory]
    [InlineData("LucidReader.Core.Feeds.FeedAutodiscovery")]
    [InlineData("LucidReader.Core.Feeds.FeedlyFeedSearch")]
    [InlineData("LucidReader.Core.Offline.ArticleFetcher")]
    public void No_other_fetcher_declares_a_user_agent_of_its_own(string typeName)
    {
        var type = typeof(FeedFetcher).Assembly.GetType(typeName);
        Assert.NotNull(type);

        var ownStrings = type!
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => f.GetValue(null) as string)
            .Where(value => value is not null);

        Assert.DoesNotContain(ownStrings, value => value!.Contains("mylo/", StringComparison.Ordinal));
    }
}
