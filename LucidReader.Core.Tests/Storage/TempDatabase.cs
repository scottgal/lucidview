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
        SqliteConnection.ClearAllPools();
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
