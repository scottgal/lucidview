namespace LucidReader.Core.Notifications;

/// <summary>
/// What one refresh sweep brought in: how many unread articles, across how
/// many feeds.
/// </summary>
public readonly record struct NewArticleSweep(int ArticleCount, int FeedCount)
{
    public static readonly NewArticleSweep Empty = new(0, 0);

    public bool HasArticles => ArticleCount > 0;
}

/// <summary>
/// Gathers per-feed refresh results into one sweep, so a refresh of forty
/// feeds produces one notification rather than forty.
///
/// Coalescing has to happen over time, not per event, because
/// FeedRefreshService.Completed fires once per feed and the feeds in a sweep
/// finish seconds apart. The rule used here is a quiet period: every arrival
/// pushes the deadline out, and the sweep is only closed once nothing has
/// arrived for that long. That way a scheduler tick that queues the whole
/// subscription list yields a single "12 new articles from 4 feeds", and a
/// feed that finishes long after everything else is its own small
/// notification rather than being silently folded into a sweep that was
/// already reported.
///
/// Deliberately holds no timer of its own. The caller owns when
/// <see cref="TakeIfSettled"/> is asked, which is what lets this be tested
/// against an injected clock with no waiting, and what keeps the class free
/// of any dependency on a dispatcher or a synchronization context.
///
/// Thread ownership: instances are not thread safe. The shell drives this
/// from the UI thread only.
/// </summary>
public sealed class NewArticleAccumulator(TimeSpan? quietPeriod = null)
{
    /// <summary>
    /// How long after the last arriving feed a sweep is considered finished.
    /// Long enough to cover the gaps between feeds in one scheduler tick,
    /// short enough that the notification still feels like a consequence of
    /// the refresh rather than an unexplained interruption later.
    /// </summary>
    public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromSeconds(6);

    private readonly TimeSpan _quietPeriod = quietPeriod ?? DefaultQuietPeriod;
    private readonly HashSet<long> _feedIds = [];

    private int _articleCount;
    private DateTimeOffset _lastArrivalUtc;

    public bool HasPending => _articleCount > 0;

    /// <summary>
    /// Records a feed that just finished with new unread articles. A feed
    /// counted twice in one sweep (a manual refresh landing on top of an
    /// automatic one) adds its articles but not a second feed.
    /// </summary>
    public void Add(long feedId, int newArticleCount, DateTimeOffset nowUtc)
    {
        if (newArticleCount <= 0) return;

        _articleCount += newArticleCount;
        _feedIds.Add(feedId);
        _lastArrivalUtc = nowUtc;
    }

    /// <summary>
    /// Returns and clears the sweep if the quiet period has elapsed since the
    /// last arrival, and <see cref="NewArticleSweep.Empty"/> otherwise.
    /// </summary>
    public NewArticleSweep TakeIfSettled(DateTimeOffset nowUtc)
    {
        if (_articleCount <= 0) return NewArticleSweep.Empty;
        if (nowUtc - _lastArrivalUtc < _quietPeriod) return NewArticleSweep.Empty;

        return Take();
    }

    /// <summary>
    /// Returns and clears whatever has accumulated, settled or not. Used when
    /// the window is closing or the user has just been shown the articles
    /// some other way, so a stale sweep is not posted minutes later.
    /// </summary>
    public NewArticleSweep Take()
    {
        var sweep = new NewArticleSweep(_articleCount, _feedIds.Count);
        _articleCount = 0;
        _feedIds.Clear();
        return sweep;
    }
}
