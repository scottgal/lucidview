using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// A real SQLite file in a temp directory, deleted on dispose. Not in-memory:
/// WAL and FTS5 behaviour differ there, and those differences are what the
/// storage tests exist to catch.
/// </summary>
public sealed class TempDatabase : IDisposable
{
    public string Path { get; }
    public string ConnectionString { get; }

    public TempDatabase()
    {
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "mylo-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, "reader.db");
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        // Clears only the pool keyed to this database's own path, not every
        // pool in the process. SqliteConnection.ClearAllPools() used to be
        // called here, which is a process-wide sweep: xUnit runs test classes
        // in parallel by default, so one test's teardown could tear down a
        // connection another TempDatabase-backed test still had open mid-setup,
        // surfacing as a sporadic ObjectDisposedException deep inside
        // SqliteSingleWriter. ClearPool(connection) only clears the pool for
        // that connection's exact connection string, so it cannot reach another
        // test's database (a different DataSource path is always a different
        // pool key). Only one connection string variant exists now:
        // ReaderDatabase.OpenAsync used to build a second, Cache=Shared variant
        // from the same path, but shared-cache mode replaces WAL's MVCC readers
        // with table-level locking and was removed (see ReaderDatabase's
        // remarks), so this connection string is the only one that ever points
        // at this file.
        using var plain = new SqliteConnection(ConnectionString);
        SqliteConnection.ClearPool(plain);

        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // A held file handle on Windows is not worth failing a test over.
        }
    }
}
