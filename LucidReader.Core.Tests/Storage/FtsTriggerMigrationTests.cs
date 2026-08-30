using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// V1 created items_fts_update as an unscoped AFTER UPDATE ON items, so
/// marking an article read, starring it, or marking a whole feed read each
/// deleted and reinserted that item's entire content_markdown term list. V4
/// narrows it to the two columns the index mirrors, without touching V1.
/// </summary>
public class FtsTriggerMigrationTests
{
    /// <summary>
    /// A database at V3, built by replaying the migrations this app shipped
    /// before V4 existed, which is the only honest way to test that V4
    /// applies to one: a fresh database would already have the new trigger.
    /// </summary>
    private static async Task<int> MigrateToV3Async(SqliteConnection connection)
    {
        for (var version = 0; version < 3; version++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Migrations.All[version];
            await command.ExecuteNonQueryAsync();
        }

        await using var stamp = connection.CreateCommand();
        stamp.CommandText = "PRAGMA user_version = 3;";
        await stamp.ExecuteNonQueryAsync();
        return 3;
    }

    private static async Task<string> ReadTriggerSqlAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT sql FROM sqlite_master WHERE type = 'trigger' AND name = 'items_fts_update';";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task V4_applies_to_an_existing_V3_database_and_narrows_the_trigger()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();
        await MigrateToV3Async(connection);

        Assert.DoesNotContain("UPDATE OF", await ReadTriggerSqlAsync(connection), StringComparison.Ordinal);

        var version = await SchemaMigrator.MigrateAsync(connection);

        Assert.Equal(Migrations.All.Count, version);
        Assert.Contains(
            "AFTER UPDATE OF title, content_markdown",
            await ReadTriggerSqlAsync(connection),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fresh_database_gets_the_narrowed_trigger_too()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();

        await SchemaMigrator.MigrateAsync(connection);

        Assert.Contains(
            "AFTER UPDATE OF title, content_markdown",
            await ReadTriggerSqlAsync(connection),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Rows written before the migration must still be findable after it, and
    /// the index must still track edits to the columns it mirrors. The
    /// database is opened through ReaderDatabase here rather than migrated by
    /// hand, so this runs the whole real stack.
    /// </summary>
    [Fact]
    public async Task Search_stays_consistent_across_the_migration()
    {
        using var temp = new TempDatabase();

        // Seed at V3, the shape an already-installed copy of the app has.
        await using (var connection = temp.Open())
        {
            await MigrateToV3Async(connection);
            await using var insert = connection.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO feeds (feed_url) VALUES ('https://example.com/feed.xml');
                INSERT INTO items (feed_id, guid, title, content_markdown, first_seen_utc)
                VALUES (1, 'guid-1', 'Migration headline', 'body about kingfishers',
                        '2026-08-28T10:00:00.0000000+00:00');
                """;
            await insert.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearPool(new SqliteConnection(temp.ConnectionString));

        await using var db = await ReaderDatabase.OpenAsync(temp.Path);
        var items = new ItemRepository(db);
        var search = new SearchRepository(db);

        // Indexed before the migration, still findable after it.
        Assert.Single(await search.SearchAsync("kingfishers", 10));

        // Reader-owned writes no longer touch the index, and must not lose it.
        await items.SetReadAsync(1, true);
        await items.SetStarredAsync(1, true);
        await items.MarkFeedReadAsync(1);
        Assert.Single(await search.SearchAsync("kingfishers", 10));

        // An edit to an indexed column is still tracked.
        await items.SetContentAsync(1, "body about herons", ContentSource.Extracted);
        Assert.Empty(await search.SearchAsync("kingfishers", 10));
        Assert.Single(await search.SearchAsync("herons", 10));

        // And so is a publisher's title correction, which arrives by upsert.
        await items.UpsertAsync(new FeedItem
        {
            FeedId = 1,
            Guid = "guid-1",
            Title = "Corrected headline",
            FirstSeenUtc = DateTimeOffset.Parse("2026-08-29T10:00:00Z")
        });
        Assert.Single(await search.SearchAsync("Corrected", 10));
        Assert.Empty(await search.SearchAsync("Migration", 10));
    }
}
