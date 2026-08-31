using LucidReader.Core.Feeds;
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
    /// (feed_id, guid) before. Reader-owned state - read, starred, content we
    /// downloaded, offline state, and image_url - is deliberately never
    /// touched by an upsert: a publisher fixing a typo must not mark fifty
    /// items unread. image_url belongs in this group, not with title and
    /// summary, because nothing populates FeedItem.ImageUrl from a parsed
    /// feed; it is set only by OfflineDownloader after reading the article
    /// page (see ItemRepository.SetContentAsync's own imageUrl guard). A
    /// refresh's upsert overwriting it with the always-null value a
    /// freshly-parsed FeedItem carries would silently erase every captured
    /// social-card image on the item's next poll.
    ///
    /// Every publisher-owned column is written with COALESCE(excluded.x, x),
    /// and any nullable column added to this list later must follow the same
    /// rule. FeedParser.RequireIdentity only demands ONE of guid, link or
    /// title, so a null title, link, summary or date is an ordinary parse
    /// result, not a sign the fetch went wrong. A plain
    /// "x = excluded.x" therefore erases what we already hold the moment a
    /// publisher stops emitting a field: moving item bodies from
    /// &lt;description&gt; to &lt;content:encoded&gt; would blank every stored
    /// summary in that feed, and for an item whose download failed or that
    /// was stored while auto-download was off (offline_state = 0, which
    /// GetPendingOfflineAsync never re-queues) the summary is the only body
    /// there is, so the reading pane shows "no content yet" permanently. A
    /// nulled link is worse still: open-original and full-text fetch both
    /// lose the only copy of the address. COALESCE keeps the last non-null
    /// value the publisher ever sent, which is the closest thing to the
    /// truth we have.
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
    ///
    /// The DO UPDATE carries a WHERE of its own, which is what "the publisher
    /// edited this article" means in this codebase: at least one
    /// publisher-owned field would end up different from what is stored. A
    /// poll that brings back a byte-identical item therefore writes nothing at
    /// all, which is the overwhelmingly common case - a feed's XML window
    /// relists every item on every fetch, so without this, every poll rewrote
    /// every row it had ever seen and fired the FTS triggers on each. An edit
    /// still lands, because an edit is exactly the case where the comparison
    /// finds a difference. The comparison uses IS NOT (null-safe) against the
    /// same COALESCE the SET uses, so "the publisher stopped sending this
    /// field" reads as no change rather than as an edit back to null.
    ///
    /// What an edit does not touch is unchanged: is_read, is_starred,
    /// content_markdown, offline_state and image_url are not in the SET list,
    /// the row id is preserved so item_tags rows stay attached, and the
    /// tombstone guard above still stands between an edit and an item that was
    /// deliberately pruned.
    ///
    /// canonical_id is publisher-owned in exactly the same sense as link,
    /// since it is derived from it, so it follows the same COALESCE rule.
    /// </summary>
    private const string UpsertSql =
        """
        INSERT INTO items (
            feed_id, guid, link, title, author, published_utc, updated_utc,
            summary, content_markdown, content_source, is_read, is_starred,
            first_seen_utc, offline_state, offline_error, image_url, canonical_id)
        SELECT
            $feedId, $guid, $link, $title, $author, $published, $updated,
            $summary, $content, $contentSource, $isRead, $isStarred,
            $firstSeen, $offlineState, $offlineError, $imageUrl, $canonicalId
        WHERE NOT EXISTS (
            SELECT 1 FROM item_tombstones t
            WHERE t.feed_id = $feedId AND t.guid = $guid
        )
        ON CONFLICT(feed_id, guid) DO UPDATE SET
            link = COALESCE(excluded.link, link),
            title = COALESCE(excluded.title, title),
            author = COALESCE(excluded.author, author),
            published_utc = COALESCE(excluded.published_utc, published_utc),
            updated_utc = COALESCE(excluded.updated_utc, updated_utc),
            summary = COALESCE(excluded.summary, summary),
            canonical_id = COALESCE(excluded.canonical_id, canonical_id)
        WHERE COALESCE(excluded.link, items.link) IS NOT items.link
           OR COALESCE(excluded.title, items.title) IS NOT items.title
           OR COALESCE(excluded.author, items.author) IS NOT items.author
           OR COALESCE(excluded.published_utc, items.published_utc) IS NOT items.published_utc
           OR COALESCE(excluded.updated_utc, items.updated_utc) IS NOT items.updated_utc
           OR COALESCE(excluded.summary, items.summary) IS NOT items.summary
           OR COALESCE(excluded.canonical_id, items.canonical_id) IS NOT items.canonical_id;
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

            // Applied before the dedupe window below, as one more predicate
            // rather than a different query: the All/Unread/Starred filter,
            // and a feed or folder scope if one is set, all still mean what
            // they mean inside a tag view.
            //
            // Every copy of a tagged article carries the tag (see
            // TagRepository), so whichever copy the ROW_NUMBER picks is a
            // tagged one and the tag view shows the article exactly once,
            // the same single row every other list shows.
            if (query.TagName is { Length: > 0 } tagName)
            {
                where.Add(
                    """
                    i.id IN (
                        SELECT it.item_id FROM item_tags it
                        JOIN tags t ON t.id = it.tag_id
                        WHERE t.name = $tagName COLLATE NOCASE
                    )
                    """);
                command.Parameters.AddWithValue("$tagName", tagName);
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

            // One row per article, not one row per subscription that carries
            // it. A site publishing both an RSS and an Atom feed gives every
            // post two rows under two feed_ids, and they are the same article;
            // the ROW_NUMBER picks the copy that arrived first (lowest id) and
            // drops the rest.
            //
            // The partition key falls back to 'row:' || id, not to
            // canonical_id alone: a null canonical_id means "no usable link,
            // this row stands alone", and PARTITION BY on a bare null would
            // group every such row together and show exactly one of them.
            //
            // Applied inside the query's own WHERE, so the suppression happens
            // within whatever the user is looking at. Scoped to one feed there
            // are normally no duplicates to remove anyway, and the two copies
            // hold the same read and starred state (see SetReadAsync), so
            // which one survives is not visible.
            //
            // COALESCE on the sort so an item with no published date sorts by
            // when we first saw it, rather than sinking to the bottom forever.
            command.CommandText =
                $"""
                 SELECT * FROM (
                     SELECT i.*, ROW_NUMBER() OVER (
                         PARTITION BY COALESCE(i.canonical_id, 'row:' || i.id)
                         ORDER BY i.id
                     ) AS duplicate_rank
                     FROM items i
                     JOIN feeds f ON f.id = i.feed_id
                     {whereClause}
                 )
                 WHERE duplicate_rank = 1
                 ORDER BY COALESCE(published_utc, first_seen_utc) DESC, id DESC
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

    /// <summary>
    /// Marks one article read or unread, and every copy of it stored under
    /// another feed with it. Returns the feed id of each row that actually
    /// changed, one entry per row, so a caller keeping cached unread counts
    /// can adjust every feed the change touched rather than only the one it
    /// clicked in.
    ///
    /// The twin update is the other half of the dedupe in QueryAsync. That
    /// query shows one row per article, and which copy it shows is not the
    /// user's choice; if reading the shown copy left its twin unread, the
    /// article would come straight back the moment the shown copy was pruned
    /// or its feed unsubscribed, and the Unread count would never reach zero.
    ///
    /// A null canonical_id matches nothing here, deliberately: the subselect
    /// yields null, and "canonical_id = null" is not true of any row, so an
    /// item with no usable link only ever updates itself.
    /// </summary>
    public Task<IReadOnlyList<long>> SetReadAsync(long id, bool isRead, CancellationToken ct = default) =>
        SetFlagAcrossCopiesAsync("is_read", id, isRead, ct);

    /// <summary>
    /// Stars or unstars an article and every copy of it, for the same reason
    /// SetReadAsync does: a star is a statement about the article, not about
    /// which subscription happened to deliver it, and the Starred list is one
    /// of the deduplicated views.
    /// </summary>
    public Task<IReadOnlyList<long>> SetStarredAsync(long id, bool isStarred, CancellationToken ct = default) =>
        SetFlagAcrossCopiesAsync("is_starred", id, isStarred, ct);

    /// <summary>
    /// Column is interpolated, which is safe here for the same reason
    /// RetentionService's starred clause is: it is one of two compile-time
    /// constants chosen by the two callers above, never user input.
    /// </summary>
    private Task<IReadOnlyList<long>> SetFlagAcrossCopiesAsync(
        string column, long id, bool value, CancellationToken ct)
    {
        var predicate =
            $"""
             {column} <> $value
             AND (
                 id = $id
                 OR canonical_id = (
                     SELECT canonical_id FROM items
                     WHERE id = $id AND canonical_id IS NOT NULL
                 )
             )
             """;

        return db.Writer.ExecuteInTransactionAsync<IReadOnlyList<long>>(
            async (connection, transaction, innerCt) =>
            {
                // Read the affected feeds before the update, inside the same
                // transaction: afterwards the predicate no longer matches the
                // rows it just changed, and computed outside a transaction the
                // set could differ from the set actually written.
                var feedIds = new List<long>();

                await using (var selectCommand = connection.CreateCommand())
                {
                    selectCommand.Transaction = transaction;
                    selectCommand.CommandText = $"SELECT feed_id FROM items WHERE {predicate};";
                    selectCommand.Parameters.AddWithValue("$id", id);
                    selectCommand.Parameters.AddWithValue("$value", value ? 1 : 0);

                    await using var reader = await selectCommand.ExecuteReaderAsync(innerCt);
                    while (await reader.ReadAsync(innerCt))
                        feedIds.Add(reader.GetInt64(0));
                }

                await using var updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText =
                    $"UPDATE items SET {column} = $value WHERE {predicate};";
                updateCommand.Parameters.AddWithValue("$id", id);
                updateCommand.Parameters.AddWithValue("$value", value ? 1 : 0);
                await updateCommand.ExecuteNonQueryAsync(innerCt);

                return feedIds;
            }, ct);
    }

    /// <summary>
    /// Marks a feed's unread articles read, and every copy of those articles
    /// held under another feed with them, for the reason SetReadAsync gives.
    /// </summary>
    public Task MarkFeedReadAsync(long feedId, CancellationToken ct = default) =>
        db.WriteAsync(
            """
            UPDATE items SET is_read = 1
            WHERE is_read = 0
              AND (
                  feed_id = $feedId
                  OR canonical_id IN (
                      SELECT canonical_id FROM items
                      WHERE feed_id = $feedId AND canonical_id IS NOT NULL
                  )
              );
            """,
            new Dictionary<string, object?> { ["$feedId"] = feedId }, ct);

    /// <summary>
    /// Marks every unread article in every feed read, and returns how many
    /// rows changed.
    ///
    /// The status item's menu is what needs this. Its whole job is to be
    /// reachable when the window is not, so a "mark all read" there that only
    /// applied to whatever feed happened to be selected behind a hidden
    /// window would be the wrong action performed silently.
    ///
    /// "AND is_read = 0" is not redundant: without it every row in the table
    /// is written, and every write to items fires the FTS update trigger. The
    /// V4 migration exists precisely because that trigger used to run on
    /// writes that changed nothing an index cares about.
    /// </summary>
    public Task<int> MarkAllReadAsync(CancellationToken ct = default) =>
        db.WriteAsync("UPDATE items SET is_read = 1 WHERE is_read = 0;",
            new Dictionary<string, object?>(), ct);

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
    /// How many unread ARTICLES there are, not how many unread rows: an
    /// article stored under two subscriptions counts once, matching what the
    /// deduplicated list actually shows. Summing the per-feed counts instead
    /// would put a number in the sidebar that no list can ever get down to.
    ///
    /// folderId scopes it to one folder's feeds; null means every feed. The
    /// same COALESCE fallback as QueryAsync keeps rows with no usable link
    /// counting individually rather than collapsing into one.
    /// </summary>
    public Task<int> GetUnreadTotalAsync(long? folderId = null, CancellationToken ct = default) =>
        db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            var scope = folderId is null ? "" : "AND f.folder_id = $folderId";
            command.CommandText =
                $"""
                 SELECT count(DISTINCT COALESCE(i.canonical_id, 'row:' || i.id))
                 FROM items i
                 JOIN feeds f ON f.id = i.feed_id
                 WHERE i.is_read = 0 {scope};
                 """;
            if (folderId is { } id) command.Parameters.AddWithValue("$folderId", id);
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }, ct);

    /// <summary>
    /// How many stored articles a feed has, for callers that only need the
    /// number (the unsubscribe confirmation, say). Counting in SQL rather than
    /// via QueryAsync matters here: QueryAsync's SELECT i.* pulls every row's
    /// content_markdown, the full article body, so counting through it would
    /// materialise the whole feed's text just to get a total, and would be
    /// capped by whatever page size the caller passed.
    /// </summary>
    public Task<int> GetCountAsync(long feedId, CancellationToken ct = default) =>
        db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM items WHERE feed_id = $feedId;";
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
        ["$imageUrl"] = item.ImageUrl,
        // Recomputed here rather than trusted from the caller, so every row
        // reaching the database carries an identity produced by the one
        // function (CanonicalArticleId) the backfill and the dedupe query also
        // agree on, whatever a caller happened to set on the record.
        ["$canonicalId"] = CanonicalArticleId.FromLink(item.Link)
    };
}
