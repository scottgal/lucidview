using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// The shipped catalogue is data compiled into the binary, so what can go wrong
/// with it is not behaviour but content: an address the policy would refuse, a
/// duplicate row, a category heading that does not exist. None of those would
/// fail a build and all of them would reach a user on their first run.
///
/// What is NOT asserted here is that the addresses answer. Every one of them
/// was fetched and confirmed to parse before it was written down; making that a
/// test would put thirty-odd requests to third parties into every run of the
/// suite and would fail whenever somebody else's server had a bad afternoon.
/// </summary>
public class FeedCatalogTests
{
    [Fact]
    public void Every_address_passes_the_feed_url_policy()
    {
        Assert.Equal(FeedCatalog.All.Count, FeedCatalog.Allowed().Count);
        Assert.All(FeedCatalog.All, feed => Assert.True(
            FeedUrlPolicy.IsAllowed(feed.FeedUrl), feed.FeedUrl));
    }

    [Fact]
    public void Every_site_link_passes_the_feed_url_policy()
    {
        Assert.All(FeedCatalog.All, feed => Assert.True(
            FeedUrlPolicy.IsAllowed(feed.SiteUrl), feed.SiteUrl));
    }

    [Fact]
    public void No_address_appears_twice()
    {
        var duplicates = FeedCatalog.All
            .GroupBy(feed => feed.FeedUrl, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_row_is_filed_under_a_known_category()
    {
        Assert.All(FeedCatalog.All, feed =>
            Assert.Contains(feed.Category, FeedCatalog.Categories));
    }

    /// <summary>
    /// Every category carries something. A heading with no rows under it is a
    /// gap in the dialog rather than a design.
    /// </summary>
    [Fact]
    public void Every_category_has_at_least_one_feed()
    {
        Assert.All(FeedCatalog.Categories, category =>
            Assert.Contains(FeedCatalog.All, feed => feed.Category == category));
    }

    /// <summary>
    /// Modest by design. A starting point long enough to need its own search is
    /// a list nobody reads, and every row is a server somebody may set polling.
    /// </summary>
    [Fact]
    public void The_catalogue_stays_a_few_dozen_entries()
    {
        Assert.InRange(FeedCatalog.All.Count, 12, 60);
    }

    [Fact]
    public void Rows_come_back_grouped_by_category_in_the_stated_order()
    {
        var order = FeedCatalog.Allowed()
            .Select(feed => FeedCatalog.Categories.ToList().IndexOf(feed.Category))
            .ToList();

        Assert.Equal(order.OrderBy(index => index), order);
    }

    /// <summary>
    /// The list was seeded from somebody else's page and says so. A credit that
    /// silently disappears in an edit is the thing worth catching.
    /// </summary>
    [Fact]
    public void The_source_is_credited()
    {
        Assert.Contains("rss.com", FeedCatalog.SourceCredit, StringComparison.OrdinalIgnoreCase);
    }
}
