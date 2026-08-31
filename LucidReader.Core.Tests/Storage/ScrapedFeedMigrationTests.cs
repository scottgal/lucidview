using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// V8 (feeds.source_kind) against a database that already has feeds and items
/// in it, which is the only version of this migration that matters: every
/// existing user has one.
///
/// The database is built by applying V1 to V7 by hand and stamping
/// user_version at 7, so this is genuinely an old database being opened by a
/// new build rather than a new one pretending to be old. Same shape as
/// CanonicalIdMigrationTests, for the same reason.
/// </summary>
public class ScrapedFeedMigrationTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private async Task SeedV7DatabaseAsync()
    {
        await using var connection = _temp.Open();

        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;");

        for (var version = 0; version < 7; version++)
            await ExecuteAsync(connection, Migrations.All[version]);

        await ExecuteAsync(connection, "PRAGMA user_version = 7;");

        await ExecuteAsync(connection,
            """
            INSERT INTO folders (id, name) VALUES (1, 'Reading');

            INSERT INTO feeds (id, folder_id, feed_url, title, consecutive_failures)
            VALUES (1, 1, 'https://example.com/rss', 'Example RSS', 0);
            INSERT INTO feeds (id, feed_url, title, consecutive_failures)
            VALUES (2, 'https://other.example/atom', 'Other Atom', 3);

            INSERT INTO items (id, feed_id, guid, link, title, published_utc, first_seen_utc,
                               is_read, is_starred, canonical_id)
            VALUES (1, 1, 'a', 'https://example.com/one', 'One',
                    '2026-08-01T09:00:00.0000000+00:00', '2026-08-01T10:00:00.0000000+00:00',
                    1, 0, 'https://example.com/one');
            INSERT INTO items (id, feed_id, guid, link, title, published_utc, first_seen_utc,
                               is_read, is_starred, canonical_id)
            VALUES (2, 1, 'b', 'https://example.com/two', 'Two',
                    '2026-08-02T09:00:00.0000000+00:00', '2026-08-02T10:00:00.0000000+00:00',
                    0, 1, 'https://example.com/two');
            INSERT INTO items (id, feed_id, guid, link, title, published_utc, first_seen_utc)
            VALUES (3, 2, 'c', 'https://other.example/three', 'Three',
                    '2026-08-03T09:00:00.0000000+00:00', '2026-08-03T10:00:00.0000000+00:00');

            INSERT INTO tags (id, name) VALUES (1, 'later');
            INSERT INTO item_tags (item_id, tag_id) VALUES (2, 1);

            INSERT INTO item_tombstones (feed_id, guid, deleted_utc)
            VALUES (1, 'pruned', '2026-07-01T00:00:00.0000000+00:00');
            """);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    [Fact]
    public async Task Migrating_a_populated_v7_database_adds_the_column_and_leaves_the_rows_alone()
    {
        await SeedV7DatabaseAsync();

        await using var db = await ReaderDatabase.OpenAsync(_temp.Path);

        await using var connection = _temp.Open();

        Assert.Equal(Migrations.All.Count, await ScalarAsync<int>(connection, "PRAGMA user_version;"));
        Assert.Equal(2, await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM feeds;"));
        Assert.Equal(3, await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM items;"));
        Assert.Equal(1, await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM item_tags;"));
        Assert.Equal(1, await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM item_tombstones;"));

        // Nothing the reader owns may be disturbed by a schema change.
        Assert.Equal(1, await ScalarAsync<int>(connection, "SELECT is_read FROM items WHERE id = 1;"));
        Assert.Equal(1, await ScalarAsync<int>(connection, "SELECT is_starred FROM items WHERE id = 2;"));
        Assert.Equal(3, await ScalarAsync<int>(
            connection, "SELECT consecutive_failures FROM feeds WHERE id = 2;"));
    }

    /// <summary>
    /// The default is the whole safety argument. Every row that existed before
    /// this column did is a published feed, so 0 is the correct value for all
    /// of them rather than a placeholder standing in for an unknown one.
    /// </summary>
    [Fact]
    public async Task Every_existing_feed_becomes_a_published_feed()
    {
        await SeedV7DatabaseAsync();

        await using var db = await ReaderDatabase.OpenAsync(_temp.Path);
        var feeds = new FeedRepository(db);

        foreach (var feed in await feeds.GetAllAsync())
        {
            Assert.Equal(FeedSourceKind.PublishedFeed, feed.SourceKind);
            Assert.False(feed.IsScraped);
        }
    }

    [Fact]
    public async Task A_scraped_feed_round_trips_through_the_repository()
    {
        await using var db = await ReaderDatabase.OpenAsync(_temp.Path);
        var feeds = new FeedRepository(db);

        var scrapedId = await feeds.AddAsync(new Feed
        {
            FeedUrl = "https://news.example/",
            Title = "News",
            SourceKind = FeedSourceKind.ScrapedPage
        });
        var publishedId = await feeds.AddAsync(new Feed { FeedUrl = "https://example.com/rss" });

        Assert.True((await feeds.GetAsync(scrapedId))!.IsScraped);
        Assert.False((await feeds.GetAsync(publishedId))!.IsScraped);
    }

    /// <summary>
    /// The kind is not something the settings dialog edits, so the update path
    /// must not carry it: a save from that dialog writes the columns a user
    /// owns and nothing else. If UpdateAsync ever started writing source_kind
    /// it would silently turn a scraped subscription back into a published one
    /// the first time its refresh interval was changed.
    /// </summary>
    [Fact]
    public async Task Editing_a_feeds_settings_does_not_change_its_kind()
    {
        await using var db = await ReaderDatabase.OpenAsync(_temp.Path);
        var feeds = new FeedRepository(db);

        var id = await feeds.AddAsync(new Feed
        {
            FeedUrl = "https://news.example/",
            SourceKind = FeedSourceKind.ScrapedPage
        });

        var stored = (await feeds.GetAsync(id))!;
        await feeds.UpdateAsync(stored with { RefreshIntervalMinutes = 120, TitleOverride = "Mine" });

        var reread = (await feeds.GetAsync(id))!;
        Assert.True(reread.IsScraped);
        Assert.Equal(120, reread.RefreshIntervalMinutes);
        Assert.Equal("Mine", reread.TitleOverride);
    }
}
