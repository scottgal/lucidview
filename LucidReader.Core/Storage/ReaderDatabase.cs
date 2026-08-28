using Microsoft.Data.Sqlite;
using Mostlylucid.Ephemeral.Sqlite;

namespace LucidReader.Core.Storage;

/// <summary>
/// Owns the connection string and the single writer. Every write in the app
/// goes through here, which is what keeps SQLite's writer lock uncontended
/// while two coordinators are running.
/// </summary>
public sealed class ReaderDatabase : IAsyncDisposable
{
    private readonly SqliteSingleWriter _writer;

    private ReaderDatabase(string connectionString, SqliteSingleWriter writer)
    {
        ConnectionString = connectionString;
        _writer = writer;
    }

    public string ConnectionString { get; }

    public SqliteSingleWriter Writer => _writer;

    public static async Task<ReaderDatabase> OpenAsync(
        string databasePath,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync(ct);
            await SchemaMigrator.MigrateAsync(connection, ct);
        }

        // EnforceForeignKeys is true by default in SqliteSingleWriterOptions, but it is
        // set explicitly here because the schema depends on it: deleting a folder relies
        // on ON DELETE SET NULL for its feeds, and deleting a feed relies on cascading
        // delete for its items. PRAGMA foreign_keys is per-connection in SQLite, not
        // stored in the database file, so the migration connection above enabling it
        // does nothing for the connections the writer opens later. SqliteSingleWriter
        // reapplies this pragma to every connection it opens (both the single write
        // connection and each short-lived read connection from QueryAsync), which is
        // what actually makes it hold for repositories.
        var options = new SqliteSingleWriterOptions
        {
            EnforceForeignKeys = true,
            EnableWriteAheadLogging = true
        };
        var writer = SqliteSingleWriter.GetOrCreate(connectionString, options);
        return new ReaderDatabase(connectionString, writer);
    }

    public Task<T> QueryAsync<T>(
        Func<SqliteConnection, Task<T>> reader,
        CancellationToken ct = default) =>
        _writer.QueryAsync(reader, ct);

    /// <summary>
    /// Runs a single write through the coordinator's own transaction rather than
    /// SqliteSingleWriter's built-in WriteAsync(sql, dictionary) overload. That overload
    /// always rewrites every dictionary key to "@" + key before binding, which breaks
    /// SQL written with "$name" style placeholders (the convention used throughout this
    /// codebase) since SQLite matches parameter names including the prefix character
    /// exactly. Binding the parameters ourselves, as WriteReturningIdAsync already does,
    /// keeps whatever prefix the caller's SQL actually uses.
    /// </summary>
    public async Task<int> WriteAsync(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default)
    {
        return await _writer.ExecuteInTransactionAsync(async (connection, transaction, innerCt) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (key, value) in parameters)
                command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            return await command.ExecuteNonQueryAsync(innerCt);
        }, ct);
    }

    /// <summary>
    /// Inserts and returns the new row id. The SELECT runs on the writer's own
    /// connection inside the same transaction, so last_insert_rowid() is guaranteed
    /// to be this statement's, not another writer's.
    /// </summary>
    public async Task<long> WriteReturningIdAsync(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default)
    {
        return await _writer.ExecuteInTransactionAsync(async (connection, transaction, innerCt) =>
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (key, value) in parameters)
                    command.Parameters.AddWithValue(key, value ?? DBNull.Value);
                await command.ExecuteNonQueryAsync(innerCt);
            }

            await using var idCommand = connection.CreateCommand();
            idCommand.Transaction = transaction;
            idCommand.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt64(await idCommand.ExecuteScalarAsync(innerCt));
        }, ct);
    }

    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}
