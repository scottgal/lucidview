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
///
/// Every name entering this class goes through <see cref="TagName"/>, which
/// is where the trimming, whitespace collapsing, character and length rules
/// live and where they are tested. This class is only responsible for
/// storage.
///
/// Adding and removing a tag applies to every copy of the article, not only
/// to the row that was clicked, exactly as ItemRepository.SetReadAsync and
/// SetStarredAsync already do. A tag is a statement about the article; which
/// subscription delivered the row the deduplicated list happened to show is
/// not the user's choice, and if tagging the shown copy left its twin
/// untagged the tag would vanish the moment that copy was pruned or its feed
/// unsubscribed.
/// </summary>
public sealed class TagRepository(ReaderDatabase db)
{
    /// <summary>
    /// Finds or creates the tag atomically. The naive shape - a SELECT through
    /// db.QueryAsync's own short-lived read connection, then a separate
    /// db.WriteReturningIdAsync - has nothing holding the two together: two
    /// concurrent calls for case variants of the same name ("DotNet" and
    /// "dotnet") can both pass the COLLATE NOCASE select before either has
    /// inserted, and then both inserts succeed, because ix_tags_name is a
    /// case-sensitive unique index. That produces exactly the duplicate this
    /// method exists to prevent. Running the select-then-insert inside one
    /// ExecuteInTransactionAsync call closes that window, the same way
    /// AddToItemAsync already does.
    /// </summary>
    public Task<long> GetOrCreateAsync(string name, CancellationToken ct = default)
    {
        var normalised = Normalise(name);

        return db.Writer.ExecuteInTransactionAsync(
            (connection, transaction, innerCt) => GetOrCreateIdAsync(connection, transaction, normalised, innerCt),
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
            var tagId = await GetOrCreateIdAsync(connection, transaction, normalised, innerCt);

            // The composite primary key makes a repeat add a no-op rather than
            // an error, and CopiesOf covers the article's twins as well as the
            // row that was clicked - see this class's doc comment.
            await using var link = connection.CreateCommand();
            link.Transaction = transaction;
            link.CommandText =
                $"""
                 INSERT OR IGNORE INTO item_tags (item_id, tag_id)
                 SELECT id, $tagId FROM items WHERE {CopiesOf};
                 """;
            link.Parameters.AddWithValue("$itemId", itemId);
            link.Parameters.AddWithValue("$tagId", tagId);
            return await link.ExecuteNonQueryAsync(innerCt);
        }, ct);
    }

    public Task RemoveFromItemAsync(long itemId, string tagName, CancellationToken ct = default) =>
        db.WriteAsync(
            $"""
             DELETE FROM item_tags
             WHERE item_id IN (SELECT id FROM items WHERE {CopiesOf})
               AND tag_id IN (SELECT id FROM tags WHERE name = $name COLLATE NOCASE);
             """,
            new Dictionary<string, object?> { ["$itemId"] = itemId, ["$name"] = Normalise(tagName) },
            ct);

    /// <summary>
    /// Every row that is the same article as $itemId: the row itself, plus any
    /// row sharing its canonical_id. A null canonical_id matches nothing, so a
    /// row with no usable link only ever tags itself - the same rule
    /// ItemRepository.SetFlagAcrossCopiesAsync follows, and the same rule the
    /// dedupe in ItemRepository.QueryAsync partitions by.
    /// </summary>
    private const string CopiesOf =
        """
        id = $itemId
        OR canonical_id = (
            SELECT canonical_id FROM items
            WHERE id = $itemId AND canonical_id IS NOT NULL
        )
        """;

    /// <summary>
    /// Every tag in use, with how many articles carry it and how many of those
    /// are unread. This is what the sidebar's Tags section is built from.
    ///
    /// Both counts are counts of ARTICLES, deduplicated by canonical_id the
    /// same way ItemRepository.GetUnreadTotalAsync is, so a tag on an article
    /// carried by two subscriptions counts once - matching what selecting the
    /// tag actually lists. Summing rows instead would put a number beside the
    /// tag that its own list can never reach.
    ///
    /// A tag with no items at all cannot appear here, because the join drops
    /// it. That is deliberate: an empty tag is not something to show, and
    /// DeleteUnusedAsync removes the row itself.
    /// </summary>
    public Task<IReadOnlyList<TagUsage>> GetUsageAsync(CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<TagUsage>>(async connection =>
        {
            var results = new List<TagUsage>();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT t.name,
                       count(DISTINCT COALESCE(i.canonical_id, 'row:' || i.id)) AS article_count,
                       count(DISTINCT CASE WHEN i.is_read = 0
                                           THEN COALESCE(i.canonical_id, 'row:' || i.id)
                                      END) AS unread_count
                FROM tags t
                JOIN item_tags it ON it.tag_id = t.id
                JOIN items i ON i.id = it.item_id
                GROUP BY t.id, t.name
                ORDER BY t.name COLLATE NOCASE;
                """;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(new TagUsage(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
            return results;
        }, ct);

    /// <summary>
    /// Renames a tag across every article carrying it, and returns false when
    /// there was no such tag.
    ///
    /// Renaming onto a name that already exists MERGES rather than failing.
    /// The alternative is an error message about a tag the user may not even
    /// have on screen, and the merge is what they meant: "call these two the
    /// same thing". The item_tags rows are repointed with INSERT OR IGNORE, so
    /// an article that carried both tags ends up carrying the surviving one
    /// once rather than violating the composite primary key, and the old tag
    /// row is then deleted.
    ///
    /// A pure change of case ("dotnet" to "DotNet") is not a merge: the target
    /// lookup is COLLATE NOCASE, so it finds the tag being renamed itself, and
    /// the plain UPDATE below is what runs. Without that check the tag would
    /// be merged into itself and deleted, taking every item link with it.
    /// </summary>
    public Task<bool> RenameAsync(string from, string to, CancellationToken ct = default)
    {
        var oldName = Normalise(from);
        var newName = Normalise(to);

        return db.Writer.ExecuteInTransactionAsync(async (connection, transaction, innerCt) =>
        {
            var oldId = await FindIdAsync(connection, transaction, oldName, innerCt);
            if (oldId is not { } sourceId) return false;

            var targetId = await FindIdAsync(connection, transaction, newName, innerCt);

            if (targetId is { } destinationId && destinationId != sourceId)
            {
                await using (var repoint = connection.CreateCommand())
                {
                    repoint.Transaction = transaction;
                    repoint.CommandText =
                        """
                        INSERT OR IGNORE INTO item_tags (item_id, tag_id)
                        SELECT item_id, $destinationId FROM item_tags WHERE tag_id = $sourceId;
                        """;
                    repoint.Parameters.AddWithValue("$destinationId", destinationId);
                    repoint.Parameters.AddWithValue("$sourceId", sourceId);
                    await repoint.ExecuteNonQueryAsync(innerCt);
                }

                // The item_tags rows still pointing at the source go with it,
                // by the ON DELETE CASCADE the join table was created with.
                await using var drop = connection.CreateCommand();
                drop.Transaction = transaction;
                drop.CommandText = "DELETE FROM tags WHERE id = $sourceId;";
                drop.Parameters.AddWithValue("$sourceId", sourceId);
                await drop.ExecuteNonQueryAsync(innerCt);
                return true;
            }

            await using var rename = connection.CreateCommand();
            rename.Transaction = transaction;
            rename.CommandText = "UPDATE tags SET name = $newName WHERE id = $sourceId;";
            rename.Parameters.AddWithValue("$newName", newName);
            rename.Parameters.AddWithValue("$sourceId", sourceId);
            await rename.ExecuteNonQueryAsync(innerCt);
            return true;
        }, ct);
    }

    /// <summary>
    /// Removes a tag from every article carrying it. The articles themselves
    /// are untouched: this deletes the tags row, and item_tags cascades from
    /// it. Nothing here can reach the items table.
    /// </summary>
    public Task<int> DeleteAsync(string name, CancellationToken ct = default) =>
        db.WriteAsync(
            "DELETE FROM tags WHERE name = $name COLLATE NOCASE;",
            new Dictionary<string, object?> { ["$name"] = Normalise(name) },
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
                results.Add(RowMappers.ReadItem((SqliteDataReader)reader));
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

    /// <summary>
    /// Select-then-insert on the caller's own connection and transaction, so
    /// callers running inside db.Writer.ExecuteInTransactionAsync (GetOrCreateAsync,
    /// AddToItemAsync) get one atomic find-or-create instead of two separate
    /// round trips with a race window between them.
    /// </summary>
    private static async Task<long> GetOrCreateIdAsync(
        SqliteConnection connection, SqliteTransaction transaction, string normalisedName, CancellationToken ct)
    {
        if (await FindIdAsync(connection, transaction, normalisedName, ct) is { } existing)
            return existing;

        await using (var insertTag = connection.CreateCommand())
        {
            insertTag.Transaction = transaction;
            insertTag.CommandText = "INSERT INTO tags (name) VALUES ($name);";
            insertTag.Parameters.AddWithValue("$name", normalisedName);
            await insertTag.ExecuteNonQueryAsync(ct);
        }

        await using var idCommand = connection.CreateCommand();
        idCommand.Transaction = transaction;
        idCommand.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(await idCommand.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// The id of a tag by name, case-insensitively, or null. Takes the
    /// caller's connection and transaction so it can be used inside a larger
    /// atomic sequence.
    /// </summary>
    private static async Task<long?> FindIdAsync(
        SqliteConnection connection, SqliteTransaction transaction, string normalisedName, CancellationToken ct)
    {
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT id FROM tags WHERE name = $name COLLATE NOCASE LIMIT 1;";
        select.Parameters.AddWithValue("$name", normalisedName);
        var found = await select.ExecuteScalarAsync(ct);
        return found is null or DBNull ? null : Convert.ToInt64(found);
    }

    /// <summary>
    /// One gate, so every write in this class stores a name obeying the rules
    /// in <see cref="TagName"/> rather than whatever a caller happened to pass.
    /// </summary>
    private static string Normalise(string name) => TagName.Normalise(name);
}

/// <summary>
/// A tag and what it holds: how many articles carry it, and how many of those
/// are unread. Both are article counts rather than row counts - see
/// <see cref="TagRepository.GetUsageAsync"/>.
/// </summary>
public sealed record TagUsage(string Name, int ArticleCount, int UnreadCount);
