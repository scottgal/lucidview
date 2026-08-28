using LucidReader.Core.Model;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

public class FeedRepository(ReaderDatabase db)
{
    public Task<long> AddAsync(Feed feed, CancellationToken ct = default) =>
        db.WriteReturningIdAsync(
            """
            INSERT INTO feeds (
                folder_id, feed_url, site_url, title, title_override, icon_path,
                is_enabled, next_due_utc, refresh_interval_minutes, auto_download,
                fetch_full_text, retention_days)
            VALUES (
                $folder, $url, $site, $title, $titleOverride, $icon,
                $enabled, $nextDue, $interval, $autoDownload,
                $fullText, $retention);
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
                ["$retention"] = feed.RetentionDays
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

    public Task UpdateAsync(Feed feed, CancellationToken ct = default) =>
        db.WriteAsync(
            """
            UPDATE feeds SET
                folder_id = $folder, site_url = $site, title = $title,
                title_override = $titleOverride, icon_path = $icon,
                is_enabled = $enabled,
                refresh_interval_minutes = $interval, auto_download = $autoDownload,
                fetch_full_text = $fullText, retention_days = $retention
            WHERE id = $id;
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = feed.Id,
                ["$folder"] = feed.FolderId,
                ["$site"] = feed.SiteUrl,
                ["$title"] = feed.Title,
                ["$titleOverride"] = feed.TitleOverride,
                ["$icon"] = feed.IconPath,
                ["$enabled"] = feed.IsEnabled ? 1 : 0,
                ["$interval"] = feed.RefreshIntervalMinutes,
                ["$autoDownload"] = feed.AutoDownload switch { true => 1, false => 0, null => (object?)null },
                ["$fullText"] = feed.FetchFullText switch { true => 1, false => 0, null => (object?)null },
                ["$retention"] = feed.RetentionDays
            },
            ct);

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

    public Task RecordFailureAsync(
        long feedId, string error,
        DateTimeOffset nowUtc, DateTimeOffset nextDueUtc, CancellationToken ct = default) =>
        db.WriteAsync(
            """
            UPDATE feeds SET
                last_fetched_utc = $now,
                consecutive_failures = consecutive_failures + 1,
                last_error = $error,
                next_due_utc = $nextDue
            WHERE id = $id;
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = feedId,
                ["$now"] = nowUtc.ToDbString(),
                ["$error"] = error,
                ["$nextDue"] = nextDueUtc.ToDbString()
            },
            ct);

    public Task DeleteAsync(long id, CancellationToken ct = default) =>
        db.WriteAsync("DELETE FROM feeds WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = id }, ct);

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
    /// Updates only is_enabled. Used for auto-pause, which - like the title
    /// and site link adoption above - must not write back a whole Feed
    /// snapshot that may already be stale by the time the fetch finishes.
    /// </summary>
    public Task SetEnabledAsync(
        long feedId, bool isEnabled, CancellationToken ct = default) =>
        db.WriteAsync(
            "UPDATE feeds SET is_enabled = $enabled WHERE id = $id;",
            new Dictionary<string, object?>
            {
                ["$id"] = feedId,
                ["$enabled"] = isEnabled ? 1 : 0
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
