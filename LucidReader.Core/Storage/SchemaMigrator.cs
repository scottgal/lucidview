using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

public static class SchemaMigrator
{
    /// <summary>
    /// Applies any migrations the database has not yet seen and returns the
    /// resulting schema version. Refuses to touch a database written by a
    /// newer version of the app rather than guessing at its shape.
    /// </summary>
    public static async Task<int> MigrateAsync(
        SqliteConnection connection,
        CancellationToken ct = default)
    {
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", ct);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", ct);

        var current = await ReadUserVersionAsync(connection, ct);
        var target = Migrations.All.Count;

        if (current > target)
            throw new InvalidOperationException(
                $"This database was written by a newer version of mylo " +
                $"(schema {current}, this build understands {target}). " +
                $"Upgrade mylo to open it.");

        if (current < target)
        {
            await using var transaction = await connection.BeginTransactionAsync(ct);
            for (var version = current; version < target; version++)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = Migrations.All[version];
                await command.ExecuteNonQueryAsync(ct);
            }

            await using (var versionCommand = connection.CreateCommand())
            {
                versionCommand.Transaction = (SqliteTransaction)transaction;
                // PRAGMA does not accept parameters, and target is an int we control.
                versionCommand.CommandText = $"PRAGMA user_version = {target};";
                await versionCommand.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }

        await EnsureIncrementalVacuumAsync(connection, ct);
        return target;
    }

    /// <summary>
    /// The exception from the most recent failed attempt to convert a database
    /// to incremental auto-vacuum mode, or null if the last attempt (if any)
    /// succeeded or none has run yet. Exposed so a composition root can log it
    /// through whatever logging it has, since this project takes no logging
    /// dependency of its own. Purely diagnostic: nothing in this codebase reads
    /// it to make a decision, and it is not meant to be asserted on by tests
    /// beyond confirming the conversion did not throw out of OpenAsync.
    /// </summary>
    public static Exception? LastIncrementalVacuumConversionError { get; private set; }

    /// <summary>
    /// Puts the database into incremental auto-vacuum mode, so that
    /// "PRAGMA incremental_vacuum" after a prune actually returns freed pages
    /// to the OS instead of leaving deleted rows' space allocated inside the
    /// file forever. SQLite only honours a changed auto_vacuum mode after a
    /// VACUUM, and VACUUM cannot run inside a transaction, so this runs once
    /// here, outside the migration transaction above, rather than on every
    /// prune. For a brand-new database this VACUUM is essentially free; for a
    /// database created before this check existed (every database made by the
    /// app so far) it is a one-time cost paid the next time it is opened,
    /// not a cost paid on the background retention timer.
    ///
    /// Converting needs roughly the database's own size again in free disk
    /// space, since VACUUM writes a full temporary copy before replacing the
    /// original. On a large existing database with a nearly-full disk that can
    /// fail. Before this conversion existed the app opened regardless of free
    /// disk space, so a failure here must not change that: the whole attempt,
    /// including reading the mode back, is contained, and any failure leaves
    /// the database exactly as it was. SQLite does not apply a changed
    /// auto_vacuum pragma to an existing, non-empty database until VACUUM
    /// completes, so a VACUUM that throws partway through has not altered the
    /// on-disk mode; there is nothing to roll back and nothing for the next
    /// open to find half-applied. The next open (the very next launch, since
    /// this runs unconditionally on every OpenAsync) simply sees the mode is
    /// still not incremental and tries again, so this is naturally retried
    /// once conditions allow rather than needing separate retry bookkeeping.
    /// </summary>
    private static async Task EnsureIncrementalVacuumAsync(
        SqliteConnection connection,
        CancellationToken ct)
    {
        const int incrementalMode = 2;
        try
        {
            if (await ReadAutoVacuumModeAsync(connection, ct) == incrementalMode)
            {
                LastIncrementalVacuumConversionError = null;
                return;
            }

            await ExecuteAsync(connection, "PRAGMA auto_vacuum = INCREMENTAL;", ct);
            await ExecuteAsync(connection, "VACUUM;", ct);
            LastIncrementalVacuumConversionError = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Contained deliberately: this is an optional space-reclamation
            // optimization, not a correctness requirement, and OpenAsync must
            // keep working exactly as it did before this conversion existed.
            LastIncrementalVacuumConversionError = ex;
        }
    }

    private static async Task<int> ReadAutoVacuumModeAsync(
        SqliteConnection connection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA auto_vacuum;";
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private static async Task<int> ReadUserVersionAsync(
        SqliteConnection connection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
