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
        var dir = Path.Combine(Path.GetTempPath(), "mylo-tests", Guid.NewGuid().ToString("N"), "nested");
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

    /// <summary>
    /// Regression test for the whole-branch review's CRITICAL finding:
    /// Cache=Shared replaced WAL's MVCC readers with table-level locking, so a
    /// read landing while a write transaction was open threw
    /// "SQLite Error 6: database table is locked", a failure busy_timeout does
    /// not retry. This holds a write transaction open (via the writer's own
    /// ExecuteInTransactionAsync, the same path every repository write uses)
    /// and proves a concurrent QueryAsync read completes without throwing
    /// while that transaction is still uncommitted.
    /// </summary>
    [Fact]
    public async Task A_read_succeeds_while_a_write_transaction_is_still_open()
    {
        using var temp = new TempDatabase();
        await using var database = await ReaderDatabase.OpenAsync(temp.Path);

        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var writeTask = database.Writer.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO folders (name, sort_order) VALUES ('Held open', 0);";
                await command.ExecuteNonQueryAsync(ct);
            }

            writeStarted.SetResult();
            await releaseWrite.Task;
            return 0;
        });

        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Before the fix this read threw SqliteException ("database table is
        // locked: folders") while the write transaction above was still open.
        var readTask = database.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM folders;";
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        });
        var count = await readTask.WaitAsync(TimeSpan.FromSeconds(10));

        releaseWrite.SetResult();
        await writeTask.WaitAsync(TimeSpan.FromSeconds(10));

        // The uncommitted insert is correctly invisible to the concurrent
        // reader; the point of this test is that the read did not throw.
        Assert.Equal(0L, count);
    }

    [Fact]
    public async Task Opening_the_same_path_twice_without_disposing_the_first_throws()
    {
        using var temp = new TempDatabase();
        await using var first = await ReaderDatabase.OpenAsync(temp.Path);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ReaderDatabase.OpenAsync(temp.Path));
        Assert.Contains("already open", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reopening_a_path_after_disposal_succeeds()
    {
        using var temp = new TempDatabase();
        var first = await ReaderDatabase.OpenAsync(temp.Path);
        await first.DisposeAsync();

        await using var second = await ReaderDatabase.OpenAsync(temp.Path);
        Assert.NotNull(second);
    }

    /// <summary>
    /// Deleting the directory straight after disposal must work. Microsoft.Data.Sqlite
    /// pools connections, so without clearing the pool the file stays open and this
    /// throws IOException on Windows. Unix allows unlinking an open file, so this test
    /// passes there either way; it exists to keep Windows CI honest, and it is the
    /// same failure the legacy profile move would hit.
    /// </summary>
    [Fact]
    public async Task The_database_file_can_be_deleted_immediately_after_disposal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mylo-dispose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var db = await ReaderDatabase.OpenAsync(Path.Combine(dir, "reader.db"));
        await db.DisposeAsync();

        Directory.Delete(dir, recursive: true);
        Assert.False(Directory.Exists(dir));
    }
}
