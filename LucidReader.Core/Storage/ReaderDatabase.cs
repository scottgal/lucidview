using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Mostlylucid.Ephemeral.Sqlite;

namespace LucidReader.Core.Storage;

/// <summary>
/// Owns the connection string and the single writer. Every write in the app
/// goes through here, which is what keeps SQLite's writer lock uncontended
/// while two coordinators are running.
///
/// IMPORTANT - process-wide sharing: <see cref="SqliteSingleWriter.GetOrCreate"/>
/// returns the SAME writer instance for the same connection string, and
/// <see cref="DisposeAsync"/> disposes that writer unconditionally. Two
/// <see cref="ReaderDatabase"/> instances opened against the same database
/// path therefore silently share one writer, and disposing either instance
/// disposes it out from under the other. Open exactly one
/// <see cref="ReaderDatabase"/> per database path per process, and keep it
/// alive for the app's lifetime rather than opening and disposing it
/// per-operation. <see cref="OpenAsync"/> guards against a second concurrent
/// open of the same path by throwing rather than allowing this to happen
/// silently.
/// </summary>
public sealed class ReaderDatabase : IAsyncDisposable
{
    // Keyed by the same normalised path OpenAsync uses to build the
    // connection string, so two callers pointing at the same database file -
    // even via different relative paths - are recognised as the same open.
    private static readonly ConcurrentDictionary<string, byte> OpenDatabasePaths = new();

    private readonly SqliteSingleWriter _writer;
    private readonly string _openKey;
    private int _disposed;

    private ReaderDatabase(string connectionString, SqliteSingleWriter writer, string openKey)
    {
        ConnectionString = connectionString;
        _writer = writer;
        _openKey = openKey;
    }

    public string ConnectionString { get; }

    public SqliteSingleWriter Writer => _writer;

    /// <summary>
    /// Opens (creating and migrating if needed) the database at
    /// <paramref name="databasePath"/>. Throws <see cref="InvalidOperationException"/>
    /// if a <see cref="ReaderDatabase"/> for this same path is already open in
    /// this process and has not been disposed - see the class remarks for why
    /// that combination is dangerous rather than merely wasteful.
    /// </summary>
    public static async Task<ReaderDatabase> OpenAsync(
        string databasePath,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var openKey = Path.GetFullPath(databasePath);
        if (!OpenDatabasePaths.TryAdd(openKey, 0))
            throw new InvalidOperationException(
                $"A ReaderDatabase for '{openKey}' is already open in this process. " +
                "ReaderDatabase wraps a process-wide shared SqliteSingleWriter " +
                "(see the class remarks); opening the same path a second time " +
                "without disposing the first instance would let disposing either " +
                "one silently dispose the writer the other still depends on. " +
                "Dispose the existing ReaderDatabase before opening a new one " +
                "for this path.");

        try
        {
            // Cache=Shared deliberately not used here: shared-cache mode replaces
            // WAL's normal MVCC readers with SQLite's own in-process table-level
            // locking, and a reader that lands while a write transaction is open
            // gets SQLITE_LOCKED - which, unlike SQLITE_BUSY, busy_timeout does
            // not retry. With two EphemeralWorkCoordinators writing concurrently
            // (FeedRefreshService, OfflineDownloader), a read landing inside
            // another feed's write transaction would throw and be recorded as a
            // false FEED failure, complete with an undeserved backoff step and
            // auto-pause increment. Plain (non-shared) cache mode under WAL keeps
            // reads genuinely concurrent with writes, which is what section 4.4
            // of the design actually requires.
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
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
            return new ReaderDatabase(connectionString, writer, openKey);
        }
        catch
        {
            OpenDatabasePaths.TryRemove(openKey, out _);
            throw;
        }
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

    public ValueTask DisposeAsync()
    {
        // Guards against calling this twice (directly, then again via a
        // wrapping `await using`): only the first call should release the
        // open-path guard and dispose the shared writer.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        OpenDatabasePaths.TryRemove(_openKey, out _);
        return _writer.DisposeAsync();
    }
}
