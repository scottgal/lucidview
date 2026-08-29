using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

/// <summary>
/// Free-text tags on items, backed by the `tags` / `item_tags` tables the
/// schema has carried since the Plan 1 migrations. Task 1 (the composition
/// root) is the first consumer of this type: ReaderServices exposes a
/// TagRepository so the shell can be built against it, but the tagging UI
/// itself is a later task. Keep this minimal and let that task extend it.
/// </summary>
public sealed class TagRepository(ReaderDatabase db)
{
    public Task<IReadOnlyList<string>> GetForItemAsync(long itemId, CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<string>>(async connection =>
        {
            var results = new List<string>();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT t.name FROM tags t " +
                "JOIN item_tags it ON it.tag_id = t.id " +
                "WHERE it.item_id = $itemId ORDER BY t.name;";
            command.Parameters.AddWithValue("$itemId", itemId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(reader.GetString(0));
            return results;
        }, ct);

    public async Task AddToItemAsync(long itemId, string tagName, CancellationToken ct = default)
    {
        // INSERT OR IGNORE followed by a SELECT rather than an UPSERT with
        // last_insert_rowid(): last_insert_rowid() only advances on a genuine
        // insert, not on the DO UPDATE branch of an UPSERT, so relying on it
        // after a conflict would silently return a stale id from an earlier
        // statement in the same transaction.
        //
        // These three statements are NOT wrapped in a single transaction, so a
        // failure or concurrent write between them (e.g. the tag row being
        // deleted between the SELECT and the item_tags insert) can leave
        // things inconsistent in a way FolderRepository/ItemRepository's
        // single-statement methods don't have to worry about. Acceptable for
        // now because this class is a documented placeholder (see the class
        // doc comment): TagRepository did not exist before Task 1 and was
        // added only so ReaderServices.Tags had something to compile against.
        // Whoever owns the tagging UI task should either wrap this in
        // db.Writer's transaction support or confirm the current three-step
        // shape is good enough, rather than assuming it was already reviewed
        // for that.
        await db.WriteAsync(
            "INSERT OR IGNORE INTO tags (name) VALUES ($name);",
            new Dictionary<string, object?> { ["$name"] = tagName },
            ct);

        var tagId = await db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM tags WHERE name = $name;";
            command.Parameters.AddWithValue("$name", tagName);
            return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        }, ct);

        await db.WriteAsync(
            "INSERT OR IGNORE INTO item_tags (item_id, tag_id) VALUES ($itemId, $tagId);",
            new Dictionary<string, object?> { ["$itemId"] = itemId, ["$tagId"] = tagId },
            ct);
    }

    public Task RemoveFromItemAsync(long itemId, string tagName, CancellationToken ct = default) =>
        db.WriteAsync(
            "DELETE FROM item_tags WHERE item_id = $itemId " +
            "AND tag_id = (SELECT id FROM tags WHERE name = $name);",
            new Dictionary<string, object?> { ["$itemId"] = itemId, ["$name"] = tagName },
            ct);
}
