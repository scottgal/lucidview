using LucidReader.Core.Model;

namespace LucidReader.Core.Notifications;

/// <summary>
/// Whether a sweep is worth telling the user about, and what to say.
///
/// Kept as static functions over plain values so both answers can be
/// asserted without a window, a notification service or a platform. The
/// shell owns everything platform-shaped; this owns the decision and the
/// wording, which are the parts that are actually easy to get wrong.
/// </summary>
public static class NotificationPolicy
{
    public static bool ShouldNotify(
        ReaderSettings settings,
        bool windowIsFocused,
        NewArticleSweep sweep) =>
        ShouldNotify(settings.EnableNotifications, settings.NotifyOnlyWhenUnfocused,
            windowIsFocused, sweep.ArticleCount);

    public static bool ShouldNotify(
        bool enabled,
        bool onlyWhenUnfocused,
        bool windowIsFocused,
        int articleCount)
    {
        if (!enabled) return false;
        if (articleCount <= 0) return false;
        if (onlyWhenUnfocused && windowIsFocused) return false;
        return true;
    }

    /// <summary>
    /// The notification body. Singular and plural are both written out rather
    /// than being assembled with an "(s)": this is the one line of text most
    /// users will ever read from this app outside the window itself.
    ///
    /// The feed count is only mentioned when it adds something. "3 new
    /// articles from 1 feed" tells the reader nothing "3 new articles" did
    /// not, so a single-feed sweep does not say it.
    /// </summary>
    public static string Describe(NewArticleSweep sweep) =>
        Describe(sweep.ArticleCount, sweep.FeedCount);

    public static string Describe(int articleCount, int feedCount)
    {
        if (articleCount <= 0) return string.Empty;

        var articles = articleCount == 1 ? "1 new article" : $"{articleCount} new articles";

        return feedCount > 1
            ? $"{articles} from {feedCount} feeds"
            : articles;
    }

    /// <summary>
    /// What the status item's tooltip says. The unread count is the whole
    /// point of a status item, so it leads.
    /// </summary>
    public static string DescribeUnread(int unreadCount) => unreadCount switch
    {
        <= 0 => "mylo: no unread articles",
        1 => "mylo: 1 unread article",
        _ => $"mylo: {unreadCount} unread articles"
    };

    /// <summary>
    /// The badge text on the status item. Empty when there is nothing unread,
    /// so the menu bar is not carrying a permanent zero, and capped so a
    /// neglected reader does not put a five-digit number in the menu bar.
    /// </summary>
    public static string UnreadBadge(int unreadCount) => unreadCount switch
    {
        <= 0 => string.Empty,
        > 999 => "999+",
        _ => unreadCount.ToString()
    };
}
