namespace LucidReader.Core.Model;

/// <summary>
/// A feed's settings after its overrides have been layered over the globals.
/// Everything downstream of the settings UI works with this, never with the
/// raw nullable fields, so the inherit-versus-override rule lives in exactly
/// one place.
/// </summary>
public readonly record struct EffectiveFeedSettings(
    TimeSpan RefreshInterval,
    bool AutoDownload,
    bool FetchFullText,
    int? RetentionDays)
{
    public static EffectiveFeedSettings Resolve(Feed feed, ReaderSettings globals)
    {
        var minutes = feed.RefreshIntervalMinutes ?? globals.DefaultRefreshIntervalMinutes;
        var interval = TimeSpan.FromMinutes(minutes);
        if (interval < ReaderSettings.MinimumRefreshInterval)
            interval = ReaderSettings.MinimumRefreshInterval;

        return new EffectiveFeedSettings(
            interval,
            feed.AutoDownload ?? globals.AutoDownloadArticles,
            feed.FetchFullText ?? globals.FetchFullText,
            feed.RetentionDays ?? globals.KeepReadArticlesDays);
    }
}
