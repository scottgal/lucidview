using LucidReader.Core.Model;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

internal static class RowMappers
{
    public static string? GetNullableString(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static int? GetNullableInt(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static bool? GetNullableBool(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal) != 0;
    }

    public static bool GetBool(this SqliteDataReader reader, string column) =>
        reader.GetInt32(reader.GetOrdinal(column)) != 0;

    /// <summary>
    /// Dates are stored as ISO-8601 round-trip strings ("o"), which sort
    /// lexicographically in the same order they sort chronologically. That is
    /// what lets the scheduler's due query and the item ordering use plain
    /// string comparison in SQL.
    /// </summary>
    public static DateTimeOffset? GetNullableDate(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return null;
        return DateTimeOffset.Parse(
            reader.GetString(ordinal),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind);
    }

    public static DateTimeOffset GetDate(this SqliteDataReader reader, string column) =>
        GetNullableDate(reader, column)
        ?? throw new InvalidOperationException($"Column {column} was unexpectedly null.");

    public static string? ToDbString(this DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("o");

    public static string ToDbString(this DateTimeOffset value) =>
        value.ToUniversalTime().ToString("o");

    public static Feed ReadFeed(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        FolderId = reader.IsDBNull(reader.GetOrdinal("folder_id"))
            ? null
            : reader.GetInt64(reader.GetOrdinal("folder_id")),
        FeedUrl = reader.GetString(reader.GetOrdinal("feed_url")),
        SiteUrl = reader.GetNullableString("site_url"),
        Title = reader.GetNullableString("title"),
        TitleOverride = reader.GetNullableString("title_override"),
        IconPath = reader.GetNullableString("icon_path"),
        IsEnabled = reader.GetBool("is_enabled"),
        LastFetchedUtc = reader.GetNullableDate("last_fetched_utc"),
        LastSuccessUtc = reader.GetNullableDate("last_success_utc"),
        ETag = reader.GetNullableString("etag"),
        LastModified = reader.GetNullableString("last_modified"),
        ConsecutiveFailures = reader.GetInt32(reader.GetOrdinal("consecutive_failures")),
        LastError = reader.GetNullableString("last_error"),
        NextDueUtc = reader.GetNullableDate("next_due_utc"),
        RefreshIntervalMinutes = reader.GetNullableInt("refresh_interval_minutes"),
        AutoDownload = reader.GetNullableBool("auto_download"),
        FetchFullText = reader.GetNullableBool("fetch_full_text"),
        RetentionDays = reader.GetNullableInt("retention_days"),
        AutoPausedUtc = reader.GetNullableDate("auto_paused_utc"),
        SourceKind = (FeedSourceKind)reader.GetInt32(reader.GetOrdinal("source_kind"))
    };

    /// <summary>
    /// Shared by ItemRepository and SearchRepository, which both select
    /// "items.*" (directly or via the FTS join) and were carrying byte-identical
    /// copies of this mapper. Consolidated while the two were still provably
    /// identical - Plan 2 adding columns to one query but not the other would
    /// otherwise drift silently.
    /// </summary>
    public static Model.FeedItem ReadItem(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        FeedId = reader.GetInt64(reader.GetOrdinal("feed_id")),
        Guid = reader.GetString(reader.GetOrdinal("guid")),
        Link = reader.GetNullableString("link"),
        Title = reader.GetNullableString("title"),
        Author = reader.GetNullableString("author"),
        PublishedUtc = reader.GetNullableDate("published_utc"),
        UpdatedUtc = reader.GetNullableDate("updated_utc"),
        Summary = reader.GetNullableString("summary"),
        ContentHtml = reader.GetNullableString("content_html"),
        ContentMarkdown = reader.GetNullableString("content_markdown"),
        ContentSource = (Model.ContentSource)reader.GetInt32(reader.GetOrdinal("content_source")),
        IsRead = reader.GetBool("is_read"),
        IsStarred = reader.GetBool("is_starred"),
        FirstSeenUtc = reader.GetDate("first_seen_utc"),
        OfflineState = (Model.OfflineState)reader.GetInt32(reader.GetOrdinal("offline_state")),
        OfflineError = reader.GetNullableString("offline_error"),
        ImageUrl = reader.GetNullableString("image_url"),
        CanonicalId = reader.GetNullableString("canonical_id")
    };

    public static Folder ReadFolder(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order")),
        ParentId = reader.IsDBNull(reader.GetOrdinal("parent_id"))
            ? null
            : reader.GetInt64(reader.GetOrdinal("parent_id"))
    };
}
