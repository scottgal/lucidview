using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

public sealed class SearchRepository(ReaderDatabase db)
{
    /// <summary>
    /// Number of tokens FTS5 puts in a snippet. Twenty is about two lines in
    /// the item list at the list's font size, which is the space the row
    /// gives the preview line, and enough context either side of the match to
    /// show why the row is there.
    /// </summary>
    private const int SnippetTokens = 20;

    /// <summary>
    /// Convenience overload for an unscoped, unfiltered search across every
    /// feed. This is what the toolbar does by default.
    /// </summary>
    public Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        int limit,
        CancellationToken ct = default) =>
        SearchAsync(new SearchQuery(query, null, null, ItemFilter.All, limit), ct);

    /// <summary>
    /// Runs a full-text search and returns each hit with the passage of it
    /// that matched.
    ///
    /// Ordering is bm25 with the column weights in <see cref="SearchRanking"/>
    /// rather than the default "ORDER BY rank", so a headline match beats a
    /// passing mention in paragraph nine. Ties break by date, newest first,
    /// so equally relevant articles come back in the order the rest of the app
    /// lists articles in.
    ///
    /// A query that reduces to no usable term (empty, whitespace, or pure
    /// punctuation) returns nothing without touching the database. Everything
    /// else is made safe by <see cref="FtsQueryBuilder"/> rather than by
    /// catching an exception after the fact.
    /// </summary>
    public Task<IReadOnlyList<SearchHit>> SearchAsync(
        SearchQuery query,
        CancellationToken ct = default)
    {
        var ftsQuery = FtsQueryBuilder.Build(query.Text);
        if (ftsQuery is null)
            return Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());

        return db.QueryAsync<IReadOnlyList<SearchHit>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            var where = new List<string> { "items_fts MATCH $query" };
            command.Parameters.AddWithValue("$query", ftsQuery);

            if (query.FeedId is { } feedId)
            {
                where.Add("i.feed_id = $feedId");
                command.Parameters.AddWithValue("$feedId", feedId);
            }

            if (query.FolderId is { } folderId)
            {
                where.Add("f.folder_id = $folderId");
                command.Parameters.AddWithValue("$folderId", folderId);
            }

            switch (query.Filter)
            {
                case ItemFilter.Unread:
                    where.Add("i.is_read = 0");
                    break;
                case ItemFilter.Starred:
                    where.Add("i.is_starred = 1");
                    break;
            }

            // Column -1 asks FTS5 for the best-matching column rather than a
            // fixed one, so a title hit shows the title and a body hit shows
            // the paragraph it came from. char(1)/char(2) are the match
            // delimiters (SearchHit.MatchStart/MatchEnd); char(8230) is the
            // ellipsis that marks a snippet cut out of a longer passage.
            command.CommandText =
                $"""
                 SELECT i.*,
                        snippet(items_fts, -1, char(1), char(2), char(8230), {SnippetTokens})
                            AS search_snippet
                 FROM items_fts
                 JOIN items i ON i.id = items_fts.rowid
                 JOIN feeds f ON f.id = i.feed_id
                 WHERE {string.Join(" AND ", where)}
                 ORDER BY {SearchRanking.OrderByExpression},
                          COALESCE(i.published_utc, i.first_seen_utc) DESC,
                          i.id DESC
                 LIMIT $limit;
                 """;
            command.Parameters.AddWithValue("$limit", query.Limit > 0 ? query.Limit : 200);

            var results = new List<SearchHit>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var row = (SqliteDataReader)reader;
                results.Add(new SearchHit(
                    RowMappers.ReadItem(row),
                    row.GetNullableString("search_snippet") ?? string.Empty));
            }

            return results;
        }, ct);
    }
}
