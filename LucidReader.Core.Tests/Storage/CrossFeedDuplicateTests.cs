using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// The reader half of the dedupe: two subscriptions carrying the same article
/// produce one row in every list, and acting on that row acts on the article
/// rather than on whichever copy the list happened to pick.
///
/// The two feeds here are deliberately built the way a real double is built -
/// same article link, different guids - because that is exactly what an RSS
/// feed and an Atom feed of one site hand over.
/// </summary>
public class CrossFeedDuplicateTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private long _rssFeed;
    private long _atomFeed;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);

        var feeds = new FeedRepository(_db);
        _rssFeed = await feeds.AddAsync(new Feed { FeedUrl = "https://example.com/rss", Title = "RSS" });
        _atomFeed = await feeds.AddAsync(new Feed { FeedUrl = "https://example.com/atom", Title = "Atom" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private static FeedItem Article(long feedId, string guid, string link, string title = "Shared article") => new()
    {
        FeedId = feedId,
        Guid = guid,
        Link = link,
        Title = title,
        Summary = "A summary.",
        PublishedUtc = DateTimeOffset.Parse("2026-08-28T09:00:00Z"),
        FirstSeenUtc = DateTimeOffset.Parse("2026-08-28T10:00:00Z")
    };

    private Task<long> StoreBothCopiesAsync(string link = "https://example.com/posts/one") =>
        StoreBothCopiesAsync(link, link);

    private async Task<long> StoreBothCopiesAsync(string rssLink, string atomLink)
    {
        var rssId = await _items.UpsertAsync(Article(_rssFeed, "rss-guid-1", rssLink));
        await _items.UpsertAsync(Article(_atomFeed, "tag:example.com,2026:1", atomLink));
        return rssId;
    }

    private Task<IReadOnlyList<FeedItem>> AllItemsAsync(ItemFilter filter = ItemFilter.All) =>
        _items.QueryAsync(new ItemQuery(null, null, filter, 100, 0));

    [Fact]
    public async Task One_article_under_two_feeds_appears_once_in_the_list()
    {
        await StoreBothCopiesAsync();

        var listed = await AllItemsAsync();

        Assert.Single(listed);
    }

    [Fact]
    public async Task Both_copies_are_still_stored()
    {
        await StoreBothCopiesAsync();

        Assert.Equal(1, await _items.GetCountAsync(_rssFeed));
        Assert.Equal(1, await _items.GetCountAsync(_atomFeed));
    }

    /// <summary>
    /// The whole point of the tracking-parameter stripping, exercised end to
    /// end: the same article with feed-specific decoration on its link is one
    /// article.
    /// </summary>
    [Fact]
    public async Task Links_differing_only_by_tracking_parameters_are_one_article()
    {
        await StoreBothCopiesAsync(
            "https://example.com/posts/one?utm_source=rss",
            "https://example.com/posts/one/#comments");

        Assert.Single(await AllItemsAsync());
    }

    [Fact]
    public async Task Two_genuinely_different_articles_both_appear()
    {
        await _items.UpsertAsync(Article(_rssFeed, "a", "https://example.com/posts/one"));
        await _items.UpsertAsync(Article(_atomFeed, "b", "https://example.com/posts/two"));

        Assert.Equal(2, (await AllItemsAsync()).Count);
    }

    /// <summary>
    /// Two items with no link at all are two items. A null identity must never
    /// group rows together, or every link-less item in the database would
    /// collapse into one row.
    /// </summary>
    [Fact]
    public async Task Items_with_no_link_do_not_collapse_into_one()
    {
        await _items.UpsertAsync(Article(_rssFeed, "a", null!) with { Link = null });
        await _items.UpsertAsync(Article(_rssFeed, "b", null!) with { Link = null });

        Assert.Equal(2, (await AllItemsAsync()).Count);
    }

    [Fact]
    public async Task Marking_the_shown_copy_read_does_not_leave_its_twin_unread()
    {
        var shownId = await StoreBothCopiesAsync();

        await _items.SetReadAsync(shownId, true);

        Assert.Equal(0, await _items.GetUnreadCountAsync(_rssFeed));
        Assert.Equal(0, await _items.GetUnreadCountAsync(_atomFeed));
        Assert.Empty(await AllItemsAsync(ItemFilter.Unread));
    }

    [Fact]
    public async Task Marking_read_reports_every_feed_it_changed()
    {
        var shownId = await StoreBothCopiesAsync();

        var affected = await _items.SetReadAsync(shownId, true);

        Assert.Equal(2, affected.Count);
        Assert.Contains(_rssFeed, affected);
        Assert.Contains(_atomFeed, affected);
    }

    [Fact]
    public async Task Marking_read_again_reports_nothing_changed()
    {
        var shownId = await StoreBothCopiesAsync();
        await _items.SetReadAsync(shownId, true);

        Assert.Empty(await _items.SetReadAsync(shownId, true));
    }

    [Fact]
    public async Task Marking_unread_puts_both_copies_back()
    {
        var shownId = await StoreBothCopiesAsync();
        await _items.SetReadAsync(shownId, true);

        await _items.SetReadAsync(shownId, false);

        Assert.Equal(1, await _items.GetUnreadCountAsync(_rssFeed));
        Assert.Equal(1, await _items.GetUnreadCountAsync(_atomFeed));
    }

    [Fact]
    public async Task Starring_the_shown_copy_stars_its_twin()
    {
        var shownId = await StoreBothCopiesAsync();

        var affected = await _items.SetStarredAsync(shownId, true);

        Assert.Equal(2, affected.Count);
        Assert.Single(await AllItemsAsync(ItemFilter.Starred));
    }

    [Fact]
    public async Task An_item_with_no_link_only_ever_updates_itself()
    {
        var lonelyId = await _items.UpsertAsync(Article(_rssFeed, "lonely", null!) with { Link = null });
        await _items.UpsertAsync(Article(_atomFeed, "other", null!) with { Link = null });

        var affected = await _items.SetReadAsync(lonelyId, true);

        Assert.Single(affected);
        Assert.Equal(1, await _items.GetUnreadCountAsync(_atomFeed));
    }

    [Fact]
    public async Task The_unread_total_counts_articles_not_rows()
    {
        await StoreBothCopiesAsync();

        // Two rows, one article.
        Assert.Equal(1, await _items.GetUnreadCountAsync(_rssFeed));
        Assert.Equal(1, await _items.GetUnreadCountAsync(_atomFeed));
        Assert.Equal(1, await _items.GetUnreadTotalAsync());
    }

    [Fact]
    public async Task The_unread_total_can_be_scoped_to_a_folder()
    {
        var folderId = await new FolderRepository(_db).AddAsync("Blogs");
        var feeds = new FeedRepository(_db);
        await feeds.UpdateAsync((await feeds.GetAsync(_rssFeed))! with { FolderId = folderId });
        await feeds.UpdateAsync((await feeds.GetAsync(_atomFeed))! with { FolderId = folderId });

        await StoreBothCopiesAsync();

        Assert.Equal(1, await _items.GetUnreadTotalAsync(folderId));
        Assert.Equal(0, await _items.GetUnreadTotalAsync(folderId + 1000));
    }

    /// <summary>
    /// Marking one feed read has to take the other feed's copies with it, or
    /// the article comes straight back the moment the list is deduplicated
    /// against the other subscription.
    /// </summary>
    [Fact]
    public async Task Marking_a_feed_read_takes_the_other_feeds_copies_with_it()
    {
        await StoreBothCopiesAsync();

        await _items.MarkFeedReadAsync(_rssFeed);

        Assert.Equal(0, await _items.GetUnreadCountAsync(_atomFeed));
    }

    [Fact]
    public async Task Marking_a_feed_read_leaves_unrelated_articles_alone()
    {
        await StoreBothCopiesAsync();
        await _items.UpsertAsync(Article(_atomFeed, "unrelated", "https://example.com/posts/other"));

        await _items.MarkFeedReadAsync(_rssFeed);

        Assert.Equal(1, await _items.GetUnreadCountAsync(_atomFeed));
    }

    /// <summary>
    /// A feed scoped view still shows the feed's own article. The dedupe picks
    /// one row per article WITHIN the query, so narrowing to the feed whose
    /// copy is not the lowest id must not produce an empty list.
    /// </summary>
    [Fact]
    public async Task Search_returns_one_hit_for_an_article_stored_twice()
    {
        await StoreBothCopiesAsync();

        var hits = await new SearchRepository(_db).SearchAsync("Shared", 50);

        Assert.Single(hits);
    }

    [Fact]
    public async Task Search_still_returns_both_of_two_different_articles()
    {
        await _items.UpsertAsync(Article(_rssFeed, "a", "https://example.com/posts/one", "Shared alpha"));
        await _items.UpsertAsync(Article(_atomFeed, "b", "https://example.com/posts/two", "Shared beta"));

        var hits = await new SearchRepository(_db).SearchAsync("Shared", 50);

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public async Task The_later_feed_still_shows_the_article_when_scoped_to_it()
    {
        await StoreBothCopiesAsync();

        var scoped = await _items.QueryAsync(new ItemQuery(_atomFeed, null, ItemFilter.All, 100, 0));

        Assert.Single(scoped);
        Assert.Equal(_atomFeed, scoped[0].FeedId);
    }
}
