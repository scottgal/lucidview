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

        if (current == target)
            return current;

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
        return target;
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
