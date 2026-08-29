using LucidReader.Core.Model;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

/// <summary>
/// User tags on items, backed by the `tags` / `item_tags` tables the schema
/// has carried since the Plan 1 migrations.
///
/// Tag names are matched case-insensitively, so "DotNet" and "dotnet" are the
/// same tag. The unique index `ix_tags_name` created in migration V1 is
/// case-sensitive, so the case-insensitive matching is done with COLLATE
/// NOCASE in the queries rather than relying on the index.
/// </summary>
public sealed class TagRepository(ReaderDatabase db)
{
    public async Task<long> GetOrCreateAsync(string name, CancellationToken ct = default)
    {
        var normalised = Normalise(name);

        var existing = await FindIdAsync(normalised, ct);
        if (existing is { } id) return id;

        return await db.WriteReturningIdAsync(
            "INSERT INTO tags (name) VALUES ($name);",
            new Dictionary<string, object?> { ["$name"] = normalised },
            ct);
    }

    public Task<IReadOnlyList<string>> GetAllAsync(CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<string>>(async connection =>
        {
            var names = new List<string>();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM tags ORDER BY name COLLATE NOCASE;";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
            return names;
        }, ct);

    public Task<IReadOnlyList<string>> GetForItemAsync(long itemId, CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<string>>(async connection =>
        {
            var names = new List<string>();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT t.name FROM tags t
                JOIN item_tags it ON it.tag_id = t.id
                WHERE it.item_id = $itemId
                ORDER BY t.name COLLATE NOCASE;
                """;
            command.Parameters.AddWithValue("$itemId", itemId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
            return names;
        }, ct);

    /// <summary>
    /// Finds-or-creates the tag and links it to the item in one transaction via
    /// db.Writer.ExecuteInTransactionAsync, rather than the three separate
    /// non-transactional statements the earlier placeholder used. Everything
    /// below - the SELECT, the tag INSERT and the item_tags INSERT - now runs
    /// on the writer's single connection inside one transaction, so a failure
    /// partway through cannot leave a tag row created without its item link,
    /// or vice versa.
    /// </summary>
    public async Task AddToItemAsync(long itemId, string tagName, CancellationToken ct = default)
    {
        var normalised = Normalise(tagName);

        await db.Writer.ExecuteInTransactionAsync(async (connection, transaction, innerCt) =>
        {
            long tagId;
            await using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = "SELECT id FROM tags WHERE name = $name COLLATE NOCASE LIMIT 1;";
                select.Parameters.AddWithValue("$name", normalised);
                var found = await select.ExecuteScalarAsync(innerCt);

                if (found is null or DBNull)
                {
                    await using var insertTag = connection.CreateCommand();
                    insertTag.Transaction = transaction;
                    insertTag.CommandText = "INSERT INTO tags (name) VALUES ($name);";
                    insertTag.Parameters.AddWithValue("$name", normalised);
                    await insertTag.ExecuteNonQueryAsync(innerCt);

                    await using var idCommand = connection.CreateCommand();
                    idCommand.Transaction = transaction;
                    idCommand.CommandText = "SELECT last_insert_rowid();";
                    tagId = Convert.ToInt64(await idCommand.ExecuteScalarAsync(innerCt));
                }
                else
                {
                    tagId = Convert.ToInt64(found);
                }
            }

            // The composite primary key makes a repeat add a no-op rather than an error.
            await using var link = connection.CreateCommand();
            link.Transaction = transaction;
            link.CommandText = "INSERT OR IGNORE INTO item_tags (item_id, tag_id) VALUES ($itemId, $tagId);";
            link.Parameters.AddWithValue("$itemId", itemId);
            link.Parameters.AddWithValue("$tagId", tagId);
            return await link.ExecuteNonQueryAsync(innerCt);
        }, ct);
    }

    public Task RemoveFromItemAsync(long itemId, string tagName, CancellationToken ct = default) =>
        db.WriteAsync(
            """
            DELETE FROM item_tags
            WHERE item_id = $itemId
              AND tag_id IN (SELECT id FROM tags WHERE name = $name COLLATE NOCASE);
            """,
            new Dictionary<string, object?> { ["$itemId"] = itemId, ["$name"] = Normalise(tagName) },
            ct);

    public Task<IReadOnlyList<FeedItem>> GetItemsWithTagAsync(
        string tagName, int limit, CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<FeedItem>>(async connection =>
        {
            var results = new List<FeedItem>();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT i.* FROM items i
                JOIN item_tags it ON it.item_id = i.id
                JOIN tags t ON t.id = it.tag_id
                WHERE t.name = $name COLLATE NOCASE
                ORDER BY COALESCE(i.published_utc, i.first_seen_utc) DESC, i.id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$name", Normalise(tagName));
            command.Parameters.AddWithValue("$limit", limit);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(RowMappers.ReadItem(reader));
            return results;
        }, ct);

    /// <summary>
    /// Removes tags no item references. Deleting an item cascades its item_tags
    /// rows away but leaves the tag itself behind, so without this the tag list
    /// only ever grows.
    /// </summary>
    public Task<int> DeleteUnusedAsync(CancellationToken ct = default) =>
        db.WriteAsync(
            "DELETE FROM tags WHERE id NOT IN (SELECT DISTINCT tag_id FROM item_tags);",
            new Dictionary<string, object?>(),
            ct);

    private Task<long?> FindIdAsync(string normalisedName, CancellationToken ct) =>
        db.QueryAsync<long?>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM tags WHERE name = $name COLLATE NOCASE LIMIT 1;";
            command.Parameters.AddWithValue("$name", normalisedName);
            var result = await command.ExecuteScalarAsync(ct);
            return result is null or DBNull ? null : Convert.ToInt64(result);
        }, ct);

    private static string Normalise(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("A tag name cannot be blank.", nameof(name));
        return trimmed;
    }
}
