using LucidReader.Core.Model;
using LucidReader.Core.Storage;

namespace LucidReader.Core.Maintenance;

/// <summary>
/// Deletes old items according to the retention settings. Starred items are
/// exempt from every rule when NeverDeleteStarred is on: a star is the user
/// saying "keep this", and no automatic policy should override that.
///
/// Every deletion here is paired with a tombstone written to item_tombstones
/// in the same transaction. Dedupe on upsert keys off (feed_id, guid) on a
/// LIVE row, so without a tombstone, deleting a row deletes the dedupe key
/// too: the next refresh sees the same guid still listed in the feed's XML
/// window, treats it as brand new, and resurrects it unread with whatever
/// downloaded content it had now gone. Any feed whose window spans longer
/// than its retention period hits this on every prune. The tombstone is what
/// tells ItemRepository's upsert "this exact item was deliberately removed,
/// do not bring it back" (see ItemRepository.UpsertSql). Tombstones are
/// themselves pruned on a much longer horizon than any item retention window
/// (TombstoneRetention below), so the table cannot grow without bound and a
/// guid genuinely reused by the publisher long after the original is gone
/// eventually becomes ordinary new-item territory again.
/// </summary>
public sealed class RetentionService(
    ReaderDatabase db,
    FeedRepository feeds,
    Func<ReaderSettings> settings,
    TimeProvider timeProvider)
{
    /// <summary>
    /// How long a tombstone survives before it is pruned and its guid becomes
    /// eligible to be treated as new again. Deliberately far longer than any
    /// realistic item retention window (the longest offered anywhere in
    /// ReaderSettings is measured in months) so a tombstone reliably outlives
    /// the condition that created it.
    /// </summary>
    private static readonly TimeSpan TombstoneRetention = TimeSpan.FromDays(400);

    public async Task<int> PruneAsync(CancellationToken ct = default)
    {
        var current = settings();
        var now = timeProvider.GetUtcNow();

        // Interpolated rather than parameterised, but safe: this is one of two
        // compile-time constants chosen by a bool, never user input. If this is
        // ever extended to interpolate anything else, that must become a
        // parameter instead.
        var starredClause = current.NeverDeleteStarred ? "AND is_starred = 0" : "";
        var deleted = 0;

        // Read items past their window. Applied per feed rather than as one
        // global DELETE: retention_days is one of the four fields a feed can
        // override (EffectiveFeedSettings.RetentionDays), so a feed with an
        // override must be pruned on its own schedule regardless of the
        // global KeepReadArticlesDays.
        deleted += await PruneReadItemsPerFeedAsync(current, now, starredClause, ct);

        // Unread items, only when the user has asked for a window. No per-feed
        // override exists for this, so it stays a single global rule.
        if (!current.KeepUnreadForever && current.KeepUnreadDays > 0)
        {
            deleted += await TombstoneAndDeleteAsync(
                "is_read = 0 AND COALESCE(published_utc, first_seen_utc) < $cutoff " + starredClause,
                new Dictionary<string, object?>
                {
                    ["$cutoff"] = now.AddDays(-current.KeepUnreadDays).ToDbString(),
                    ["$now"] = now.ToDbString()
                }, ct);
        }

        // Per-feed cap: keep the newest N in each feed, drop the rest. No
        // per-feed override exists for this either.
        if (current.MaxArticlesPerFeed > 0)
        {
            deleted += await TombstoneAndDeleteAsync(
                $$"""
                  id IN (
                      SELECT id FROM (
                          SELECT id,
                                 ROW_NUMBER() OVER (
                                     PARTITION BY feed_id
                                     ORDER BY COALESCE(published_utc, first_seen_utc) DESC
                                 ) AS row_number
                          FROM items
                          WHERE 1 = 1 {{starredClause}}
                      )
                      WHERE row_number > $max
                  )
                  """,
                new Dictionary<string, object?>
                {
                    ["$max"] = current.MaxArticlesPerFeed,
                    ["$now"] = now.ToDbString()
                }, ct);
        }

        await PruneTombstonesAsync(now, ct);

        // Deleting an item cascades its item_tags rows away but leaves the tag
        // row itself, so without this the tag list only ever grows and a tag
        // whose last item was pruned stays in the picker forever.
        // TagRepository is a stateless wrapper over the same database, so it
        // is built here rather than threaded through the composition root.
        if (deleted > 0)
            await new TagRepository(db).DeleteUnusedAsync(ct);

        // A DELETE only marks pages free inside the file; SQLite does not shrink
        // the file itself without help. The database was put into incremental
        // auto-vacuum mode once at startup (SchemaMigrator), so this returns
        // freed pages to the OS now. Unlike a full VACUUM it does not rewrite
        // the whole file or need an exclusive lock beyond a normal write, which
        // is what makes it safe to run here on the background retention timer
        // rather than only at startup. Skipped when nothing was deleted, since
        // there is nothing to reclaim and no point paying even that small cost.
        if (deleted > 0)
        {
            await CompactFullTextIndexAsync(ct);

            await db.WriteAsync(
                "PRAGMA incremental_vacuum;", new Dictionary<string, object?>(), ct);
        }

        // Outside the `deleted > 0` branch on purpose: the write-ahead log
        // grows from writes, not from deletions, and the pass that deletes
        // nothing is exactly the one that follows a week of ordinary
        // refreshing.
        await CheckpointWriteAheadLogAsync(ct);

        return deleted;
    }

    /// <summary>
    /// Merges a bounded number of FTS5 b-tree pages.
    ///
    /// An external-content FTS5 index does not shrink when its rows go. The
    /// delete trigger writes a delete marker into a new segment rather than
    /// removing terms from the old one, so an index over a table that is
    /// pruned every day gets steadily larger and steadily slower to query
    /// while holding steadily less. Only a merge reclaims that.
    ///
    /// 'merge' with a page budget rather than 'optimize', which merges the
    /// entire index into one segment in a single statement: on a large
    /// database that is a long exclusive write, and this runs on a background
    /// timer inside a running app. A bounded merge does a slice of the same
    /// work, and there is another prune along in six hours to do the next
    /// slice.
    ///
    /// Contained rather than propagated. Reclaiming index space is
    /// housekeeping; a prune that deleted rows successfully must not be
    /// reported as failed because the tidy-up afterwards did not run.
    /// </summary>
    private async Task CompactFullTextIndexAsync(CancellationToken ct)
    {
        try
        {
            await db.WriteAsync(
                "INSERT INTO items_fts(items_fts, rank) VALUES('merge', $pages);",
                new Dictionary<string, object?> { ["$pages"] = MergePageBudget }, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* housekeeping only, see the summary above */ }
    }

    /// <summary>
    /// Folds the write-ahead log back into the database file and truncates it.
    ///
    /// SQLite checkpoints automatically once the log passes about a thousand
    /// pages, but a passive checkpoint only ever moves the frames it can and
    /// never shortens the file, so a reader left open for weeks keeps a -wal
    /// sitting at its high-water mark for the rest of the session. TRUNCATE
    /// is the mode that actually returns it. It is skipped, without error,
    /// whenever a reader is active, which is the correct behaviour here: this
    /// is opportunistic, and there is another attempt in six hours.
    /// </summary>
    private async Task CheckpointWriteAheadLogAsync(CancellationToken ct)
    {
        try
        {
            await db.CheckpointWalAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* housekeeping only, see the summary above */ }
    }

    /// <summary>
    /// How many b-tree pages one merge pass is allowed to touch. Large enough
    /// that a day's deletions are absorbed in a pass or two, small enough
    /// that the write it takes is not felt by anyone reading at the time.
    /// </summary>
    private const int MergePageBudget = 64;

    private async Task<int> PruneReadItemsPerFeedAsync(
        ReaderSettings current, DateTimeOffset now, string starredClause, CancellationToken ct)
    {
        var allFeeds = await feeds.GetAllAsync(ct);
        var deleted = 0;

        foreach (var feed in allFeeds)
        {
            var effectiveDays = feed.RetentionDays ?? current.KeepReadArticlesDays;
            if (effectiveDays <= 0) continue;

            deleted += await TombstoneAndDeleteAsync(
                "feed_id = $feedId AND is_read = 1 " +
                "AND COALESCE(published_utc, first_seen_utc) < $cutoff " + starredClause,
                new Dictionary<string, object?>
                {
                    ["$feedId"] = feed.Id,
                    ["$cutoff"] = now.AddDays(-effectiveDays).ToDbString(),
                    ["$now"] = now.ToDbString()
                }, ct);
        }

        return deleted;
    }

    /// <summary>
    /// Writes a tombstone for every item about to be deleted, then deletes
    /// them, atomically in one transaction. Both statements share the exact
    /// same WHERE clause and parameters, so the set of rows tombstoned is
    /// guaranteed to be exactly the set deleted - not computed twice with a
    /// window in which they could disagree. Returns the number of rows
    /// deleted (the tombstone insert's own row count is not interesting to
    /// callers: it is bounded by, and normally equal to, the delete count,
    /// modulo ON CONFLICT updates for a guid tombstoned more than once).
    ///
    /// Deliberately not the simpler "one WriteAsync with two ;-separated
    /// statements" shape used elsewhere: this needs the INSERT's row count
    /// ignored and the DELETE's row count trusted regardless of how the ADO
    /// driver reports affected rows across a batched multi-statement command,
    /// so it drives both statements explicitly on the writer's own
    /// transaction instead (the same pattern ItemRepository.UpsertManyAsync
    /// uses for its own count-sensitive batch).
    /// </summary>
    private Task<int> TombstoneAndDeleteAsync(
        string whereClause,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct) =>
        db.Writer.ExecuteInTransactionAsync(async (connection, transaction, innerCt) =>
        {
            await using (var insertCommand = connection.CreateCommand())
            {
                insertCommand.Transaction = transaction;
                insertCommand.CommandText =
                    $"""
                     INSERT INTO item_tombstones (feed_id, guid, deleted_utc)
                     SELECT feed_id, guid, $now FROM items
                     WHERE {whereClause}
                     ON CONFLICT(feed_id, guid) DO UPDATE SET deleted_utc = excluded.deleted_utc;
                     """;
                foreach (var (key, value) in parameters)
                    insertCommand.Parameters.AddWithValue(key, value ?? DBNull.Value);
                await insertCommand.ExecuteNonQueryAsync(innerCt);
            }

            await using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = $"DELETE FROM items WHERE {whereClause};";
            foreach (var (key, value) in parameters)
                deleteCommand.Parameters.AddWithValue(key, value ?? DBNull.Value);
            return await deleteCommand.ExecuteNonQueryAsync(innerCt);
        }, ct);

    private Task PruneTombstonesAsync(DateTimeOffset now, CancellationToken ct) =>
        db.WriteAsync(
            "DELETE FROM item_tombstones WHERE deleted_utc < $cutoff;",
            new Dictionary<string, object?> { ["$cutoff"] = (now - TombstoneRetention).ToDbString() },
            ct);

    public Task<long> GetDatabaseSizeBytesAsync(CancellationToken ct = default) =>
        db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size();";
            return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        }, ct);
}
