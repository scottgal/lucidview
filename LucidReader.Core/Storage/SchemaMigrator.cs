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
                $"This database was written by a newer version of lucidREADER " +
                $"(schema {current}, this build understands {target}). " +
                $"Upgrade lucidREADER to open it.");

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
    /// </summary>
    private static async Task EnsureIncrementalVacuumAsync(
        SqliteConnection connection,
        CancellationToken ct)
    {
        const int incrementalMode = 2;
        if (await ReadAutoVacuumModeAsync(connection, ct) == incrementalMode)
            return;

        await ExecuteAsync(connection, "PRAGMA auto_vacuum = INCREMENTAL;", ct);
        await ExecuteAsync(connection, "VACUUM;", ct);
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
