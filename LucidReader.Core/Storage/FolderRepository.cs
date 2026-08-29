using LucidReader.Core.Model;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

public sealed class FolderRepository(ReaderDatabase db)
{
    public Task<long> AddAsync(string name, long? parentId = null, CancellationToken ct = default) =>
        db.WriteReturningIdAsync(
            "INSERT INTO folders (name, sort_order, parent_id) " +
            "VALUES ($name, (SELECT COALESCE(MAX(sort_order), -1) + 1 FROM folders), $parent);",
            new Dictionary<string, object?> { ["$name"] = name, ["$parent"] = parentId },
            ct);

    public Task<IReadOnlyList<Folder>> GetAllAsync(CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<Folder>>(async connection =>
        {
            var results = new List<Folder>();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM folders ORDER BY sort_order, name;";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(RowMappers.ReadFolder((SqliteDataReader)reader));
            return results;
        }, ct);

    public Task RenameAsync(long id, string name, CancellationToken ct = default) =>
        db.WriteAsync(
            "UPDATE folders SET name = $name WHERE id = $id;",
            new Dictionary<string, object?> { ["$name"] = name, ["$id"] = id },
            ct);

    /// <summary>
    /// Feeds in the folder are moved to the top level, never deleted. Removing a
    /// folder must not silently unsubscribe the user from everything in it.
    /// </summary>
    public Task DeleteAsync(long id, CancellationToken ct = default) =>
        db.WriteAsync(
            "DELETE FROM folders WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = id },
            ct);
}
