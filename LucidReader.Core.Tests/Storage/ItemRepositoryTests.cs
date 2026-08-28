using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class ItemRepositoryTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _feedId = await new FeedRepository(_db).AddAsync(
            new Feed { FeedUrl = "https://example.com/feed.xml", Title = "Example" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private FeedItem NewItem(string guid = "guid-1", string title = "Hello") => new()
    {
        FeedId = _feedId,
        Guid = guid,
        Title = title,
        Link = $"https://example.com/{guid}",
        Summary = "A summary.",
        PublishedUtc = DateTimeOffset.Parse("2026-08-28T09:00:00Z"),
        FirstSeenUtc = DateTimeOffset.Parse("2026-08-28T10:00:00Z")
    };

    [Fact]
    public async Task Upserting_a_new_item_inserts_it()
    {
        var id = await _items.UpsertAsync(NewItem());

        var loaded = await _items.GetAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("Hello", loaded!.Title);
        Assert.Equal(OfflineState.None, loaded.OfflineState);
    }

    [Fact]
    public async Task Upserting_the_same_guid_updates_in_place_and_does_not_duplicate()
    {
        var first = await _items.UpsertAsync(NewItem(title: "Original title"));
        var second = await _items.UpsertAsync(NewItem(title: "Corrected title"));

        Assert.Equal(first, second);
        var all = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));
        Assert.Single(all);
        Assert.Equal("Corrected title", all[0].Title);
    }

    [Fact]
    public async Task Re_upserting_preserves_read_and_starred_state()
    {
        var id = await _items.UpsertAsync(NewItem());
        await _items.SetReadAsync(id, true);
        await _items.SetStarredAsync(id, true);

        await _items.UpsertAsync(NewItem(title: "Republished with an edit"));

        var loaded = await _items.GetAsync(id);
        Assert.True(loaded!.IsRead);
        Assert.True(loaded.IsStarred);
        Assert.Equal("Republished with an edit", loaded.Title);
    }

    [Fact]
    public async Task Re_upserting_preserves_downloaded_content()
    {
        var id = await _items.UpsertAsync(NewItem());
        await _items.SetContentAsync(id, "# The full article", ContentSource.Extracted);

        await _items.UpsertAsync(NewItem(title: "Title fixed upstream"));

        var loaded = await _items.GetAsync(id);
        Assert.Equal("# The full article", loaded!.ContentMarkdown);
        Assert.Equal(ContentSource.Extracted, loaded.ContentSource);
    }

    [Fact]
    public async Task UpsertMany_reports_only_the_newly_inserted_count()
    {
        await _items.UpsertAsync(NewItem("guid-1"));

        var inserted = await _items.UpsertManyAsync(new[]
        {
            NewItem("guid-1"),
            NewItem("guid-2"),
            NewItem("guid-3")
        });

        Assert.Equal(2, inserted);
    }

    [Fact]
    public async Task Upserting_a_batch_spanning_two_feeds_is_rejected()
    {
        var otherFeed = await new FeedRepository(_db).AddAsync(
            new Feed { FeedUrl = "https://other.example/feed.xml" });

        await Assert.ThrowsAsync<ArgumentException>(() => _items.UpsertManyAsync(new[]
        {
            NewItem("guid-1"),
            NewItem("guid-2") with { FeedId = otherFeed }
        }));
    }

    /// <summary>
    /// Regression test for the before/after count race: a concurrent delete on the
    /// same feed (standing in for retention pruning, which runs on its own timer)
    /// must not corrupt the newly-inserted count that UpsertManyAsync returns. This
    /// reproduces reliably against the pre-fix implementation, which took its two
    /// counts through separate short-lived read connections outside the write
    /// transaction: it failed on the very first iteration there. With both counts
    /// taken on the transaction's own connection inside the same
    /// ExecuteInTransactionAsync call, the delete (itself a write dispatched through
    /// the same single-writer queue) can only land wholly before or wholly after the
    /// upsert's transaction, never inside it, so the count is unaffected however the
    /// two tasks are scheduled. 200 iterations to keep the race window exercised
    /// repeatedly rather than relying on a single lucky (or unlucky) interleaving.
    /// </summary>
    [Fact]
    public async Task Concurrent_delete_during_a_batch_upsert_does_not_corrupt_the_inserted_count()
    {
        for (var iter = 0; iter < 200; iter++)
        {
            await _items.UpsertAsync(NewItem($"keep-{iter}"));
            await _items.UpsertAsync(NewItem($"doomed-{iter}"));

            var upsertTask = _items.UpsertManyAsync(new[]
            {
                NewItem($"keep-{iter}"),
                NewItem($"new-{iter}-1"),
                NewItem($"new-{iter}-2")
            });
            var deleteTask = _db.WriteAsync(
                "DELETE FROM items WHERE guid = $guid AND feed_id = $feedId;",
                new Dictionary<string, object?> { ["$guid"] = $"doomed-{iter}", ["$feedId"] = _feedId });

            var inserted = await upsertTask;
            await deleteTask;

            Assert.Equal(2, inserted);
        }
    }

    [Fact]
    public async Task The_same_guid_in_two_different_feeds_is_two_items()
    {
        var otherFeed = await new FeedRepository(_db).AddAsync(
            new Feed { FeedUrl = "https://other.example/feed.xml" });

        await _items.UpsertAsync(NewItem("shared-guid"));
        await _items.UpsertAsync(NewItem("shared-guid") with { FeedId = otherFeed });

        var mine = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));
        var theirs = await _items.QueryAsync(new ItemQuery(otherFeed, null, ItemFilter.All, 100, 0));
        Assert.Single(mine);
        Assert.Single(theirs);
    }

    [Fact]
    public async Task Unread_filter_returns_only_unread_items()
    {
        var readId = await _items.UpsertAsync(NewItem("guid-1"));
        await _items.UpsertAsync(NewItem("guid-2"));
        await _items.SetReadAsync(readId, true);

        var unread = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.Unread, 100, 0));

        Assert.Single(unread);
        Assert.Equal("guid-2", unread[0].Guid);
    }

    [Fact]
    public async Task Starred_filter_crosses_feeds()
    {
        var otherFeed = await new FeedRepository(_db).AddAsync(
            new Feed { FeedUrl = "https://other.example/feed.xml" });
        var a = await _items.UpsertAsync(NewItem("guid-1"));
        var b = await _items.UpsertAsync(NewItem("guid-2") with { FeedId = otherFeed });
        await _items.SetStarredAsync(a, true);
        await _items.SetStarredAsync(b, true);

        var starred = await _items.QueryAsync(new ItemQuery(null, null, ItemFilter.Starred, 100, 0));

        Assert.Equal(2, starred.Count);
    }

    [Fact]
    public async Task Items_come_back_newest_first()
    {
        await _items.UpsertAsync(NewItem("old") with
        {
            PublishedUtc = DateTimeOffset.Parse("2026-08-01T09:00:00Z")
        });
        await _items.UpsertAsync(NewItem("new") with
        {
            PublishedUtc = DateTimeOffset.Parse("2026-08-27T09:00:00Z")
        });

        var all = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));

        Assert.Equal("new", all[0].Guid);
        Assert.Equal("old", all[1].Guid);
    }

    [Fact]
    public async Task An_item_with_no_published_date_sorts_by_when_we_first_saw_it()
    {
        await _items.UpsertAsync(NewItem("dated") with
        {
            PublishedUtc = DateTimeOffset.Parse("2026-08-01T09:00:00Z"),
            FirstSeenUtc = DateTimeOffset.Parse("2026-08-01T09:00:00Z")
        });
        await _items.UpsertAsync(NewItem("undated") with
        {
            PublishedUtc = null,
            FirstSeenUtc = DateTimeOffset.Parse("2026-08-28T10:00:00Z")
        });

        var all = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));

        Assert.Equal("undated", all[0].Guid);
    }

    [Fact]
    public async Task Marking_a_whole_feed_read_clears_its_unread_count()
    {
        await _items.UpsertAsync(NewItem("guid-1"));
        await _items.UpsertAsync(NewItem("guid-2"));

        await _items.MarkFeedReadAsync(_feedId);

        Assert.Equal(0, await _items.GetUnreadCountAsync(_feedId));
    }

    [Fact]
    public async Task Pending_offline_items_are_returned_for_download()
    {
        var id = await _items.UpsertAsync(NewItem() with { OfflineState = OfflineState.Pending });
        await _items.UpsertAsync(NewItem("guid-2"));

        var pending = await _items.GetPendingOfflineAsync(limit: 10);

        Assert.Single(pending);
        Assert.Equal(id, pending[0].Id);
    }

    [Fact]
    public async Task A_failed_download_records_the_error_and_keeps_the_summary()
    {
        var id = await _items.UpsertAsync(NewItem() with { OfflineState = OfflineState.Pending });

        await _items.SetOfflineFailedAsync(id, "404 Not Found");

        var loaded = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Failed, loaded!.OfflineState);
        Assert.Equal("404 Not Found", loaded.OfflineError);
        Assert.Equal("A summary.", loaded.Summary);
    }

    [Fact]
    public async Task Deleting_a_feed_deletes_its_items()
    {
        await _items.UpsertAsync(NewItem());

        await new FeedRepository(_db).DeleteAsync(_feedId);

        var all = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));
        Assert.Empty(all);
    }
}
