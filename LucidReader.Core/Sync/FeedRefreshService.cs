using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Mostlylucid.Ephemeral;

namespace LucidReader.Core.Sync;

/// <summary>
/// Owns feed refreshing. Work goes through an EphemeralWorkCoordinator so
/// concurrency is bounded and progress is observable, and an in-flight set
/// coalesces a manual refresh with an already-queued automatic one.
/// </summary>
public sealed class FeedRefreshService : IAsyncDisposable
{
    /// <summary>
    /// The coordinator requires an explicit bound, and rightly so: a server
    /// that accepts a connection and then stalls would otherwise hold its
    /// concurrency slot until the app closes.
    /// </summary>
    public static readonly TimeSpan MaxFeedFetchDuration = TimeSpan.FromSeconds(60);

    private readonly FeedRepository _feeds;
    private readonly ItemRepository _items;
    private readonly FeedFetcher _fetcher;
    private readonly IFeedParser _parser;
    private readonly BackoffPolicy _backoff;
    private readonly Func<ReaderSettings> _settings;
    private readonly TimeProvider _time;
    private readonly EphemeralWorkCoordinator<FeedRefreshRequest> _coordinator;
    private readonly ConcurrentDictionary<long, byte> _inFlight = new();

    public FeedRefreshService(
        FeedRepository feeds,
        ItemRepository items,
        FeedFetcher fetcher,
        IFeedParser parser,
        BackoffPolicy backoff,
        Func<ReaderSettings> settings,
        TimeProvider timeProvider,
        int maxConcurrency = 4)
    {
        _feeds = feeds;
        _items = items;
        _fetcher = fetcher;
        _parser = parser;
        _backoff = backoff;
        _settings = settings;
        _time = timeProvider;

        _coordinator = new EphemeralWorkCoordinator<FeedRefreshRequest>(
            RunAsync,
            MaxFeedFetchDuration,
            new EphemeralOptions
            {
                MaxConcurrency = maxConcurrency,
                // The default of 200 is the bounded channel's capacity, and
                // EnqueueAsync blocks once it is full. A user with more than
                // 200 subscriptions hitting Refresh All would stall on that.
                MaxTrackedOperations = 4096
            },
            timeProvider);
    }

    public int PendingCount => _coordinator.PendingCount;
    public int ActiveCount => _coordinator.ActiveCount;
    public int TotalFailed => _coordinator.TotalFailed;

    public event Action<FeedRefreshOutcome>? Completed;

    /// <summary>
    /// Queues a refresh, or returns false if this feed is already queued or
    /// running. That is the coalescing rule: pressing Refresh twice does not
    /// fetch twice.
    /// </summary>
    public bool TryQueue(long feedId, bool isManual = false)
    {
        if (!_inFlight.TryAdd(feedId, 0)) return false;

        if (_coordinator.TryEnqueue(new FeedRefreshRequest(feedId, isManual)))
            return true;

        _inFlight.TryRemove(feedId, out _);
        return false;
    }

    public async Task QueueAsync(long feedId, bool isManual = false, CancellationToken ct = default)
    {
        if (!_inFlight.TryAdd(feedId, 0)) return;

        try
        {
            await _coordinator.EnqueueAsync(new FeedRefreshRequest(feedId, isManual), ct);
        }
        catch
        {
            _inFlight.TryRemove(feedId, out _);
            throw;
        }
    }

    public void Pause() => _coordinator.Pause();
    public void Resume() => _coordinator.Resume();

    private async Task RunAsync(FeedRefreshRequest request, CancellationToken ct)
    {
        FeedRefreshOutcome outcome;
        try
        {
            outcome = await RefreshCoreAsync(request.FeedId, ct);
        }
        finally
        {
            // Removed before Completed fires, and unconditionally on the way out
            // (including when RefreshCoreAsync throws): a subscriber reacting to
            // Completed by re-queueing the same feed must see it as available, and
            // a body that throws must not leave the feed permanently in flight.
            _inFlight.TryRemove(request.FeedId, out _);
        }

        Completed?.Invoke(outcome);
    }

    /// <summary>
    /// Refreshes one feed inline, bypassing the queue. Used by the synchronous
    /// refresh path and by tests.
    /// </summary>
    public Task<FeedRefreshOutcome> RefreshNowAsync(long feedId, CancellationToken ct = default) =>
        RefreshCoreAsync(feedId, ct);

    private async Task<FeedRefreshOutcome> RefreshCoreAsync(long feedId, CancellationToken ct)
    {
        var feed = await _feeds.GetAsync(feedId, ct);
        if (feed is null)
            return new FeedRefreshOutcome(feedId, false, 0, false, "The feed no longer exists.");

        var settings = EffectiveFeedSettings.Resolve(feed, _settings());
        var now = _time.GetUtcNow();

        var result = await _fetcher.FetchAsync(feed.FeedUrl, feed.ETag, feed.LastModified, ct);

        switch (result)
        {
            case FeedFetchResult.NotModified:
                await _feeds.RecordSuccessAsync(
                    feedId, feed.ETag, feed.LastModified, now,
                    _backoff.NextDueAfterSuccess(now, settings), ct);
                return new FeedRefreshOutcome(feedId, true, 0, true, null);

            case FeedFetchResult.Failed failed:
                await RecordFailureAsync(feed, failed.Error, now, settings, ct);
                return new FeedRefreshOutcome(feedId, false, 0, false, failed.Error);

            case FeedFetchResult.Fetched fetched:
                return await StoreAsync(feed, fetched, settings, now, ct);

            default:
                throw new InvalidOperationException("Unreachable fetch result.");
        }
    }

    private async Task<FeedRefreshOutcome> StoreAsync(
        Feed feed,
        FeedFetchResult.Fetched fetched,
        EffectiveFeedSettings settings,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ParsedFeed parsed;
        try
        {
            parsed = _parser.Parse(fetched.Content, new Uri(feed.FeedUrl));
        }
        catch (Exception ex)
        {
            // A parse failure is a feed problem, not a crash, and it must not
            // touch the items we already have stored.
            await RecordFailureAsync(feed, ex.Message, now, settings, ct);
            return new FeedRefreshOutcome(feed.Id, false, 0, false, ex.Message);
        }

        var items = parsed.Items
            .Select(item => new FeedItem
            {
                FeedId = feed.Id,
                Guid = StableGuid(item),
                Link = item.Link,
                Title = item.Title,
                Author = item.Author,
                PublishedUtc = item.PublishedUtc,
                UpdatedUtc = item.UpdatedUtc,
                Summary = item.Summary,
                ContentMarkdown = null,
                ContentSource = ContentSource.Feed,
                FirstSeenUtc = now,
                OfflineState = settings.AutoDownload ? OfflineState.Pending : OfflineState.None
            })
            .ToList();

        var newCount = await _items.UpsertManyAsync(items, ct);

        // Adopt the feed's own title and site link, but never overwrite a title
        // the user set for themselves.
        if (parsed.Title is not null || parsed.SiteUrl is not null)
        {
            await _feeds.UpdateAsync(feed with
            {
                Title = parsed.Title ?? feed.Title,
                SiteUrl = parsed.SiteUrl ?? feed.SiteUrl
            }, ct);
        }

        await _feeds.RecordSuccessAsync(
            feed.Id, fetched.ETag, fetched.LastModified, now,
            _backoff.NextDueAfterSuccess(now, settings), ct);

        return new FeedRefreshOutcome(feed.Id, true, newCount, false, null);
    }

    private async Task RecordFailureAsync(
        Feed feed,
        string error,
        DateTimeOffset now,
        EffectiveFeedSettings settings,
        CancellationToken ct)
    {
        var failures = feed.ConsecutiveFailures + 1;
        await _feeds.RecordFailureAsync(
            feed.Id, error, now,
            _backoff.NextDueAfterFailure(now, failures, settings), ct);

        if (BackoffPolicy.ShouldAutoPause(failures) && feed.IsEnabled)
            await _feeds.UpdateAsync(feed with { IsEnabled = false }, ct);
    }

    /// <summary>
    /// The feed's own guid when it has one, otherwise a hash of the link. The
    /// hash has to be stable across refreshes, or every fetch would look like
    /// a fresh batch of items and the user's list would fill with duplicates.
    /// </summary>
    private static string StableGuid(ParsedItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Guid)) return item.Guid;

        var basis = item.Link ?? item.Title ?? string.Empty;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
        return "sha256:" + Convert.ToHexString(hash)[..32];
    }

    public async ValueTask DisposeAsync()
    {
        _coordinator.Complete();
        await _coordinator.DisposeAsync();
    }
}
