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

    /// <summary>
    /// A multiplier on the resolved font size, not an absolute measure, so it
    /// keeps meaning the same thing when FontSize changes. Matches lucidVIEW's
    /// AppSettings.LineHeight default of 1.5. A value of 1.0 means "use the
    /// typeface's own line metrics".
    /// </summary>
    public double LineHeight { get; init; } = 1.5;

    /// <summary>
    /// Absolute, not a multiple of FontSize: a monospace face at the same
    /// point size as the body reads bigger, so code wants its own number.
    /// Same default as lucidVIEW.
    /// </summary>
    public double CodeFontSize { get; init; } = 13;

    /// <summary>
    /// The width the reading column is asked for. It is a preference, not the
    /// width actually used: LucidReader.Models.ReadingColumnMetrics clamps it
    /// to what the reading pane can show and leaves this value alone, so
    /// widening the pane again restores it.
    /// </summary>
    public double ColumnWidth { get; init; } = 760;
    public int MarkReadDwellMilliseconds { get; init; } = 800;
    public bool OpenLinksExternally { get; init; } = true;

    /// <summary>
    /// Off by default. This is the only setting that permits the reader to
    /// send anything the user typed (a search query) to a third party -
    /// Feedly's public search index. Every caller of <c>IFeedSearch</c> must
    /// treat this as a hard gate, not a courtesy check: with it off, no
    /// search request may be constructed, let alone sent.
    /// </summary>
    public bool EnableOnlineFeedSearch { get; init; }

    public static ReaderSettings Defaults { get; } = new();
}
