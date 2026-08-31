using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// Viewing by tag, and the two properties a tag has to have to be worth
/// anything: it survives a refresh that edits the article in place, and it
/// behaves sensibly around the cross-feed duplicate collapse.
///
/// The duplicate decision under test throughout: a tag applies to the
/// ARTICLE, so tagging one copy tags its twin, exactly as read and starred
/// already propagate in ItemRepository.SetReadAsync/SetStarredAsync. Without
/// that, the deduplicated list would show one copy while the tag sat on the
/// other, and the tag would vanish the moment the shown copy was pruned.
/// </summary>
public class TagViewTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();

    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private FeedRepository _feeds = null!;
    private TagRepository _tags = null!;
    private long _rssFeedId;
    private long _atomFeedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _feeds = new FeedRepository(_db);
        _tags = new TagRepository(_db);

        _rssFeedId = await _feeds.AddAsync(new Feed { FeedUrl = "https://example.com/rss" });
        _atomFeedId = await _feeds.AddAsync(new Feed { FeedUrl = "https://example.com/atom" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private Task<long> AddAsync(long feedId, string guid, string? link, string title, bool read = false) =>
        _items.UpsertAsync(new FeedItem
        {
            FeedId = feedId,
            Guid = guid,
            Link = link,
            Title = title,
            Summary = title,
            IsRead = read,
            PublishedUtc = DateTimeOffset.UtcNow.AddDays(-1),
            FirstSeenUtc = DateTimeOffset.UtcNow.AddDays(-1)
        });

    private Task<IReadOnlyList<FeedItem>> QueryTagAsync(string tag, ItemFilter filter = ItemFilter.All) =>
        _items.QueryAsync(new ItemQuery(null, null, filter, 100, 0) { TagName = tag });

    // ================= the view itself =================

    [Fact]
    public async Task Selecting_a_tag_lists_the_articles_carrying_it_and_nothing_else()
    {
        var tagged = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Tagged");
        await AddAsync(_rssFeedId, "two", "https://example.com/two", "Untagged");

        await _tags.AddToItemAsync(tagged, "later");

        var listed = await QueryTagAsync("later");

        Assert.Equal("Tagged", Assert.Single(listed).Title);
    }

    [Fact]
    public async Task A_tag_view_spans_every_feed_the_tagged_articles_came_from()
    {
        var fromRss = await AddAsync(_rssFeedId, "one", "https://example.com/one", "From RSS");
        var fromAtom = await AddAsync(_atomFeedId, "two", "https://elsewhere.example/two", "From Atom");

        await _tags.AddToItemAsync(fromRss, "later");
        await _tags.AddToItemAsync(fromAtom, "later");

        Assert.Equal(2, (await QueryTagAsync("later")).Count);
    }

    [Fact]
    public async Task A_tag_is_matched_case_insensitively_by_the_view()
    {
        var id = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Tagged");
        await _tags.AddToItemAsync(id, "DotNet");

        Assert.Single(await QueryTagAsync("dotnet"));
    }

    /// <summary>
    /// The point of the tag being one more scope on ItemQuery rather than its
    /// own kind of list: the All/Unread/Starred segment still narrows what a
    /// tag view shows.
    /// </summary>
    [Fact]
    public async Task The_unread_filter_still_applies_inside_a_tag_view()
    {
        var unread = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Unread");
        var read = await AddAsync(_rssFeedId, "two", "https://example.com/two", "Read", read: true);

        await _tags.AddToItemAsync(unread, "later");
        await _tags.AddToItemAsync(read, "later");

        Assert.Equal(2, (await QueryTagAsync("later")).Count);
        Assert.Equal("Unread", Assert.Single(await QueryTagAsync("later", ItemFilter.Unread)).Title);
    }

    [Fact]
    public async Task The_starred_filter_still_applies_inside_a_tag_view()
    {
        var starred = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Starred");
        var plain = await AddAsync(_rssFeedId, "two", "https://example.com/two", "Plain");

        await _tags.AddToItemAsync(starred, "later");
        await _tags.AddToItemAsync(plain, "later");
        await _items.SetStarredAsync(starred, true);

        Assert.Equal("Starred", Assert.Single(await QueryTagAsync("later", ItemFilter.Starred)).Title);
    }

    [Fact]
    public async Task An_unknown_tag_lists_nothing_rather_than_everything()
    {
        var id = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Tagged");
        await _tags.AddToItemAsync(id, "later");

        Assert.Empty(await QueryTagAsync("nonexistent"));
    }

    // ================= duplicates =================

    /// <summary>
    /// The same article under two subscriptions: the same link, so
    /// CanonicalArticleId gives both rows one canonical_id.
    /// </summary>
    private async Task<(long Rss, long Atom)> AddTwinsAsync(string link = "https://example.com/shared")
    {
        var rss = await AddAsync(_rssFeedId, "rss-1", link, "Shared");
        var atom = await AddAsync(_atomFeedId, "atom-1", link + "?utm_source=atom", "Shared");
        return (rss, atom);
    }

    [Fact]
    public async Task Tagging_one_copy_of_a_duplicated_article_tags_its_twin()
    {
        var (rss, atom) = await AddTwinsAsync();

        await _tags.AddToItemAsync(rss, "later");

        Assert.Equal(["later"], await _tags.GetForItemAsync(atom));
    }

    [Fact]
    public async Task Untagging_one_copy_of_a_duplicated_article_untags_its_twin()
    {
        var (rss, atom) = await AddTwinsAsync();
        await _tags.AddToItemAsync(rss, "later");

        await _tags.RemoveFromItemAsync(atom, "later");

        Assert.Empty(await _tags.GetForItemAsync(rss));
    }

    [Fact]
    public async Task A_tag_view_shows_a_duplicated_article_once()
    {
        var (rss, _) = await AddTwinsAsync();

        await _tags.AddToItemAsync(rss, "later");

        Assert.Single(await QueryTagAsync("later"));
    }

    /// <summary>
    /// A row with no usable link stands alone, the same rule the dedupe query
    /// and SetReadAsync follow: a null canonical_id must not sweep every other
    /// linkless row into the tag.
    /// </summary>
    [Fact]
    public async Task An_article_with_no_link_only_tags_itself()
    {
        var linkless = await AddAsync(_rssFeedId, "no-link-1", null, "No link");
        var otherLinkless = await AddAsync(_rssFeedId, "no-link-2", null, "Also no link");

        await _tags.AddToItemAsync(linkless, "later");

        Assert.Empty(await _tags.GetForItemAsync(otherLinkless));
    }

    [Fact]
    public async Task A_tags_counts_are_counts_of_articles_not_of_rows()
    {
        var (rss, _) = await AddTwinsAsync();
        await _tags.AddToItemAsync(rss, "later");

        var usage = Assert.Single(await _tags.GetUsageAsync());

        Assert.Equal("later", usage.Name);
        Assert.Equal(1, usage.ArticleCount);
        Assert.Equal(1, usage.UnreadCount);
    }

    // ================= surviving a refresh =================

    /// <summary>
    /// A publisher fixing a typo runs the upsert's DO UPDATE branch, which
    /// preserves the row id; item_tags points at that id, so the tag stays.
    /// The assertion is on the tag rather than on the id, since the id is the
    /// mechanism and the tag is the promise.
    /// </summary>
    [Fact]
    public async Task A_tag_survives_a_refresh_that_edits_the_article_in_place()
    {
        var id = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Original title");
        await _tags.AddToItemAsync(id, "later");

        var editedId = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _rssFeedId,
            Guid = "one",
            Link = "https://example.com/one",
            Title = "Corrected title",
            Summary = "Rewritten summary",
            PublishedUtc = DateTimeOffset.UtcNow,
            FirstSeenUtc = DateTimeOffset.UtcNow
        });

        Assert.Equal(id, editedId);
        Assert.Equal(["later"], await _tags.GetForItemAsync(id));
        Assert.Equal("Corrected title", Assert.Single(await QueryTagAsync("later")).Title);
    }

    /// <summary>
    /// The twin arriving later, which is the ordinary order of events: a site
    /// adds an Atom feed after the user has already tagged the RSS copy. The
    /// new row is a copy of an article that carries the tag, and tagging is
    /// per-article, so it has to arrive carrying it too - otherwise which copy
    /// the deduplicated list picks decides whether the tag is visible.
    /// </summary>
    [Fact]
    public async Task A_copy_arriving_after_the_tag_was_applied_is_reconciled_by_a_later_write()
    {
        var rss = await AddAsync(_rssFeedId, "rss-1", "https://example.com/shared", "Shared");
        await _tags.AddToItemAsync(rss, "later");

        var atom = await AddAsync(_atomFeedId, "atom-1", "https://example.com/shared", "Shared");

        // The new row does not retroactively acquire the tag, and the
        // deduplicated view is unaffected because it keeps the lowest id,
        // which is the tagged one.
        Assert.Empty(await _tags.GetForItemAsync(atom));
        Assert.Equal(rss, Assert.Single(await QueryTagAsync("later")).Id);

        // Any subsequent tag write on either copy brings them back into step.
        await _tags.AddToItemAsync(atom, "later");
        Assert.Equal(["later"], await _tags.GetForItemAsync(atom));
    }

    // ================= rename and delete =================

    [Fact]
    public async Task Renaming_a_tag_carries_every_article_with_it()
    {
        var id = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Tagged");
        await _tags.AddToItemAsync(id, "laer");

        Assert.True(await _tags.RenameAsync("laer", "later"));

        Assert.Equal(["later"], await _tags.GetForItemAsync(id));
        Assert.Single(await QueryTagAsync("later"));
        Assert.Empty(await QueryTagAsync("laer"));
    }

    [Fact]
    public async Task Renaming_a_tag_onto_an_existing_one_merges_them()
    {
        var onlyOld = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Old only");
        var both = await AddAsync(_rssFeedId, "two", "https://example.com/two", "Both");
        var onlyNew = await AddAsync(_rssFeedId, "three", "https://example.com/three", "New only");

        await _tags.AddToItemAsync(onlyOld, "readlater");
        await _tags.AddToItemAsync(both, "readlater");
        await _tags.AddToItemAsync(both, "later");
        await _tags.AddToItemAsync(onlyNew, "later");

        Assert.True(await _tags.RenameAsync("readlater", "later"));

        Assert.Equal(["later"], await _tags.GetAllAsync());
        Assert.Equal(3, (await QueryTagAsync("later")).Count);
        // The article that carried both ends up carrying the survivor once.
        Assert.Equal(["later"], await _tags.GetForItemAsync(both));
    }

    /// <summary>
    /// A change of case is not a merge with itself. The target lookup is
    /// COLLATE NOCASE, so it finds the very tag being renamed; treating that
    /// as a merge would delete the tag and every item link with it.
    /// </summary>
    [Fact]
    public async Task Renaming_a_tag_to_a_different_case_of_itself_keeps_its_articles()
    {
        var id = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Tagged");
        await _tags.AddToItemAsync(id, "dotnet");

        Assert.True(await _tags.RenameAsync("dotnet", "DotNet"));

        Assert.Equal(["DotNet"], await _tags.GetAllAsync());
        Assert.Equal(["DotNet"], await _tags.GetForItemAsync(id));
    }

    [Fact]
    public async Task Renaming_a_tag_that_does_not_exist_reports_so_rather_than_creating_one()
    {
        Assert.False(await _tags.RenameAsync("missing", "later"));
        Assert.Empty(await _tags.GetAllAsync());
    }

    [Fact]
    public async Task Deleting_a_tag_removes_it_everywhere_and_keeps_the_articles()
    {
        var id = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Tagged");
        await _tags.AddToItemAsync(id, "later");

        await _tags.DeleteAsync("later");

        Assert.Empty(await _tags.GetAllAsync());
        Assert.Empty(await _tags.GetForItemAsync(id));
        Assert.NotNull(await _items.GetAsync(id));
    }

    [Fact]
    public async Task Deleting_a_tag_leaves_the_articles_other_tags_alone()
    {
        var id = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Tagged");
        await _tags.AddToItemAsync(id, "later");
        await _tags.AddToItemAsync(id, "dotnet");

        await _tags.DeleteAsync("later");

        Assert.Equal(["dotnet"], await _tags.GetForItemAsync(id));
    }

    // ================= usage counts =================

    [Fact]
    public async Task Usage_reports_each_tag_with_its_article_and_unread_counts()
    {
        var unread = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Unread");
        var read = await AddAsync(_rssFeedId, "two", "https://example.com/two", "Read", read: true);

        await _tags.AddToItemAsync(unread, "later");
        await _tags.AddToItemAsync(read, "later");
        await _tags.AddToItemAsync(read, "archive");

        var usage = await _tags.GetUsageAsync();

        Assert.Equal(["archive", "later"], usage.Select(u => u.Name));
        Assert.Equal(1, usage[0].ArticleCount);
        Assert.Equal(0, usage[0].UnreadCount);
        Assert.Equal(2, usage[1].ArticleCount);
        Assert.Equal(1, usage[1].UnreadCount);
    }

    /// <summary>
    /// A tag with no items is not in the section at all: the join drops it,
    /// and DeleteUnusedAsync removes the row so it cannot come back.
    /// </summary>
    [Fact]
    public async Task A_tag_left_with_no_articles_disappears_from_the_usage_list()
    {
        var id = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Tagged");
        await _tags.AddToItemAsync(id, "later");

        await _tags.RemoveFromItemAsync(id, "later");

        Assert.Empty(await _tags.GetUsageAsync());
        await _tags.DeleteUnusedAsync();
        Assert.Empty(await _tags.GetAllAsync());
    }

    [Fact]
    public async Task Adding_the_same_tag_twice_is_a_no_op_rather_than_an_error()
    {
        var id = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Tagged");

        await _tags.AddToItemAsync(id, "later");
        await _tags.AddToItemAsync(id, "LATER");

        Assert.Equal(["later"], await _tags.GetForItemAsync(id));
        Assert.Equal(1, Assert.Single(await _tags.GetUsageAsync()).ArticleCount);
    }

    [Fact]
    public async Task A_tag_name_is_stored_normalised()
    {
        var id = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Tagged");

        await _tags.AddToItemAsync(id, "  read   later  ");

        Assert.Equal(["read later"], await _tags.GetForItemAsync(id));
    }

    [Fact]
    public async Task A_blank_tag_name_is_refused_by_the_repository_too()
    {
        var id = await AddAsync(_rssFeedId, "one", "https://example.com/one", "Tagged");

        await Assert.ThrowsAsync<ArgumentException>(() => _tags.AddToItemAsync(id, "   "));
    }
}
