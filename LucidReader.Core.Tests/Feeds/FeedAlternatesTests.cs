using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

public class FeedAlternatesTests
{
    private static FeedAlternateCandidate WithItems(
        string url, string? mediaType, params string[] links) =>
        new(url, mediaType, "https://example.com/", links);

    [Fact]
    public void One_feed_is_never_an_alternate_of_anything()
    {
        var verdicts = FeedAlternates.Classify(
            [new FeedAlternateCandidate("https://example.com/rss")]);

        Assert.Single(verdicts);
        Assert.False(verdicts[0].IsAlternate);
    }

    /// <summary>
    /// The case that started all this: one site, two formats, the same twenty
    /// articles. Exactly one survives, and it is the Atom one.
    /// </summary>
    [Fact]
    public void Two_formats_of_one_feed_leave_the_atom_one_ticked()
    {
        string[] articles =
        [
            "https://example.com/one", "https://example.com/two", "https://example.com/three"
        ];

        var verdicts = FeedAlternates.Classify(
        [
            WithItems("https://example.com/rss", "application/rss+xml", articles),
            WithItems("https://example.com/atom", "application/atom+xml", articles)
        ]);

        var rss = verdicts.Single(v => v.FeedUrl.EndsWith("/rss"));
        var atom = verdicts.Single(v => v.FeedUrl.EndsWith("/atom"));

        Assert.False(atom.IsAlternate);
        Assert.True(rss.IsAlternate);
        Assert.Equal("https://example.com/atom", rss.AlternateOfUrl);
    }

    /// <summary>
    /// The overlap does not have to be exact. Two formats of one feed
    /// routinely publish different window sizes, and a publisher can be
    /// mid-update while both are being read.
    /// </summary>
    [Fact]
    public void A_shorter_window_of_the_same_articles_still_pairs_up()
    {
        var verdicts = FeedAlternates.Classify(
        [
            WithItems("https://example.com/atom.xml", "application/atom+xml",
                "https://example.com/1", "https://example.com/2",
                "https://example.com/3", "https://example.com/4"),
            WithItems("https://example.com/rss.xml", "application/rss+xml",
                "https://example.com/1", "https://example.com/2", "https://example.com/3")
        ]);

        Assert.True(verdicts.Single(v => v.FeedUrl.Contains("rss")).IsAlternate);
        Assert.False(verdicts.Single(v => v.FeedUrl.Contains("atom")).IsAlternate);
    }

    /// <summary>
    /// The important negative. A news site publishing one feed per section
    /// puts several feeds on one host with feed-shaped addresses, and every
    /// one of them is a separate subscription the user may want.
    /// </summary>
    [Fact]
    public void Two_feeds_carrying_different_articles_are_both_ticked()
    {
        var verdicts = FeedAlternates.Classify(
        [
            WithItems("https://example.com/news/rss", "application/rss+xml",
                "https://example.com/news/1", "https://example.com/news/2"),
            WithItems("https://example.com/sport/rss", "application/rss+xml",
                "https://example.com/sport/1", "https://example.com/sport/2")
        ]);

        Assert.All(verdicts, v => Assert.False(v.IsAlternate));
    }

    /// <summary>
    /// Normalisation applies to the comparison too, so the same articles
    /// carrying feed-specific tracking parameters still recognise each other.
    /// </summary>
    [Fact]
    public void Tracking_parameters_do_not_stop_two_formats_pairing_up()
    {
        var verdicts = FeedAlternates.Classify(
        [
            WithItems("https://example.com/atom", "application/atom+xml",
                "https://example.com/a#top", "https://example.com/b#top"),
            WithItems("https://example.com/rss", "application/rss+xml",
                "https://example.com/a?utm_source=rss", "https://example.com/b?utm_source=rss")
        ]);

        Assert.True(verdicts.Single(v => v.FeedUrl.EndsWith("/rss")).IsAlternate);
    }

    [Fact]
    public void Feeds_naming_the_same_site_pair_up_when_their_contents_are_unknown()
    {
        var verdicts = FeedAlternates.Classify(
        [
            new FeedAlternateCandidate("https://example.com/atom", "application/atom+xml", "https://example.com/"),
            new FeedAlternateCandidate("https://example.com/rss", "application/rss+xml", "https://example.com/")
        ]);

        Assert.True(verdicts.Single(v => v.FeedUrl.EndsWith("/rss")).IsAlternate);
        Assert.False(verdicts.Single(v => v.FeedUrl.EndsWith("/atom")).IsAlternate);
    }

    [Fact]
    public void Feeds_naming_different_sites_do_not_pair_up()
    {
        var verdicts = FeedAlternates.Classify(
        [
            new FeedAlternateCandidate("https://example.com/a/rss", null, "https://example.com/a"),
            new FeedAlternateCandidate("https://example.com/b/rss", null, "https://example.com/b")
        ]);

        Assert.All(verdicts, v => Assert.False(v.IsAlternate));
    }

    /// <summary>
    /// With no contents and no site link at all, the address shape is the only
    /// evidence left. It has to pair up conventional feed names on one host
    /// and leave everything else alone.
    /// </summary>
    [Fact]
    public void With_no_evidence_at_all_conventional_addresses_on_one_host_pair_up()
    {
        var verdicts = FeedAlternates.Classify(
        [
            new FeedAlternateCandidate("https://example.com/rss"),
            new FeedAlternateCandidate("https://example.com/atom")
        ]);

        Assert.True(verdicts.Single(v => v.FeedUrl.EndsWith("/rss")).IsAlternate);
    }

    [Fact]
    public void With_no_evidence_feeds_under_different_paths_stay_apart()
    {
        var verdicts = FeedAlternates.Classify(
        [
            new FeedAlternateCandidate("https://example.com/news/rss"),
            new FeedAlternateCandidate("https://example.com/sport/rss")
        ]);

        Assert.All(verdicts, v => Assert.False(v.IsAlternate));
    }

    [Fact]
    public void With_no_evidence_feeds_on_different_hosts_stay_apart()
    {
        var verdicts = FeedAlternates.Classify(
        [
            new FeedAlternateCandidate("https://a.example.com/rss"),
            new FeedAlternateCandidate("https://b.example.com/atom")
        ]);

        Assert.All(verdicts, v => Assert.False(v.IsAlternate));
    }

    [Fact]
    public void Three_formats_of_one_feed_leave_exactly_one_ticked()
    {
        string[] articles = ["https://example.com/1", "https://example.com/2"];

        var verdicts = FeedAlternates.Classify(
        [
            WithItems("https://example.com/rss", "application/rss+xml", articles),
            WithItems("https://example.com/atom", "application/atom+xml", articles),
            WithItems("https://example.com/index.xml", null, articles)
        ]);

        Assert.Equal(1, verdicts.Count(v => !v.IsAlternate));
        Assert.Equal("https://example.com/atom", verdicts.Single(v => !v.IsAlternate).FeedUrl);
    }

    /// <summary>
    /// Same input, same answer. The dialog's pre-ticking must not depend on
    /// the order discovery happened to return the feeds in.
    /// </summary>
    [Fact]
    public void The_choice_does_not_depend_on_discovery_order()
    {
        string[] articles = ["https://example.com/1", "https://example.com/2"];

        var forwards = FeedAlternates.Classify(
        [
            WithItems("https://example.com/rss", "application/rss+xml", articles),
            WithItems("https://example.com/atom", "application/atom+xml", articles)
        ]);

        var backwards = FeedAlternates.Classify(
        [
            WithItems("https://example.com/atom", "application/atom+xml", articles),
            WithItems("https://example.com/rss", "application/rss+xml", articles)
        ]);

        Assert.Equal(
            forwards.Single(v => !v.IsAlternate).FeedUrl,
            backwards.Single(v => !v.IsAlternate).FeedUrl);
    }
}
