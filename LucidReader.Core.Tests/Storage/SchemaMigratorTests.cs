using LucidReader.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class SchemaMigratorTests
{
    [Fact]
    public async Task Migrating_a_fresh_database_creates_every_table()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();

        await SchemaMigrator.MigrateAsync(connection);

        var tables = await ReadTableNamesAsync(connection);
        Assert.Contains("folders", tables);
        Assert.Contains("feeds", tables);
        Assert.Contains("items", tables);
        Assert.Contains("tags", tables);
        Assert.Contains("item_tags", tables);
        Assert.Contains("items_fts", tables);
        Assert.Contains("item_tombstones", tables);
    }

    [Fact]
    public async Task Migrating_sets_the_schema_version()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();

        var version = await SchemaMigrator.MigrateAsync(connection);

        Assert.Equal(Migrations.All.Count, version);
    }

    [Fact]
    public async Task Migrating_twice_is_a_no_op()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();

        var first = await SchemaMigrator.MigrateAsync(connection);
        var second = await SchemaMigrator.MigrateAsync(connection);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task A_database_newer_than_the_app_is_refused()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();
        await SchemaMigrator.MigrateAsync(connection);

        // Simulate a database written by a future version of the app.
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA user_version = {Migrations.All.Count + 5};";
            await command.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SchemaMigrator.MigrateAsync(connection));
        Assert.Contains("newer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fts5_is_available_in_the_native_sqlite_build()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();

        await SchemaMigrator.MigrateAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM items_fts WHERE items_fts MATCH 'anything';";
        var result = await command.ExecuteScalarAsync();

        Assert.Equal(0L, Convert.ToInt64(result));
    }

    /// <summary>
    /// Simulates a database written before V2 (tombstones, auto_paused_utc)
    /// existed: applies only the V1 SQL directly and stamps user_version = 1,
    /// bypassing SchemaMigrator so V2 is not silently applied to what is
    /// meant to represent a pre-V2 database. Then runs the real migrator and
    /// checks the pre-existing rows and the new column/table both come
    /// through intact.
    /// </summary>
    [Fact]
    public async Task Migrating_an_existing_V1_database_forward_preserves_its_data()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Migrations.All[0];
            await command.ExecuteNonQueryAsync();
        }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version = 1;";
            await command.ExecuteNonQueryAsync();
        }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO feeds (feed_url, is_enabled) VALUES ('https://example.com/feed.xml', 1);";
            await command.ExecuteNonQueryAsync();
        }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO items (feed_id, guid, title, first_seen_utc)
                VALUES (1, 'guid-1', 'Pre-existing item', '2026-08-28T00:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var version = await SchemaMigrator.MigrateAsync(connection);

        Assert.Equal(Migrations.All.Count, version);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT feed_url FROM feeds WHERE id = 1;";
            Assert.Equal("https://example.com/feed.xml", await command.ExecuteScalarAsync());
        }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT title FROM items WHERE feed_id = 1 AND guid = 'guid-1';";
            Assert.Equal("Pre-existing item", await command.ExecuteScalarAsync());
        }
        await using (var command = connection.CreateCommand())
        {
            // The new nullable column exists and is null for a row that
            // predates it, rather than the ALTER TABLE having failed silently
            // or the row having been dropped.
            command.CommandText = "SELECT auto_paused_utc FROM feeds WHERE id = 1;";
            Assert.Equal(DBNull.Value, await command.ExecuteScalarAsync());
        }

        var tables = await ReadTableNamesAsync(connection);
        Assert.Contains("item_tombstones", tables);
    }

    private static async Task<List<string>> ReadTableNamesAsync(SqliteConnection connection)
    {
        var names = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','view');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }
}
