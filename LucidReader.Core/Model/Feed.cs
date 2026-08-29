namespace LucidReader.Core.Model;

public sealed record Feed
{
    public long Id { get; init; }
    public long? FolderId { get; init; }
    public required string FeedUrl { get; init; }
    public string? SiteUrl { get; init; }
    public string? Title { get; init; }
    public string? TitleOverride { get; init; }
    public string? IconPath { get; init; }
    public bool IsEnabled { get; init; } = true;

    public DateTimeOffset? LastFetchedUtc { get; init; }
    public DateTimeOffset? LastSuccessUtc { get; init; }
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
    public int ConsecutiveFailures { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset? NextDueUtc { get; init; }

    /// <summary>
    /// Set only when FeedRefreshService disabled this feed automatically after
    /// reaching BackoffPolicy.AutoPauseThreshold consecutive failures; null for
    /// a feed the user disabled deliberately. Lets a UI distinguish the two and
    /// is cleared whenever the feed is re-enabled.
    /// </summary>
    public DateTimeOffset? AutoPausedUtc { get; init; }

    public int? RefreshIntervalMinutes { get; init; }
    public bool? AutoDownload { get; init; }
    public bool? FetchFullText { get; init; }
    public int? RetentionDays { get; init; }

    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(TitleOverride) ? TitleOverride
        : !string.IsNullOrWhiteSpace(Title) ? Title
        : FeedUrl;
}
