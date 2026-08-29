using LucidReader.Core.Model;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

public sealed class ItemRepository(ReaderDatabase db)
{
    /// <summary>
    /// ItemQuery is a positional record struct, so default(ItemQuery) - and
    /// any caller who forgets to set Limit - zero-inits every field including
    /// Limit, silently returning zero rows rather than failing loudly. Struct
    /// defaults cannot be overridden in C#, so an unset (non-positive) Limit is
    /// treated as "use a sane page size" here instead.
    /// </summary>
    private const int DefaultQueryLimit = 200;

    /// <summary>
    /// Inserts, or updates the publisher-owned fields when we have seen this
    /// (feed_id, guid) before. Reader-owned state (read, starred, content we
    /// downloaded, offline state) is deliberately never touched by an upsert:
    /// a publisher fixing a typo must not mark fifty items unread.
    ///
    /// The WHERE NOT EXISTS guard is the other half of retention's tombstone
    /// design (see item_tombstones in Migrations.V2 and RetentionService): a
    /// (feed_id, guid) RetentionService has deliberately pruned must not come
    /// back just because the feed's XML window still lists it. When a
    /// tombstone matches, the SELECT yields no row, so nothing is inserted and
    /// - since nothing was inserted - the ON CONFLICT branch never fires
    /// either, leaving the item deleted. Once the tombstone itself ages out
    /// (RetentionService prunes those on a much longer horizon), the same guid
    /// is ordinary new-item territory again.
    /// </summary>
    private const string UpsertSql =
        """
        INSERT INTO items (
            feed_id, guid, link, title, author, published_utc, updated_utc,
            summary, content_markdown, content_source, is_read, is_starred,
            first_seen_utc, offline_state, offline_error, image_url)
        SELECT
            $feedId, $guid, $link, $title, $author, $published, $updated,
            $summary, $content, $contentSource, $isRead, $isStarred,
            $firstSeen, $offlineState, $offlineError, $imageUrl
        WHERE NOT EXISTS (
            SELECT 1 FROM item_tombstones t
            WHERE t.feed_id = $feedId AND t.guid = $guid
        )
        ON CONFLICT(feed_id, guid) DO UPDATE SET
            link = excluded.link,
            title = excluded.title,
            author = excluded.author,
            published_utc = excluded.published_utc,
            updated_utc = excluded.updated_utc,
            summary = excluded.summary,
            image_url = excluded.image_url;
        """;

    /// <summary>
    /// Returns the row id, or -1 if the item was not written because a
    /// tombstone for this (feed_id, guid) blocked it - see UpsertSql. -1 can
    /// only happen when a caller deliberately upserts an item this database
    /// has just pruned; UpsertManyAsync (the refresh path) does not rely on
    /// this return value at all, so this is reached only by direct callers of
    /// UpsertAsync, chiefly tests.
    /// </summary>
    public async Task<long> UpsertAsync(FeedItem item, CancellationToken ct = default)
    {
        await db.WriteAsync(UpsertSql, BuildParameters(item), ct);
        return await db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM items WHERE feed_id = $feedId AND guid = $guid;";
            command.Parameters.AddWithValue("$feedId", item.FeedId);
            command.Parameters.AddWithValue("$guid", item.Guid);
            var result = await command.ExecuteScalarAsync(ct);
            return result is null ? -1L : Convert.ToInt64(result);
        }, ct);
    }

    /// <summary>
    /// Upserts a batch of items in a single transaction and returns how many
    /// rows were newly inserted, which is what the caller needs in order to
    /// queue only genuinely new items for offline download.
    ///
    /// All items in the batch must belong to the same feed. The inserted count
    /// is computed by counting that feed's rows before and after the batch,
    /// which is only correct when every item shares one feed_id; a batch that
    /// spans multiple feeds is rejected rather than silently mis-counted.
    /// </summary>
    public async Task<int> UpsertManyAsync(
        IReadOnlyList<FeedItem> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0) return 0;

        var feedId = items[0].FeedId;
        if (items.Any(item => item.FeedId != feedId))
        {
            throw new ArgumentException(
                "All items in a batch must belong to the same feed.", nameof(items));
        }

        // SqliteSingleWriter.WriteBatchAsync (Mostlylucid.Ephemeral.Sqlite.SingleWriter 3.0.0)
        // is not usable here: its per-command Parameters is bound via reflection over the
        // object's public properties, always prefixed with "@", so passing our $-prefixed
        // SQL with a Dictionary<string, object?> would silently reflect over the
        // dictionary's own properties (Count, Keys, ...) instead of its entries. Binding the
        // parameters ourselves inside one ExecuteInTransactionAsync call keeps the batch
        // atomic and keeps the $-prefixed SQL convention used throughout this codebase.
        //
        // Both counts are taken on the transaction's own connection, inside the same
        // ExecuteInTransactionAsync call as the upsert loop, so the count-insert-count
        // sequence is atomic against any concurrent writer (retention pruning in
        // particular runs on a timer and can delete rows for this feed between two
        // counts taken outside the transaction).
        return await db.Writer.ExecuteInTransactionAsync(async (connection, transaction, innerCt) =>
        {
            var before = await CountAsync(connection, transaction, feedId, innerCt);

            foreach (var item in items)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = UpsertSql;
                foreach (var (key, value) in BuildParameters(item))
                    command.Parameters.AddWithValue(key, value ?? DBNull.Value);
                await command.ExecuteNonQueryAsync(innerCt);
            }

            var after = await CountAsync(connection, transaction, feedId, innerCt);
            return after - before;
        }, ct);
    }

    public Task<FeedItem?> GetAsync(long id, CancellationToken ct = default) =>
        db.QueryAsync<FeedItem?>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM items WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? RowMappers.ReadItem((SqliteDataReader)reader) : null;
        }, ct);

    public Task<IReadOnlyList<FeedItem>> QueryAsync(
        ItemQuery query,
        CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<FeedItem>>(async connection =>
        {
            var where = new List<string>();
            await using var command = connection.CreateCommand();

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

            var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            // COALESCE so an item with no published date sorts by when we first
            // saw it, rather than sinking to the bottom of the list forever.
            command.CommandText =
                $"""
                 SELECT i.* FROM items i
                 JOIN feeds f ON f.id = i.feed_id
                 {whereClause}
                 ORDER BY COALESCE(i.published_utc, i.first_seen_utc) DESC, i.id DESC
                 LIMIT $limit OFFSET $offset;
                 """;
            command.Parameters.AddWithValue("$limit", query.Limit > 0 ? query.Limit : DefaultQueryLimit);
            command.Parameters.AddWithValue("$offset", query.Offset);

            var results = new List<FeedItem>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(RowMappers.ReadItem((SqliteDataReader)reader));
            return results;
        }, ct);

    public Task<IReadOnlyList<FeedItem>> GetPendingOfflineAsync(
        int limit,
        CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<FeedItem>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT * FROM items
                WHERE offline_state = 1
                ORDER BY COALESCE(published_utc, first_seen_utc) DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);

            var results = new List<FeedItem>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(RowMappers.ReadItem((SqliteDataReader)reader));
            return results;
        }, ct);

    public Task SetReadAsync(long id, bool isRead, CancellationToken ct = default) =>
        db.WriteAsync("UPDATE items SET is_read = $value WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = id, ["$value"] = isRead ? 1 : 0 }, ct);

    public Task SetStarredAsync(long id, bool isStarred, CancellationToken ct = default) =>
        db.WriteAsync("UPDATE items SET is_starred = $value WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = id, ["$value"] = isStarred ? 1 : 0 }, ct);

    public Task MarkFeedReadAsync(long feedId, CancellationToken ct = default) =>
        db.WriteAsync("UPDATE items SET is_read = 1 WHERE feed_id = $feedId AND is_read = 0;",
            new Dictionary<string, object?> { ["$feedId"] = feedId }, ct);

    /// <summary>
    /// imageUrl defaults to null and, when omitted, leaves the column
    /// untouched rather than clobbering a previously-captured image: only
    /// the extracted-page download path in OfflineDownloader ever has an
    /// image to offer, and the feed-summary path must not blank out an
    /// image a prior extracted download already stored for this item.
    /// </summary>
    public Task SetContentAsync(
        long id,
        string markdown,
        ContentSource source,
        string? imageUrl = null,
        CancellationToken ct = default) =>
        db.WriteAsync(
            """
            UPDATE items SET
                content_markdown = $content,
                content_source = $source,
                offline_state = 2,
                offline_error = NULL,
                image_url = COALESCE($imageUrl, image_url)
            WHERE id = $id;
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = id,
                ["$content"] = markdown,
                ["$source"] = (int)source,
                ["$imageUrl"] = imageUrl
            }, ct);

    public Task SetOfflineFailedAsync(long id, string error, CancellationToken ct = default) =>
        db.WriteAsync(
            "UPDATE items SET offline_state = 3, offline_error = $error WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = id, ["$error"] = error }, ct);

    public Task<int> GetUnreadCountAsync(long feedId, CancellationToken ct = default) =>
        db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM items WHERE feed_id = $feedId AND is_read = 0;";
            command.Parameters.AddWithValue("$feedId", feedId);
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }, ct);

    /// <summary>
    /// Counts a feed's rows on the given connection and transaction, rather than
    /// through db.QueryAsync's own short-lived connection, so a caller can take
    /// this count as part of a larger atomic sequence (see UpsertManyAsync).
    /// </summary>
    private static async Task<int> CountAsync(
        SqliteConnection connection, SqliteTransaction transaction, long feedId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM items WHERE feed_id = $feedId;";
        command.Parameters.AddWithValue("$feedId", feedId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private static Dictionary<string, object?> BuildParameters(FeedItem item) => new()
    {
        ["$feedId"] = item.FeedId,
        ["$guid"] = item.Guid,
        ["$link"] = item.Link,
        ["$title"] = item.Title,
        ["$author"] = item.Author,
        ["$published"] = item.PublishedUtc.ToDbString(),
        ["$updated"] = item.UpdatedUtc.ToDbString(),
        ["$summary"] = item.Summary,
        ["$content"] = item.ContentMarkdown,
        ["$contentSource"] = (int)item.ContentSource,
        ["$isRead"] = item.IsRead ? 1 : 0,
        ["$isStarred"] = item.IsStarred ? 1 : 0,
        ["$firstSeen"] = item.FirstSeenUtc.ToDbString(),
        ["$offlineState"] = (int)item.OfflineState,
        ["$offlineError"] = item.OfflineError,
        ["$imageUrl"] = item.ImageUrl
    };
}
