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

    /// <summary>
    /// Which panes the window shows: "ThreePane", "ListAndReading" or
    /// "ReadingOnly". A string rather than an enum for the same reason
    /// <see cref="Theme"/> is one: this record is serialized straight to
    /// settings.json, and a stored name this build does not recognise has to
    /// degrade to the default instead of failing the whole file to parse.
    /// LucidReader.Models.ReaderLayout owns the parsing and the cycle order.
    ///
    /// There is no control for this in the settings dialog. The toolbar's
    /// layout button is the control; this is only where its last position is
    /// kept so it survives a restart.
    /// </summary>
    public string LayoutMode { get; init; } = "ThreePane";
    public bool OpenLinksExternally { get; init; } = true;

    /// <summary>
    /// Off by default. This is the only setting that permits the reader to
    /// send anything the user typed (a search query) to a third party -
    /// Feedly's public search index. Every caller of <c>IFeedSearch</c> must
    /// treat this as a hard gate, not a courtesy check: with it off, no
    /// search request may be constructed, let alone sent.
    /// </summary>
    public bool EnableOnlineFeedSearch { get; init; }

    /// <summary>
    /// Set once, the first time the starter subscriptions in
    /// LucidReader.Core.Feeds.DefaultFeeds are written, and never cleared.
    ///
    /// It lives here rather than in the database because it has to survive an
    /// empty feed table: its whole job is to stop a profile whose owner has
    /// unsubscribed from everything being handed the starter list again on
    /// the next launch. FirstRunSeedPolicy is what reads it.
    /// </summary>
    public bool HasSeededDefaultFeeds { get; init; }

    public static ReaderSettings Defaults { get; } = new();
}
