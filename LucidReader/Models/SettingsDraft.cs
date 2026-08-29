using LucidReader.Core.Model;

namespace LucidReader.Models;

/// <summary>
/// A plain, Avalonia-free holder for every editable value in the settings
/// dialog, plus the mapping back onto <see cref="ReaderSettings"/> and the
/// clamping that goes with it. A Window cannot be constructed in a unit test
/// in this repo, so this class exists specifically so the mapping - the part
/// actually worth testing - is testable as an ordinary object; SettingsDialog
/// itself is just a thin shell that reads and writes these properties.
/// </summary>
public sealed class SettingsDraft
{
    private readonly ReaderSettings _original;

    public SettingsDraft(ReaderSettings current)
    {
        _original = current;

        DefaultRefreshIntervalMinutes = current.DefaultRefreshIntervalMinutes;
        RefreshOnStartup = current.RefreshOnStartup;
        PauseWhenOffline = current.PauseWhenOffline;
        MaxConcurrentFetches = current.MaxConcurrentFetches;
        AutoDownloadArticles = current.AutoDownloadArticles;
        FetchFullText = current.FetchFullText;
        CacheImages = current.CacheImages;
        MaxConcurrentDownloads = current.MaxConcurrentDownloads;
        KeepReadArticlesDays = current.KeepReadArticlesDays;
        KeepUnreadForever = current.KeepUnreadForever;
        KeepUnreadDays = current.KeepUnreadDays;
        MaxArticlesPerFeed = current.MaxArticlesPerFeed;
        NeverDeleteStarred = current.NeverDeleteStarred;
        Theme = current.Theme;
        FontSize = current.FontSize;
        ColumnWidth = current.ColumnWidth;
        MarkReadDwellMilliseconds = current.MarkReadDwellMilliseconds;
        OpenLinksExternally = current.OpenLinksExternally;
        EnableOnlineFeedSearch = current.EnableOnlineFeedSearch;
    }

    // Updates
    public int DefaultRefreshIntervalMinutes { get; set; }
    public bool RefreshOnStartup { get; set; }
    public bool PauseWhenOffline { get; set; }
    public int MaxConcurrentFetches { get; set; }

    /// <summary>
    /// Off by default in ReaderSettings, and this is the only editable value
    /// that ever sends anything the user typed to a third party (Feedly's
    /// public search index). It stays here rather than tucked away because
    /// the dialog must say so plainly, not just gate it quietly.
    /// </summary>
    public bool EnableOnlineFeedSearch { get; set; }

    // Offline
    public bool AutoDownloadArticles { get; set; }
    public bool FetchFullText { get; set; }
    public bool CacheImages { get; set; }
    public int MaxConcurrentDownloads { get; set; }

    // Retention
    public int KeepReadArticlesDays { get; set; }
    public bool KeepUnreadForever { get; set; }
    public int KeepUnreadDays { get; set; }
    public int MaxArticlesPerFeed { get; set; }
    public bool NeverDeleteStarred { get; set; }

    // Reading
    // No UI exposes Theme any more (the app follows the system appearance,
    // like Mail); this survives edits only because the round-trip through
    // Apply() must not reset a setting the dialog does not offer.
    public string Theme { get; set; } = "Auto";
    public double FontSize { get; set; }
    public double ColumnWidth { get; set; }
    public int MarkReadDwellMilliseconds { get; set; }
    public bool OpenLinksExternally { get; set; }

    /// <summary>
    /// Applies the edits over the settings the draft was built from, so any
    /// setting this dialog does not expose survives untouched. Clamps every
    /// value that has a floor defined elsewhere (ReaderSettings.MinimumRefreshInterval,
    /// or simply "must not be negative/zero") rather than trusting the UI
    /// widgets to have enforced it, because nothing stops a value reaching
    /// here some other way.
    /// </summary>
    public ReaderSettings Apply()
    {
        var floorMinutes = (int)ReaderSettings.MinimumRefreshInterval.TotalMinutes;

        return _original with
        {
            DefaultRefreshIntervalMinutes = Math.Max(floorMinutes, DefaultRefreshIntervalMinutes),
            RefreshOnStartup = RefreshOnStartup,
            PauseWhenOffline = PauseWhenOffline,
            MaxConcurrentFetches = Math.Max(1, MaxConcurrentFetches),
            EnableOnlineFeedSearch = EnableOnlineFeedSearch,
            AutoDownloadArticles = AutoDownloadArticles,
            FetchFullText = FetchFullText,
            CacheImages = CacheImages,
            MaxConcurrentDownloads = Math.Max(1, MaxConcurrentDownloads),
            KeepReadArticlesDays = Math.Max(0, KeepReadArticlesDays),
            KeepUnreadForever = KeepUnreadForever,
            KeepUnreadDays = Math.Max(0, KeepUnreadDays),
            MaxArticlesPerFeed = Math.Max(0, MaxArticlesPerFeed),
            NeverDeleteStarred = NeverDeleteStarred,
            Theme = Theme,
            FontSize = Math.Clamp(FontSize, 9, 40),
            ColumnWidth = Math.Clamp(ColumnWidth, 320, 2000),
            MarkReadDwellMilliseconds = Math.Max(0, MarkReadDwellMilliseconds),
            OpenLinksExternally = OpenLinksExternally
        };
    }

    /// <summary>
    /// Human-readable byte count for the retention group's database-size
    /// readout, because a retention setting whose effect is invisible is one
    /// nobody trusts.
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} bytes";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.0} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
    }
}
