using LucidReader.Core.Model;

namespace LucidReader.Models;

/// <summary>
/// A plain, Avalonia-free holder for every editable value in the per-feed
/// settings dialog, plus the mapping back onto <see cref="Feed"/>. Matches
/// the split used by SettingsDraft/SettingsDialog: a Window cannot be
/// constructed in a unit test in this repo, so the inherit-versus-override
/// rule - the part actually worth testing - lives here as an ordinary
/// object, and FeedSettingsDialog is just a thin shell that reads and
/// writes these properties.
///
/// The rule this exists to enforce: each of the four inheritable columns
/// (RefreshIntervalMinutes, AutoDownload, FetchFullText, RetentionDays) is
/// null on <see cref="Feed"/> until the user explicitly overrides it, and
/// null means "follow the global setting". Turning an override off MUST
/// write null back, not the global's current value - writing the current
/// value would make the feed stop following future changes to that global,
/// silently. <c>false</c> is a real override and must never be confused
/// with "unset".
/// </summary>
public sealed class FeedSettingsDraft
{
    private readonly Feed _original;
    private readonly ReaderSettings _globals;

    public FeedSettingsDraft(Feed feed, ReaderSettings globals)
    {
        _original = feed;
        _globals = globals;

        // Feed.DisplayTitle already encodes the override-then-title-then-URL
        // order. Reading feed.Title directly here showed the raw feed URL as
        // the dialog header for a feed that has a user override but no
        // publisher title yet.
        DisplayTitle = feed.DisplayTitle;
        FeedUrl = feed.FeedUrl;
        TitleOverride = feed.TitleOverride ?? string.Empty;
        FolderId = feed.FolderId;
        IsEnabled = feed.IsEnabled;

        // A non-null column means the user already set an override for this
        // feed; that is the only signal available, there is no separate
        // "has an override" flag on Feed.
        OverrideRefreshInterval = feed.RefreshIntervalMinutes is not null;
        OverrideAutoDownload = feed.AutoDownload is not null;
        OverrideFetchFullText = feed.FetchFullText is not null;
        OverrideRetention = feed.RetentionDays is not null;

        // Editors start at the inherited value so switching an override on
        // does not jump to some unrelated number.
        RefreshIntervalMinutes = feed.RefreshIntervalMinutes ?? globals.DefaultRefreshIntervalMinutes;
        AutoDownload = feed.AutoDownload ?? globals.AutoDownloadArticles;
        FetchFullText = feed.FetchFullText ?? globals.FetchFullText;
        RetentionDays = feed.RetentionDays ?? globals.KeepReadArticlesDays;
    }

    public string DisplayTitle { get; }
    public string FeedUrl { get; }
    public string TitleOverride { get; set; }
    public long? FolderId { get; set; }
    public bool IsEnabled { get; set; }

    public bool OverrideRefreshInterval { get; set; }
    public bool OverrideAutoDownload { get; set; }
    public bool OverrideFetchFullText { get; set; }
    public bool OverrideRetention { get; set; }

    public int RefreshIntervalMinutes { get; set; }
    public bool AutoDownload { get; set; }
    public bool FetchFullText { get; set; }
    public int RetentionDays { get; set; }

    public string InheritedRefreshIntervalLabel =>
        $"Use the global setting ({_globals.DefaultRefreshIntervalMinutes} minutes)";
    public string InheritedAutoDownloadLabel =>
        $"Use the global setting ({(_globals.AutoDownloadArticles ? "on" : "off")})";
    public string InheritedFetchFullTextLabel =>
        $"Use the global setting ({(_globals.FetchFullText ? "on" : "off")})";
    public string InheritedRetentionLabel =>
        $"Use the global setting ({_globals.KeepReadArticlesDays} days)";

    /// <summary>Set by <see cref="Apply"/>. Null until Apply has run.</summary>
    public Feed? Result { get; private set; }

    /// <summary>
    /// Null means inherit. An override switched off MUST write null rather
    /// than the global's present value, or the feed silently stops
    /// following future changes to that global - that distinction is the
    /// entire point of the nullable columns.
    /// </summary>
    public Feed Apply()
    {
        var floor = (int)ReaderSettings.MinimumRefreshInterval.TotalMinutes;

        var applied = _original with
        {
            TitleOverride = string.IsNullOrWhiteSpace(TitleOverride) ? null : TitleOverride.Trim(),
            FolderId = FolderId,
            IsEnabled = IsEnabled,
            RefreshIntervalMinutes = OverrideRefreshInterval
                ? Math.Max(floor, RefreshIntervalMinutes)
                : null,
            AutoDownload = OverrideAutoDownload ? AutoDownload : null,
            FetchFullText = OverrideFetchFullText ? FetchFullText : null,
            RetentionDays = OverrideRetention ? Math.Max(0, RetentionDays) : null
        };

        Result = applied;
        return applied;
    }
}
