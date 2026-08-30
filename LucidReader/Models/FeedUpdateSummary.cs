namespace LucidReader.Models;

/// <summary>
/// What the per-feed update line says, and whether it says anything at all.
///
/// Returned rather than assembled in the view so the whole decision is one
/// value a test can assert on.
/// </summary>
/// <param name="IsVisible">
/// False for a selection that is not a single feed. A folder groups feeds with
/// their own separate schedules and a smart row spans every feed there is, so
/// neither has a last-updated time or a next-due time, and "refresh this feed"
/// has nothing to point at. The line is hidden for both rather than shown with
/// a hedge in it.
/// </param>
/// <param name="Text">The one quiet line. Never null, empty only when hidden.</param>
/// <param name="CanRefresh">
/// Whether to offer the manual refresh control. False while a refresh is
/// already running, and false for an auto-paused feed: the way back for that
/// one is Resume, which already has its own affordance, and a Refresh that
/// silently left the feed paused would be a worse answer than no Refresh.
/// </param>
public readonly record struct FeedUpdateLine(
    bool IsVisible, string Text, bool CanRefresh, string ShortText)
{
    public static readonly FeedUpdateLine Hidden = new(false, string.Empty, false, string.Empty);
}

/// <summary>
/// The wording and the arithmetic behind the per-feed update line.
///
/// Plain and Avalonia-free, the same way ReadingColumnMetrics and
/// FeedUrlPolicy are, and for the same reason: a Window cannot be constructed
/// in a unit test in this repo, so anything that has to be right - and relative
/// time is the classic place to be off by one boundary - has to live outside
/// the view.
///
/// Everything takes an explicit `now`. Nothing here reads the clock, so the
/// boundaries are testable rather than approximately testable.
/// </summary>
public static class FeedUpdateSummary
{
    /// <summary>
    /// Between the two halves of the line. A thin separator rather than a
    /// second line, because the requirement is that this stays compact.
    /// </summary>
    private const string Separator = "   ·   ";

    public static FeedUpdateLine Describe(
        bool isFeedSelected,
        bool isRefreshing,
        bool isAutoPaused,
        bool isEnabled,
        DateTimeOffset? lastFetchedUtc,
        DateTimeOffset? lastSuccessUtc,
        string? lastError,
        DateTimeOffset? nextDueUtc,
        DateTimeOffset now)
    {
        if (!isFeedSelected) return FeedUpdateLine.Hidden;

        // Checked before anything else, including the paused and disabled
        // cases: a feed can be resumed and immediately queued, and while that
        // fetch is running "Paused after repeated failures" would be stale in
        // the one moment the user is watching.
        if (isRefreshing)
            return new FeedUpdateLine(true, "Refreshing now...", false, "Refreshing...");

        if (isAutoPaused)
            return new FeedUpdateLine(true, "Paused after repeated failures.", false, "Paused");

        if (!isEnabled)
            return new FeedUpdateLine(true, "Updates are turned off for this feed.", false, "Updates off");

        var head =
            lastFetchedUtc is null ? "Not updated yet"
            : HasFailed(lastFetchedUtc, lastSuccessUtc, lastError) ? "Last update failed"
            : "Updated " + DescribeElapsed(lastSuccessUtc ?? lastFetchedUtc, now);

        var tail = DescribeNext(nextDueUtc, now);

        // The short form is what actually renders. At the item list's default
        // 340px the segmented filter and a full sentence cannot share a line,
        // so the line wrapped immediately and saved none of the space it was
        // added to save. The long form survives as the tooltip, so nothing is
        // lost, and the WrapPanel still drops to two lines if the column is
        // dragged narrower than even the short form needs.
        var shortHead =
            lastFetchedUtc is null ? "Never"
            : HasFailed(lastFetchedUtc, lastSuccessUtc, lastError) ? "Failed"
            : DescribeElapsed(lastSuccessUtc ?? lastFetchedUtc, now);

        return new FeedUpdateLine(
            true,
            tail.Length == 0 ? head : head + Separator + tail,
            true,
            shortHead);
    }

    /// <summary>
    /// Whether the most recent attempt failed. An error message on its own is
    /// not enough: last_error is left in place after a failure and is only
    /// cleared by the next success, so a feed that failed an hour ago and has
    /// worked ever since still carries one. The attempt is the failing one only
    /// when nothing has succeeded since it.
    /// </summary>
    private static bool HasFailed(
        DateTimeOffset? lastFetchedUtc,
        DateTimeOffset? lastSuccessUtc,
        string? lastError)
    {
        if (string.IsNullOrWhiteSpace(lastError)) return false;
        if (lastSuccessUtc is null) return true;

        return lastFetchedUtc > lastSuccessUtc;
    }

    private static string DescribeNext(DateTimeOffset? nextDueUtc, DateTimeOffset now)
    {
        if (nextDueUtc is not { } due) return string.Empty;

        var remaining = due - now;

        // A due time that has passed is the ordinary state between the moment a
        // feed falls due and the moment the scheduler's next tick picks it up,
        // which can be up to a minute. "Next in -3 min" would be nonsense and
        // "Next in 0 min" would be a lie.
        return remaining <= TimeSpan.Zero ? "Due now" : "Next in " + DescribeDuration(remaining);
    }

    /// <summary>
    /// How long ago something happened, in words. Null means it never did.
    /// </summary>
    public static string DescribeElapsed(DateTimeOffset? then, DateTimeOffset now)
    {
        if (then is not { } moment) return "never";

        var elapsed = now - moment;

        // A timestamp in the future is not worth a special phrase. It means a
        // clock correction or a server's own dating, and "just now" is both
        // true enough and the least alarming thing to say.
        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return Plural((int)elapsed.TotalMinutes, "min", "min") + " ago";
        if (elapsed < TimeSpan.FromDays(1)) return Plural((int)elapsed.TotalHours, "hour", "hours") + " ago";
        if (elapsed < TimeSpan.FromDays(2)) return "yesterday";

        return Plural((int)elapsed.TotalDays, "day", "days") + " ago";
    }

    /// <summary>
    /// How long until something happens, in words. Never called with a
    /// non-positive span: DescribeNext says "Due now" for those.
    /// </summary>
    public static string DescribeDuration(TimeSpan remaining)
    {
        if (remaining < TimeSpan.FromMinutes(1)) return "under a minute";
        if (remaining < TimeSpan.FromHours(1)) return Plural((int)remaining.TotalMinutes, "min", "min");
        if (remaining < TimeSpan.FromDays(1)) return Plural((int)remaining.TotalHours, "hour", "hours");

        return Plural((int)remaining.TotalDays, "day", "days");
    }

    private static string Plural(int count, string one, string many) =>
        count + " " + (count == 1 ? one : many);
}
