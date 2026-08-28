using LucidReader.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class ReaderDatabaseTests
{
    [Fact]
    public async Task Opening_creates_and_migrates_the_database_file()
    {
        using var temp = new TempDatabase();

        await using var database = await ReaderDatabase.OpenAsync(temp.Path);

        Assert.True(File.Exists(temp.Path));
        var version = await database.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        });
        Assert.Equal(Migrations.All.Count, version);
    }

    [Fact]
    public async Task Opening_creates_missing_parent_directories()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lucidreader-tests", Guid.NewGuid().ToString("N"), "nested");
        var path = Path.Combine(dir, "reader.db");
        try
        {
            await using var database = await ReaderDatabase.OpenAsync(path);
            Assert.True(File.Exists(path));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteReturningIdAsync_gives_back_the_inserted_row_id()
    {
        using var temp = new TempDatabase();
        await using var database = await ReaderDatabase.OpenAsync(temp.Path);

        var id = await database.WriteReturningIdAsync(
            "INSERT INTO folders (name, sort_order) VALUES ($name, $sort);",
            new Dictionary<string, object?> { ["$name"] = "News", ["$sort"] = 0 });

        Assert.True(id > 0);
    }

    [Fact]
    public async Task Concurrent_writes_all_land_without_a_busy_error()
    {
        using var temp = new TempDatabase();
        await using var database = await ReaderDatabase.OpenAsync(temp.Path);

        var writes = Enumerable.Range(0, 50).Select(i =>
            database.WriteAsync(
                "INSERT INTO folders (name, sort_order) VALUES ($name, $sort);",
                new Dictionary<string, object?> { ["$name"] = $"Folder {i}", ["$sort"] = i }));

        await Task.WhenAll(writes);

        var count = await database.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM folders;";
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        });
        Assert.Equal(50L, count);
    }

    [Fact]
    public async Task Foreign_keys_are_enforced_on_the_connections_repositories_use()
    {
        using var temp = new TempDatabase();
        await using var database = await ReaderDatabase.OpenAsync(temp.Path);

        var enforced = await database.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys;";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        });

        Assert.Equal(1, enforced);
    }

    [Fact]
    public async Task Foreign_keys_are_enforced_on_the_write_connection()
    {
        using var temp = new TempDatabase();
        await using var database = await ReaderDatabase.OpenAsync(temp.Path);

        await Assert.ThrowsAsync<SqliteException>(() => database.WriteAsync(
            "INSERT INTO items (feed_id, guid, first_seen_utc) VALUES ($feedId, $guid, $firstSeen);",
            new Dictionary<string, object?>
            {
                ["$feedId"] = 999999,
                ["$guid"] = "does-not-matter",
                ["$firstSeen"] = "2026-08-28T00:00:00Z"
            }));
    }
}
