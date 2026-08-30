using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// The identity two copies of one article share. Two halves to this: the
/// spellings that MUST collapse to one identity, and - at least as important -
/// the ones that MUST NOT, because a false match hides an article the user
/// never saw.
/// </summary>
public class CanonicalArticleIdTests
{
    [Theory]
    [InlineData("HTTPS://Example.COM/posts/one", "https://example.com/posts/one")]
    [InlineData("https://EXAMPLE.com/posts/one", "https://example.com/posts/one")]
    [InlineData("https://example.com/posts/one/", "https://example.com/posts/one")]
    [InlineData("https://example.com/posts/one#comments", "https://example.com/posts/one")]
    [InlineData("https://example.com/posts/one?utm_source=rss", "https://example.com/posts/one")]
    [InlineData("https://example.com/posts/one?utm_medium=feed&utm_campaign=x", "https://example.com/posts/one")]
    [InlineData("https://example.com/posts/one?fbclid=abc", "https://example.com/posts/one")]
    [InlineData("https://example.com/posts/one?gclid=abc", "https://example.com/posts/one")]
    [InlineData("https://example.com/posts/one?ref=newsletter", "https://example.com/posts/one")]
    [InlineData("  https://example.com/posts/one  ", "https://example.com/posts/one")]
    [InlineData("https://example.com:443/posts/one", "https://example.com/posts/one")]
    [InlineData("http://example.com:80/posts/one", "http://example.com/posts/one")]
    public void Normalises_to_the_expected_identity(string link, string expected) =>
        Assert.Equal(expected, CanonicalArticleId.FromLink(link));

    [Fact]
    public void The_same_article_in_an_rss_and_an_atom_feed_gets_one_identity()
    {
        var fromRss = CanonicalArticleId.FromLink(
            "https://www.mostlylucid.net/blog/some-post/?utm_source=rss&utm_medium=feed");
        var fromAtom = CanonicalArticleId.FromLink(
            "https://WWW.MostlyLucid.net/blog/some-post#top");

        Assert.NotNull(fromRss);
        Assert.Equal(fromRss, fromAtom);
    }

    [Fact]
    public void Two_different_articles_on_one_site_do_not_collide()
    {
        var first = CanonicalArticleId.FromLink("https://example.com/blog/one");
        var second = CanonicalArticleId.FromLink("https://example.com/blog/two");

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// The query string is the only thing naming the article on a great many
    /// sites, so anything not on the tracking list has to survive.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/?p=1", "https://example.com/?p=2")]
    [InlineData("https://example.com/index.php?id=10", "https://example.com/index.php?id=11")]
    [InlineData("https://example.com/story?page=1", "https://example.com/story?page=2")]
    public void Query_parameters_that_name_the_article_are_kept(string first, string second) =>
        Assert.NotEqual(CanonicalArticleId.FromLink(first), CanonicalArticleId.FromLink(second));

    [Fact]
    public void A_kept_parameter_survives_alongside_a_stripped_one()
    {
        Assert.Equal(
            "https://example.com/index.php?id=10",
            CanonicalArticleId.FromLink("https://example.com/index.php?id=10&utm_source=rss"));
    }

    /// <summary>
    /// Case in the path is left alone: /About and /about are two different
    /// pages on any case-sensitive server, which is most of them.
    /// </summary>
    [Fact]
    public void Path_case_is_significant()
    {
        Assert.NotEqual(
            CanonicalArticleId.FromLink("https://example.com/About"),
            CanonicalArticleId.FromLink("https://example.com/about"));
    }

    [Fact]
    public void Two_different_hosts_do_not_collide()
    {
        Assert.NotEqual(
            CanonicalArticleId.FromLink("https://a.example.com/post"),
            CanonicalArticleId.FromLink("https://b.example.com/post"));
    }

    [Fact]
    public void A_bare_host_keeps_its_root_slash()
    {
        Assert.Equal("https://example.com/", CanonicalArticleId.FromLink("https://example.com/"));
        Assert.Equal("https://example.com/", CanonicalArticleId.FromLink("https://example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/only")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("ftp://example.com/feed")]
    [InlineData("javascript:alert(1)")]
    public void Anything_that_is_not_a_web_link_has_no_identity(string? link) =>
        Assert.Null(CanonicalArticleId.FromLink(link));
}
