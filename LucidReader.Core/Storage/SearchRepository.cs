using LucidReader.Core.Model;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

public sealed class SearchRepository(ReaderDatabase db)
{
    public Task<IReadOnlyList<FeedItem>> SearchAsync(
        string query,
        int limit,
        CancellationToken ct = default)
    {
        var ftsQuery = ToFtsQuery(query);
        if (ftsQuery is null)
            return Task.FromResult<IReadOnlyList<FeedItem>>(Array.Empty<FeedItem>());

        return db.QueryAsync<IReadOnlyList<FeedItem>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT i.* FROM items_fts
                JOIN items i ON i.id = items_fts.rowid
                WHERE items_fts MATCH $query
                ORDER BY rank
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$query", ftsQuery);
            command.Parameters.AddWithValue("$limit", limit);

            var results = new List<FeedItem>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(RowMappers.ReadItem((SqliteDataReader)reader));
            return results;
        }, ct);
    }

    /// <summary>
    /// Turns whatever the user typed into a safe FTS5 query. Every term is
    /// wrapped in double quotes as a phrase literal, with inner quotes doubled,
    /// so a stray quote or parenthesis is searched for rather than parsed as
    /// FTS5 syntax and thrown back as an exception. Returns null for a query
    /// with no usable terms.
    /// </summary>
    private static string? ToFtsQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var terms = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.Trim('"', '(', ')', '*', ':', '^'))
            .Where(term => term.Length > 0)
            .Select(term => "\"" + term.Replace("\"", "\"\"") + "\"")
            .ToList();

        return terms.Count == 0 ? null : string.Join(" ", terms);
    }
}
