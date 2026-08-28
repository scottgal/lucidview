using LucidReader.Core.Model;
using LucidReader.Core.Storage;

namespace LucidReader.Core.Maintenance;

/// <summary>
/// Deletes old items according to the retention settings. Starred items are
/// exempt from every rule when NeverDeleteStarred is on: a star is the user
/// saying "keep this", and no automatic policy should override that.
/// </summary>
public sealed class RetentionService(
    ReaderDatabase db,
    Func<ReaderSettings> settings,
    TimeProvider timeProvider)
{
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

        // Read items past their window.
        if (current.KeepReadArticlesDays > 0)
        {
            deleted += await db.WriteAsync(
                $"""
                 DELETE FROM items
                 WHERE is_read = 1
                   AND COALESCE(published_utc, first_seen_utc) < $cutoff
                   {starredClause};
                 """,
                new Dictionary<string, object?>
                {
                    ["$cutoff"] = now.AddDays(-current.KeepReadArticlesDays).ToDbString()
                }, ct);
        }

        // Unread items, only when the user has asked for a window.
        if (!current.KeepUnreadForever && current.KeepUnreadDays > 0)
        {
            deleted += await db.WriteAsync(
                $"""
                 DELETE FROM items
                 WHERE is_read = 0
                   AND COALESCE(published_utc, first_seen_utc) < $cutoff
                   {starredClause};
                 """,
                new Dictionary<string, object?>
                {
                    ["$cutoff"] = now.AddDays(-current.KeepUnreadDays).ToDbString()
                }, ct);
        }

        // Per-feed cap: keep the newest N in each feed, drop the rest.
        if (current.MaxArticlesPerFeed > 0)
        {
            deleted += await db.WriteAsync(
                $"""
                 DELETE FROM items
                 WHERE id IN (
                     SELECT id FROM (
                         SELECT id,
                                ROW_NUMBER() OVER (
                                    PARTITION BY feed_id
                                    ORDER BY COALESCE(published_utc, first_seen_utc) DESC
                                ) AS row_number
                         FROM items
                         WHERE 1 = 1 {starredClause}
                     )
                     WHERE row_number > $max
                 );
                 """,
                new Dictionary<string, object?> { ["$max"] = current.MaxArticlesPerFeed },
                ct);
        }

        return deleted;
    }

    public Task<long> GetDatabaseSizeBytesAsync(CancellationToken ct = default) =>
        db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size();";
            return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        }, ct);
}
