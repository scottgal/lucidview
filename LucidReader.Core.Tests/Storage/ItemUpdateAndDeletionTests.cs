using LucidReader.Core.Maintenance;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// What happens to a stored article after it is first stored: the publisher
/// editing it, the publisher relisting it unchanged, and retention removing
/// it while the feed still lists it.
/// </summary>
public class ItemUpdateAndDeletionTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private FeedRepository _feeds = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _feeds = new FeedRepository(_db);
        _feedId = await _feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private FeedItem Article(string guid = "guid-1", string title = "Original", string? summary = "Original summary") => new()
    {
        FeedId = _feedId,
        Guid = guid,
        Link = "https://example.com/posts/" + guid,
        Title = title,
        Summary = summary,
        PublishedUtc = DateTimeOffset.Parse("2026-08-28T09:00:00Z"),
        FirstSeenUtc = DateTimeOffset.Parse("2026-08-28T10:00:00Z")
    };

    // =====================================================================
    // Updates.
    // =====================================================================

    [Fact]
    public async Task An_edited_title_lands()
    {
        var id = await _items.UpsertAsync(Article());
        await _items.UpsertAsync(Article(title: "Corrected"));

        Assert.Equal("Corrected", (await _items.GetAsync(id))!.Title);
    }

    [Fact]
    public async Task An_edited_summary_lands()
    {
        var id = await _items.UpsertAsync(Article());
        await _items.UpsertAsync(Article(summary: "Rewritten summary"));

        Assert.Equal("Rewritten summary", (await _items.GetAsync(id))!.Summary);
    }

    [Fact]
    public async Task An_edit_keeps_the_read_and_starred_state()
    {
        var id = await _items.UpsertAsync(Article());
        await _items.SetReadAsync(id, true);
        await _items.SetStarredAsync(id, true);

        await _items.UpsertAsync(Article(title: "Corrected"));

        var loaded = await _items.GetAsync(id);
        Assert.True(loaded!.IsRead);
        Assert.True(loaded.IsStarred);
    }

    [Fact]
    public async Task An_edit_keeps_the_downloaded_body_and_the_tags()
    {
        var id = await _items.UpsertAsync(Article());
        await _items.SetContentAsync(id, "# The downloaded body", ContentSource.Extracted);

        var tags = new TagRepository(_db);
        await tags.AddToItemAsync(id, "keep-me");

        await _items.UpsertAsync(Article(title: "Corrected"));

        var loaded = await _items.GetAsync(id);
        Assert.Equal("# The downloaded body", loaded!.ContentMarkdown);
        Assert.Equal(ContentSource.Extracted, loaded.ContentSource);
        Assert.Contains("keep-me", await tags.GetForItemAsync(id));
    }

    [Fact]
    public async Task An_edit_reuses_the_same_row_rather_than_adding_one()
    {
        var first = await _items.UpsertAsync(Article());
        var second = await _items.UpsertAsync(Article(title: "Corrected"));

        Assert.Equal(first, second);
        Assert.Equal(1, await _items.GetCountAsync(_feedId));
    }

    /// <summary>
    /// The definition of "changed". A feed's XML window relists every item on
    /// every fetch, so the identical case is the one that happens thousands of
    /// times; it must not write.
    /// </summary>
    [Fact]
    public async Task Relisting_an_unchanged_item_writes_nothing()
    {
        var id = await _items.UpsertAsync(Article());
        await CountUpdatesFromNowAsync();

        await _items.UpsertAsync(Article());

        Assert.Equal(0, await CountUpdatesFromNowAsync());
        Assert.Equal("Original", (await _items.GetAsync(id))!.Title);
    }

    [Fact]
    public async Task A_changed_item_does_write()
    {
        await _items.UpsertAsync(Article());
        await CountUpdatesFromNowAsync();

        await _items.UpsertAsync(Article(title: "Corrected"));

        Assert.Equal(1, await CountUpdatesFromNowAsync());
    }

    /// <summary>
    /// A publisher moving item bodies from one element to another stops
    /// sending the old one. That is not an edit to null, and it must not erase
    /// what we hold.
    /// </summary>
    [Fact]
    public async Task A_field_the_publisher_stopped_sending_is_not_an_edit()
    {
        var id = await _items.UpsertAsync(Article());
        await CountUpdatesFromNowAsync();

        await _items.UpsertAsync(Article(summary: null));

        Assert.Equal(0, await CountUpdatesFromNowAsync());
        Assert.Equal("Original summary", (await _items.GetAsync(id))!.Summary);
    }

    /// <summary>
    /// How many times a row in items has been updated since the last call, and
    /// resets the count.
    ///
    /// Counted with a trigger of the test's own rather than with SQLite's
    /// total_changes(): that counter belongs to the connection that did the
    /// writing, and every write here goes through the shared single writer
    /// while every read comes back on a different, short-lived connection, so
    /// it reads zero no matter what happened. A trigger lives in the database
    /// and sees the writes whoever made them.
    /// </summary>
    private async Task<int> CountUpdatesFromNowAsync()
    {
        await _db.WriteAsync(
            """
            CREATE TABLE IF NOT EXISTS observed_item_updates (n INTEGER);

            CREATE TRIGGER IF NOT EXISTS observe_item_updates AFTER UPDATE ON items BEGIN
                INSERT INTO observed_item_updates (n) VALUES (1);
            END;
            """,
            new Dictionary<string, object?>());

        var count = await _db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM observed_item_updates;";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        });

        await _db.WriteAsync(
            "DELETE FROM observed_item_updates;", new Dictionary<string, object?>());

        return count;
    }

    // =====================================================================
    // Deletion.
    // =====================================================================

    [Fact]
    public async Task A_pruned_item_is_not_resurrected_by_the_next_poll()
    {
        await SeedAndPruneAsync();

        var reinserted = await _items.UpsertAsync(Article("stale", "Original") with
        {
            PublishedUtc = DateTimeOffset.Parse("2020-01-01T00:00:00Z")
        });

        Assert.Equal(-1, reinserted);
        Assert.Equal(0, await _items.GetCountAsync(_feedId));
    }

    /// <summary>
    /// The same guid arriving with EDITED content is still the same deleted
    /// item. An edit must not be a way back in.
    /// </summary>
    [Fact]
    public async Task An_edit_does_not_resurrect_a_pruned_item()
    {
        await SeedAndPruneAsync();

        await _items.UpsertAsync(Article("stale", "A brand new title") with
        {
            Summary = "Rewritten entirely",
            PublishedUtc = DateTimeOffset.Parse("2020-01-01T00:00:00Z")
        });

        Assert.Equal(0, await _items.GetCountAsync(_feedId));
    }

    [Fact]
    public async Task A_pruned_item_leaves_a_tombstone_behind()
    {
        await SeedAndPruneAsync();

        Assert.Equal(1, await CountTombstonesAsync());
    }

    [Fact]
    public async Task Pruning_does_not_block_a_genuinely_new_item()
    {
        await SeedAndPruneAsync();

        var id = await _items.UpsertAsync(Article("fresh", "A new article"));

        Assert.NotEqual(-1, id);
        Assert.Equal(1, await _items.GetCountAsync(_feedId));
    }

    /// <summary>
    /// Stores one old read article, then prunes with a retention window it is
    /// well outside. Retention writes a tombstone in the same transaction as
    /// the delete, which is what the tests above rely on.
    /// </summary>
    private async Task SeedAndPruneAsync()
    {
        var id = await _items.UpsertAsync(Article("stale", "Original") with
        {
            PublishedUtc = DateTimeOffset.Parse("2020-01-01T00:00:00Z")
        });
        await _items.SetReadAsync(id, true);

        var settings = new ReaderSettings
        {
            KeepReadArticlesDays = 30,
            KeepUnreadForever = true,
            MaxArticlesPerFeed = 0,
            NeverDeleteStarred = true
        };

        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));
        var retention = new RetentionService(_db, _feeds, () => settings, time);

        Assert.Equal(1, await retention.PruneAsync());
    }

    private Task<int> CountTombstonesAsync() =>
        _db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM item_tombstones;";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        });
}
