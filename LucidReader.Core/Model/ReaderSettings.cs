namespace LucidReader.Core.Model;

/// <summary>
/// Global defaults. Every field here has a matching nullable override on Feed;
/// null on the feed means "use the value from here".
/// </summary>
public sealed record ReaderSettings
{
    /// <summary>
    /// No setting combination may poll a server faster than this. It is a floor
    /// on the whole app, not a default.
    /// </summary>
    public static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(5);

    // Updates
    public int DefaultRefreshIntervalMinutes { get; init; } = 30;
    public bool RefreshOnStartup { get; init; } = true;
    public bool PauseWhenOffline { get; init; } = true;
    public int MaxConcurrentFetches { get; init; } = 4;

    // Offline
    public bool AutoDownloadArticles { get; init; } = true;
    public bool FetchFullText { get; init; } = true;
    public bool CacheImages { get; init; } = true;
    public int MaxImageBytes { get; init; } = 5 * 1024 * 1024;
    public int MaxConcurrentDownloads { get; init; } = 2;

    // Retention
    public int KeepReadArticlesDays { get; init; } = 30;
    public bool KeepUnreadForever { get; init; } = true;
    public int KeepUnreadDays { get; init; } = 180;
    public int MaxArticlesPerFeed { get; init; } = 500;
    public bool NeverDeleteStarred { get; init; } = true;

    // Reading
    public string Theme { get; init; } = "Auto";
    public double FontSize { get; init; } = 15;
    public double ColumnWidth { get; init; } = 760;
    public int MarkReadDwellMilliseconds { get; init; } = 800;
    public bool OpenLinksExternally { get; init; } = true;

    public static ReaderSettings Defaults { get; } = new();
}
