using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// V5 widens items_fts from (title, content_markdown) to
/// (title, author, summary, content_markdown). An FTS5 table's column set
/// cannot be altered, so the migration drops and recreates the table and its
/// three triggers and then rebuilds the index from the items table.
///
/// The one thing that has to be proved is that the rebuild works on a
/// database that already holds articles, since that is every installed copy
/// of the app and the failure mode is silent: a search box that finds
/// nothing, on a database that looks otherwise intact.
/// </summary>
public class FtsColumnMigrationTests
{
    /// <summary>
    /// A database at V4, built by replaying the migrations that shipped before
    /// V5. A fresh database would already have the new columns, so it proves
    /// nothing about upgrading one that does not.
    /// </summary>
    private static async Task MigrateToV4Async(SqliteConnection connection)
    {
        for (var version = 0; version < 4; version++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Migrations.All[version];
            await command.ExecuteNonQueryAsync();
        }

        await using var stamp = connection.CreateCommand();
        stamp.CommandText = "PRAGMA user_version = 4;";
        await stamp.ExecuteNonQueryAsync();
    }

    private static async Task<string> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    [Fact]
    public async Task V5_adds_the_columns_and_keeps_the_update_trigger_narrow()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();
        await MigrateToV4Async(connection);

        await SchemaMigrator.MigrateAsync(connection);

        var table = await ScalarAsync(connection,
            "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'items_fts';");
        Assert.Contains("author", table, StringComparison.Ordinal);
        Assert.Contains("summary", table, StringComparison.Ordinal);

        // Deliberately still unicode61: see the note on Migrations.V5 for the
        // measurement that ruled the porter stemmer out.
        Assert.Contains("unicode61", table, StringComparison.Ordinal);
        Assert.DoesNotContain("porter", table, StringComparison.Ordinal);

        var trigger = await ScalarAsync(connection,
            "SELECT sql FROM sqlite_master WHERE type = 'trigger' AND name = 'items_fts_update';");
        Assert.Contains("AFTER UPDATE OF", trigger, StringComparison.Ordinal);
        Assert.DoesNotContain("is_read", trigger, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rebuild, against a database with rows in it. The summaries seeded
    /// here were written before the index knew the column existed, so finding
    /// them afterwards is only possible if the rebuild actually reindexed
    /// existing rows rather than leaving them behind.
    /// </summary>
    [Fact]
    public async Task V5_reindexes_a_database_that_already_holds_articles()
    {
        using var temp = new TempDatabase();

        await using (var connection = temp.Open())
        {
            await MigrateToV4Async(connection);
            await using var insert = connection.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO feeds (feed_url) VALUES ('https://example.com/feed.xml');
                INSERT INTO items (feed_id, guid, title, author, summary, content_markdown, first_seen_utc)
                VALUES (1, 'guid-1', 'Migration headline', 'Marguerite Yourcenar',
                        'A summary mentioning kingfishers', 'a body about herons',
                        '2026-08-28T10:00:00.0000000+00:00');
                INSERT INTO items (feed_id, guid, title, summary, first_seen_utc)
                VALUES (1, 'guid-2', 'Never downloaded', 'This one only ever had a summary, about puffins.',
                        '2026-08-28T10:00:00.0000000+00:00');
                """;
            await insert.ExecuteNonQueryAsync();

            // Prove the pre-migration state: the summary is not searchable yet.
            var before = await ScalarAsync(connection,
                "SELECT count(*) FROM items_fts WHERE items_fts MATCH '\"kingfishers\"';");
            Assert.Equal("0", before);
        }

        SqliteConnection.ClearPool(new SqliteConnection(temp.ConnectionString));

        await using var db = await ReaderDatabase.OpenAsync(temp.Path);
        var search = new SearchRepository(db);
        var items = new ItemRepository(db);

        // Indexed before the migration, still findable after it.
        Assert.Single(await search.SearchAsync("herons", 10));
        Assert.Single(await search.SearchAsync("Migration", 10));

        // Newly indexed by the rebuild, from rows written long before.
        Assert.Single(await search.SearchAsync("kingfishers", 10));
        Assert.Single(await search.SearchAsync("Yourcenar", 10));
        Assert.Single(await search.SearchAsync("puffins", 10));

        // And the recreated triggers still maintain the index afterwards.
        await items.SetContentAsync(1, "a body about avocets", ContentSource.Extracted);
        Assert.Empty(await search.SearchAsync("herons", 10));
        Assert.Single(await search.SearchAsync("avocets", 10));

        // Reader-owned writes still leave the index alone (V4's property).
        await items.SetReadAsync(1, true);
        await items.SetStarredAsync(1, true);
        Assert.Single(await search.SearchAsync("avocets", 10));
    }

    /// <summary>
    /// An external-content FTS5 table can be left inconsistent by a bad
    /// rebuild in ways a search still half works with, so the index is asked
    /// to check itself.
    /// </summary>
    [Fact]
    public async Task The_rebuilt_index_passes_its_own_integrity_check()
    {
        using var temp = new TempDatabase();

        await using (var connection = temp.Open())
        {
            await MigrateToV4Async(connection);
            await using var insert = connection.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO feeds (feed_url) VALUES ('https://example.com/feed.xml');
                WITH RECURSIVE counter(n) AS (
                    SELECT 1 UNION ALL SELECT n + 1 FROM counter WHERE n < 200
                )
                INSERT INTO items (feed_id, guid, title, summary, content_markdown, first_seen_utc)
                SELECT 1, 'guid-' || n, 'Title ' || n, 'Summary ' || n,
                       'Body ' || n, '2026-08-28T10:00:00.0000000+00:00'
                FROM counter;
                """;
            await insert.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearPool(new SqliteConnection(temp.ConnectionString));

        await using (var db = await ReaderDatabase.OpenAsync(temp.Path))
        {
            Assert.Equal(200, (await new SearchRepository(db).SearchAsync("Summary", 500)).Count);
        }

        SqliteConnection.ClearPool(new SqliteConnection(temp.ConnectionString));

        await using var check = temp.Open();
        await using var command = check.CreateCommand();
        command.CommandText = "INSERT INTO items_fts(items_fts) VALUES('integrity-check');";
        await command.ExecuteNonQueryAsync();
    }
}
