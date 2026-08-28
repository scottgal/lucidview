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
