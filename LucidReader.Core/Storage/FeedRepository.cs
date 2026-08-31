using LucidReader.Core.Model;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

/// <summary>
/// The feed row as it stands immediately after a failure was recorded, read
/// back from the same statement that wrote it. See
/// <see cref="FeedRepository.RecordFailureAsync"/>.
/// </summary>
public readonly record struct FeedFailureState(bool Found, int ConsecutiveFailures, bool IsEnabled);

public class FeedRepository(ReaderDatabase db)
{
    public virtual Task<long> AddAsync(Feed feed, CancellationToken ct = default) =>
        db.WriteReturningIdAsync(
            """
            INSERT INTO feeds (
                folder_id, feed_url, site_url, title, title_override, icon_path,
                is_enabled, next_due_utc, refresh_interval_minutes, auto_download,
                fetch_full_text, retention_days, source_kind)
            VALUES (
                $folder, $url, $site, $title, $titleOverride, $icon,
                $enabled, $nextDue, $interval, $autoDownload,
                $fullText, $retention, $sourceKind);
            """,
            new Dictionary<string, object?>
            {
                ["$folder"] = feed.FolderId,
                ["$url"] = feed.FeedUrl,
                ["$site"] = feed.SiteUrl,
                ["$title"] = feed.Title,
                ["$titleOverride"] = feed.TitleOverride,
                ["$icon"] = feed.IconPath,
                ["$enabled"] = feed.IsEnabled ? 1 : 0,
                ["$nextDue"] = feed.NextDueUtc.ToDbString(),
                ["$interval"] = feed.RefreshIntervalMinutes,
                ["$autoDownload"] = feed.AutoDownload switch { true => 1, false => 0, null => (object?)null },
                ["$fullText"] = feed.FetchFullText switch { true => 1, false => 0, null => (object?)null },
                ["$retention"] = feed.RetentionDays,
                ["$sourceKind"] = (int)feed.SourceKind
            },
            ct);

    public Task<Feed?> GetAsync(long id, CancellationToken ct = default) =>
        QuerySingleAsync("SELECT * FROM feeds WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = id }, ct);

    public Task<Feed?> GetByUrlAsync(string feedUrl, CancellationToken ct = default) =>
        QuerySingleAsync("SELECT * FROM feeds WHERE feed_url = $url;",
            new Dictionary<string, object?> { ["$url"] = feedUrl }, ct);

    public Task<IReadOnlyList<Feed>> GetAllAsync(CancellationToken ct = default) =>
        QueryManyAsync("SELECT * FROM feeds ORDER BY title, feed_url;",
            new Dictionary<string, object?>(), ct);

    /// <summary>
    /// Feeds whose next_due_utc has passed, plus feeds that have never been
    /// fetched (null next_due). Disabled feeds are excluded, matching the
    /// partial index ix_feeds_next_due.
    ///
    /// Virtual solely so tests can inject a failure (a repository whose
    /// override throws once) to exercise RefreshScheduler's exception
    /// containment. The class stays non-sealed only for that override; every
    /// other member is unchanged production behaviour.
    /// </summary>
    public virtual Task<IReadOnlyList<Feed>> GetDueAsync(
        DateTimeOffset nowUtc, int limit, CancellationToken ct = default) =>
        QueryManyAsync(
            """
            SELECT * FROM feeds
            WHERE is_enabled = 1
              AND (next_due_utc IS NULL OR next_due_utc <= $now)
            ORDER BY next_due_utc IS NOT NULL, next_due_utc
            LIMIT $limit;
            """,
            new Dictionary<string, object?>
            {
                ["$now"] = nowUtc.ToDbString(),
                ["$limit"] = limit
            }, ct);

    /// <summary>
    /// Writes the columns a user edits. Deliberately does not write title or
    /// site_url: those two belong to the publisher, and
    /// UpdateTitleAndSiteUrlAsync exists precisely to own them. A refresh can
    /// adopt a new publisher title at any moment, including while the settings
    /// dialog is open, so a save that carried those columns along would revert
    /// it; a rename the user typed goes to title_override, never here.
    ///
    /// Changing the refresh interval also recomputes next_due_utc, in the same
    /// transaction as the write. Without that, a feed moved from daily to
    /// every fifteen minutes keeps the due time the last fetch calculated
    /// under the old interval, so the new one does not take effect until a day
    /// later. The new due time is measured from the last fetch, which is where
    /// the current one was measured from. Clearing the override entirely
    /// (back to "use the global setting") nulls next_due_utc instead, since
    /// the global interval is not known here and a null simply means the feed
    /// is picked up on the next scheduler pass.
    /// </summary>
    public Task UpdateAsync(Feed feed, CancellationToken ct = default) =>
        db.Writer.ExecuteInTransactionAsync(async (connection, transaction, innerCt) =>
        {
            var previousInterval = await ReadIntervalAsync(connection, transaction, feed.Id, innerCt);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE feeds SET
                    folder_id = $folder,
                    title_override = $titleOverride, icon_path = $icon,
                    is_enabled = $enabled,
                    refresh_interval_minutes = $interval, auto_download = $autoDownload,
                    fetch_full_text = $fullText, retention_days = $retention
                WHERE id = $id;
                """;
            var parameters = new Dictionary<string, object?>
            {
                ["$id"] = feed.Id,
                ["$folder"] = feed.FolderId,
                ["$titleOverride"] = feed.TitleOverride,
                ["$icon"] = feed.IconPath,
                ["$enabled"] = feed.IsEnabled ? 1 : 0,
                ["$interval"] = feed.RefreshIntervalMinutes,
                ["$autoDownload"] = feed.AutoDownload switch { true => 1, false => 0, null => (object?)null },
                ["$fullText"] = feed.FetchFullText switch { true => 1, false => 0, null => (object?)null },
                ["$retention"] = feed.RetentionDays
            };
            foreach (var (key, value) in parameters)
                command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            var rows = await command.ExecuteNonQueryAsync(innerCt);

            if (previousInterval.Found && previousInterval.Minutes != feed.RefreshIntervalMinutes)
            {
                await RescheduleForIntervalAsync(
                    connection, transaction, feed.Id, feed.RefreshIntervalMinutes, innerCt);
            }

            return rows;
        }, ct);

    private static async Task<(bool Found, int? Minutes)> ReadIntervalAsync(
        SqliteConnection connection, SqliteTransaction transaction, long feedId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT refresh_interval_minutes FROM feeds WHERE id = $id;";
        command.Parameters.AddWithValue("$id", feedId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (false, null);
        return (true, reader.IsDBNull(0) ? null : reader.GetInt32(0));
    }

    /// <summary>
    /// Moves next_due_utc onto the interval that has just been set. A feed
    /// with no fetch behind it, or one now inheriting the global interval,
    /// gets a null due time, which GetDueAsync treats as due now.
    /// </summary>
    private static async Task RescheduleForIntervalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long feedId,
        int? intervalMinutes,
        CancellationToken ct)
    {
        DateTimeOffset? nextDue = null;

        if (intervalMinutes is > 0)
        {
            await using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT last_fetched_utc FROM feeds WHERE id = $id;";
            read.Parameters.AddWithValue("$id", feedId);
            var lastFetched = await read.ExecuteScalarAsync(ct);

            if (lastFetched is string text
                && DateTimeOffset.TryParse(
                    text, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                nextDue = parsed.AddMinutes(intervalMinutes.Value);
        }

        await using var write = connection.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = "UPDATE feeds SET next_due_utc = $nextDue WHERE id = $id;";
        write.Parameters.AddWithValue("$id", feedId);
        write.Parameters.AddWithValue("$nextDue", (object?)nextDue.ToDbString() ?? DBNull.Value);
        await write.ExecuteNonQueryAsync(ct);
    }

    public Task RecordSuccessAsync(
        long feedId, string? etag, string? lastModified,
        DateTimeOffset nowUtc, DateTimeOffset nextDueUtc, CancellationToken ct = default) =>
        db.WriteAsync(
            """
            UPDATE feeds SET
                last_fetched_utc = $now, last_success_utc = $now,
                etag = $etag, last_modified = $lastModified,
                consecutive_failures = 0, last_error = NULL,
                next_due_utc = $nextDue
            WHERE id = $id;
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = feedId,
                ["$now"] = nowUtc.ToDbString(),
                ["$etag"] = etag,
                ["$lastModified"] = lastModified,
                ["$nextDue"] = nextDueUtc.ToDbString()
            },
            ct);

    /// <summary>
    /// Records one failure and returns the row as it stands afterwards.
    ///
    /// The counter is incremented in SQL, so the returned count is the true
    /// one even when two refreshes of the same feed overlap. The caller must
    /// decide auto-pause from what comes back rather than from its own Feed
    /// snapshot: that snapshot can be a minute old, and two concurrent
    /// refreshes both reading 3 would each compute 4 while the database
    /// reached 5, so a dead feed would never cross the threshold and never
    /// pause. is_enabled comes back for the same reason - a stale snapshot
    /// would re-pause a feed the user resumed while the fetch was in flight.
    /// Found is false when the feed was deleted mid-fetch.
    /// </summary>
    public Task<FeedFailureState> RecordFailureAsync(
        long feedId, string error,
        DateTimeOffset nowUtc, DateTimeOffset nextDueUtc, CancellationToken ct = default) =>
        db.Writer.ExecuteInTransactionAsync(async (connection, transaction, innerCt) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE feeds SET
                    last_fetched_utc = $now,
                    consecutive_failures = consecutive_failures + 1,
                    last_error = $error,
                    next_due_utc = $nextDue
                WHERE id = $id
                RETURNING consecutive_failures, is_enabled;
                """;
            command.Parameters.AddWithValue("$id", feedId);
            command.Parameters.AddWithValue("$now", nowUtc.ToDbString());
            command.Parameters.AddWithValue("$error", error);
            command.Parameters.AddWithValue("$nextDue", nextDueUtc.ToDbString());

            await using var reader = await command.ExecuteReaderAsync(innerCt);
            if (!await reader.ReadAsync(innerCt))
                return new FeedFailureState(false, 0, false);

            return new FeedFailureState(true, reader.GetInt32(0), reader.GetInt32(1) != 0);
        }, ct);

    public Task DeleteAsync(long id, CancellationToken ct = default) =>
        db.WriteAsync("DELETE FROM feeds WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = id }, ct);

    /// <summary>
    /// Pulls back any next_due_utc that sits further ahead than this app can
    /// ever have scheduled, and returns how many rows were changed.
    ///
    /// Only ever reached when RefreshScheduler has seen the system clock step
    /// backwards. next_due_utc is an absolute instant, so a clock corrected
    /// back by a month leaves every feed scheduled a month out, and background
    /// refresh stops for a month with no error anywhere: the tick still runs,
    /// GetDueAsync still returns nothing, and "nothing is due" and "everything
    /// is due in the far future" look exactly alike from outside. This is the
    /// one statement that can tell them apart, because it is the only one that
    /// knows what "further ahead than possible" means
    /// (RefreshCatchUp.MaxSaneDueAhead).
    ///
    /// Virtual for the same narrow reason GetDueAsync is: so a test can
    /// observe the call without a database.
    /// </summary>
    public virtual Task<int> ClampFutureDueAsync(
        DateTimeOffset newDueUtc,
        DateTimeOffset maxAheadUtc,
        CancellationToken ct = default) =>
        db.WriteAsync(
            """
            UPDATE feeds SET next_due_utc = $newDue
            WHERE next_due_utc IS NOT NULL AND next_due_utc > $maxAhead;
            """,
            new Dictionary<string, object?>
            {
                ["$newDue"] = newDueUtc.ToDbString(),
                ["$maxAhead"] = maxAheadUtc.ToDbString()
            },
            ct);

    /// <summary>
    /// Updates only the publisher-owned title and site link a refresh adopts
    /// from the feed's own content. Deliberately narrower than UpdateAsync:
    /// a refresh runs against whatever Feed snapshot it loaded at the start
    /// of the fetch, and the user is free to edit folder, overrides, enabled
    /// state and the rest of the row while that fetch is in flight. Writing
    /// only these two columns means a refresh can never revert a concurrent
    /// user edit to anything else.
    /// </summary>
    public Task UpdateTitleAndSiteUrlAsync(
        long feedId, string? title, string? siteUrl, CancellationToken ct = default) =>
        db.WriteAsync(
            "UPDATE feeds SET title = $title, site_url = $site WHERE id = $id;",
            new Dictionary<string, object?>
            {
                ["$id"] = feedId,
                ["$title"] = title,
                ["$site"] = siteUrl
            },
            ct);

    /// <summary>
    /// Writes only the user-owned title override, the column Feed.DisplayTitle
    /// prefers over the publisher's own title. A rename must come through here
    /// rather than through UpdateTitleAndSiteUrlAsync: that one writes
    /// feeds.title, which FeedRefreshService re-adopts from the feed's own
    /// content on every successful refresh, so a rename written there is
    /// reverted on the next poll (and invisible straight away if an override
    /// already exists). Null clears the override, which is what "go back to
    /// the feed's own title" means.
    /// </summary>
    public Task UpdateTitleOverrideAsync(
        long feedId, string? titleOverride, CancellationToken ct = default) =>
        db.WriteAsync(
            "UPDATE feeds SET title_override = $titleOverride WHERE id = $id;",
            new Dictionary<string, object?>
            {
                ["$id"] = feedId,
                ["$titleOverride"] = titleOverride
            },
            ct);

    /// <summary>
    /// Enables or disables a feed as a deliberate action (a manual toggle, or
    /// a user re-enabling a feed FeedRefreshService auto-paused). Like the
    /// title and site link adoption above, this must not write back a whole
    /// Feed snapshot that may already be stale by the time it runs.
    ///
    /// Enabling always clears consecutive_failures, last_error and
    /// auto_paused_utc: nothing but a successful refresh (RecordSuccessAsync)
    /// ever clears consecutive_failures otherwise, and a disabled feed is
    /// excluded from GetDueAsync and so can never reach a successful refresh.
    /// Without this reset, a feed the user re-enabled after auto-pause was
    /// re-disabled on its very first subsequent failure - one attempt, not a
    /// real second chance. Disabling never touches these columns; only
    /// FeedRefreshService's own automatic path (AutoPauseAsync) sets
    /// auto_paused_utc, so a manual disable is never mistaken for one.
    /// </summary>
    public Task SetEnabledAsync(
        long feedId, bool isEnabled, CancellationToken ct = default) =>
        db.WriteAsync(
            isEnabled
                ? """
                  UPDATE feeds SET
                      is_enabled = 1,
                      consecutive_failures = 0,
                      last_error = NULL,
                      auto_paused_utc = NULL
                  WHERE id = $id;
                  """
                : "UPDATE feeds SET is_enabled = 0 WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = feedId },
            ct);

    /// <summary>
    /// Disables a feed automatically after it reaches BackoffPolicy.AutoPauseThreshold
    /// consecutive failures, stamping auto_paused_utc so a UI can tell this
    /// apart from is_enabled = 0 set by a deliberate user action (SetEnabledAsync's
    /// disable branch never sets this column).
    /// </summary>
    public Task AutoPauseAsync(
        long feedId, DateTimeOffset nowUtc, CancellationToken ct = default) =>
        db.WriteAsync(
            "UPDATE feeds SET is_enabled = 0, auto_paused_utc = $now WHERE id = $id;",
            new Dictionary<string, object?>
            {
                ["$id"] = feedId,
                ["$now"] = nowUtc.ToDbString()
            },
            ct);

    private Task<Feed?> QuerySingleAsync(
        string sql, Dictionary<string, object?> parameters, CancellationToken ct) =>
        db.QueryAsync<Feed?>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (key, value) in parameters)
                command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct)
                ? RowMappers.ReadFeed((SqliteDataReader)reader)
                : null;
        }, ct);

    private Task<IReadOnlyList<Feed>> QueryManyAsync(
        string sql, Dictionary<string, object?> parameters, CancellationToken ct) =>
        db.QueryAsync<IReadOnlyList<Feed>>(async connection =>
        {
            var results = new List<Feed>();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (key, value) in parameters)
                command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(RowMappers.ReadFeed((SqliteDataReader)reader));
            return results;
        }, ct);
}
