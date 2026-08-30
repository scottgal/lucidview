using LucidReader.Core.Feeds;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

/// <summary>
/// Fills items.canonical_id for rows stored before the column existed, using
/// the same <see cref="CanonicalArticleId"/> function the write path uses.
///
/// This is the second half of the V6 migration, and it is C# rather than SQL
/// for the reason V6's own comment gives: an approximate SQL normalisation
/// would give old rows a different identity from new ones, so the doubles a
/// user already has - the whole reason this column exists - would not pair up.
///
/// Restartable and idempotent. It only ever reads rows WHERE canonical_id IS
/// NULL and only ever writes rows it computed an identity for, so an
/// interrupted run resumes on the next launch and a completed one costs a
/// single indexed scan that finds nothing. Rows whose link cannot produce an
/// identity (no link at all, a mailto:, something unparseable) stay null
/// forever; that is what makes them stand alone in every query, and there are
/// few enough of them that re-reading them on each launch is not a cost worth
/// a second column to avoid.
///
/// Batched so a large database is not one enormous transaction and the first
/// window is not held off by a single write.
/// </summary>
public static class CanonicalIdBackfill
{
    private const int BatchSize = 500;

    /// <summary>
    /// Returns how many rows were given an identity. Runs on the connection it
    /// is handed, so it can be called from the same open that ran the
    /// migration, before the shared writer exists.
    /// </summary>
    public static async Task<int> RunAsync(SqliteConnection connection, CancellationToken ct = default)
    {
        var written = 0;

        // Rows this pass looked at and could not resolve keep their null, so
        // they stay in the result set. They are also, in id order, always ahead
        // of every row not yet looked at, so counting them gives the offset
        // that steps past them. Without it a full batch of unresolvable rows
        // would be re-read forever.
        var unresolved = 0;

        while (true)
        {
            var batch = await ReadBatchAsync(connection, unresolved, ct);
            if (batch.Count == 0) break;

            var updates = new List<(long Id, string Canonical)>(batch.Count);
            foreach (var (id, link) in batch)
            {
                if (CanonicalArticleId.FromLink(link) is { } canonical)
                    updates.Add((id, canonical));
                else
                    unresolved++;
            }

            if (updates.Count > 0)
                await WriteBatchAsync(connection, updates, ct);

            written += updates.Count;

            if (batch.Count < BatchSize) break;
        }

        return written;
    }

    private static async Task<List<(long Id, string? Link)>> ReadBatchAsync(
        SqliteConnection connection, int offset, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, link FROM items
            WHERE canonical_id IS NULL
            ORDER BY id
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", BatchSize);
        command.Parameters.AddWithValue("$offset", offset);

        var rows = new List<(long, string?)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add((reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1)));

        return rows;
    }

    private static async Task WriteBatchAsync(
        SqliteConnection connection,
        IReadOnlyList<(long Id, string Canonical)> updates,
        CancellationToken ct)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE items SET canonical_id = $canonical WHERE id = $id;";
            var canonicalParameter = command.Parameters.Add("$canonical", SqliteType.Text);
            var idParameter = command.Parameters.Add("$id", SqliteType.Integer);

            foreach (var (id, canonical) in updates)
            {
                canonicalParameter.Value = canonical;
                idParameter.Value = id;
                await command.ExecuteNonQueryAsync(ct);
            }
        }

        await transaction.CommitAsync(ct);
    }
}
