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
            "lucidreader-tests",
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
        // Clears only the pool(s) keyed to this database's own path, not every
        // pool in the process. SqliteConnection.ClearAllPools() used to be
        // called here, which is a process-wide sweep: xUnit runs test classes
        // in parallel by default, so one test's teardown could tear down a
        // connection another TempDatabase-backed test still had open mid-setup,
        // surfacing as a sporadic ObjectDisposedException deep inside
        // SqliteSingleWriter. ClearPool(connection) only clears the pool for
        // that connection's exact connection string, so it cannot reach another
        // test's database (a different DataSource path is always a different
        // pool key). Two variants are cleared because two different connection
        // strings point at this same file: the plain one below, used by Open(),
        // and the Cache=Shared variant ReaderDatabase.OpenAsync builds from the
        // same path for its own connections.
        using (var plain = new SqliteConnection(ConnectionString))
            SqliteConnection.ClearPool(plain);

        var sharedCacheConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        using (var shared = new SqliteConnection(sharedCacheConnectionString))
            SqliteConnection.ClearPool(shared);

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
