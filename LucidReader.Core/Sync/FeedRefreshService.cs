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
    ///
    /// This bound is enforced twice, deliberately. Mostlylucid.Ephemeral 3.0.0's
    /// EphemeralWorkCoordinator races the queued body against a
    /// Task.WaitAsync(maxBodyDuration) call that lives OUTSIDE the body (verified
    /// by decompiling BodyDurationGuard.RunBoundedAsync): the token it hands the
    /// body is the coordinator's own long-lived shutdown token, never cancelled
    /// by the duration timer itself. If the coordinator's timer alone were relied
    /// on, a stalled fetch would keep running forever as an orphaned task: this
    /// method's own token would never see cancellation, _inFlight would never be
    /// released, Completed would never fire, and no failure would ever be
    /// recorded - the exact "server accepts a connection and stalls" scenario
    /// this bound exists to guard against. So the same duration is enforced
    /// again, independently, inside RefreshWithTimeoutGuardAsync, using a timer
    /// this class controls directly.
    /// </summary>
    public static readonly TimeSpan MaxFeedFetchDuration = TimeSpan.FromSeconds(60);

    private readonly FeedRepository _feeds;
    private readonly ItemRepository _items;
    private readonly FeedFetcher _fetcher;
    private readonly IFeedParser _parser;
    private readonly BackoffPolicy _backoff;
    private readonly Func<ReaderSettings> _settings;
    private readonly TimeProvider _time;
    private readonly TimeSpan _maxFetchDuration;
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
        int maxConcurrency = 4,
        TimeSpan? maxFetchDuration = null)
    {
        _feeds = feeds;
        _items = items;
        _fetcher = fetcher;
        _parser = parser;
        _backoff = backoff;
        _settings = settings;
        _time = timeProvider;
        _maxFetchDuration = maxFetchDuration ?? MaxFeedFetchDuration;

        _coordinator = new EphemeralWorkCoordinator<FeedRefreshRequest>(
            RunAsync,
            _maxFetchDuration,
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
            try
            {
                outcome = await RefreshWithTimeoutGuardAsync(request.FeedId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // A genuine caller-requested stop (app shutdown, Coordinator.Cancel()):
                // RefreshWithTimeoutGuardAsync already turned its own timer firing into
                // an ordinary failure outcome above, so any OperationCanceledException
                // that reaches here can only be this. Nothing to record, and Completed
                // deliberately does not fire for it - there is no feed-level failure to
                // report, the whole app is stopping.
                throw;
            }
            catch (Exception ex)
            {
                // Anything else unhandled - a transient SQLite error, a disk-full
                // write, any unexpected failure from a repository call that (unlike
                // the parser's own try/catch in StoreAsync) is not otherwise guarded -
                // must still result in Completed firing. Left uncaught, this is exactly
                // the same "a caller waiting on Completed hangs forever, and no
                // bookkeeping is written" symptom the timeout guard above exists to
                // prevent, just triggered by a different kind of failure.
                outcome = await RecordUnexpectedFailureAsync(request.FeedId, ex, ct);
            }
        }
        finally
        {
            // Removed before Completed fires, and unconditionally on the way out
            // (including when the refresh throws): a subscriber reacting to
            // Completed by re-queueing the same feed must see it as available, and
            // a body that throws must not leave the feed permanently in flight.
            _inFlight.TryRemove(request.FeedId, out _);
        }

        Completed?.Invoke(outcome);
    }

    /// <summary>
    /// Refreshes one feed inline, bypassing the queue. Used by the synchronous
    /// refresh path and by tests. Runs under the same timeout guard as the
    /// queued path, for the same reason: a manual "refresh this feed" action
    /// should not hang forever against a stalled server either.
    /// </summary>
    public Task<FeedRefreshOutcome> RefreshNowAsync(long feedId, CancellationToken ct = default) =>
        RefreshWithTimeoutGuardAsync(feedId, ct);

    /// <summary>
    /// Runs one refresh under a timer this class owns, rather than trusting the
    /// coordinator's own body-duration bound to cancel anything (see the comment
    /// on MaxFeedFetchDuration for why that bound does not reach this token).
    ///
    /// `ct` cancelling is a genuine caller-requested stop: for the queued path
    /// that is Mostlylucid.Ephemeral's own coordinator-wide shutdown token,
    /// cancelled only by Pause-independent Cancel()/DisposeAsync, never by the
    /// per-body duration timer; for RefreshNowAsync it is whatever the caller
    /// passed in. The linked timeoutCts firing on its own timer is a distinct,
    /// later condition. The two are told apart below by testing the ORIGINAL
    /// `ct` - not the linked token handed down into the fetch, which is
    /// cancelled in both cases - after catching: if `ct` itself is still live,
    /// only our own timer could have fired.
    /// </summary>
    private async Task<FeedRefreshOutcome> RefreshWithTimeoutGuardAsync(long feedId, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_maxFetchDuration);

        try
        {
            return await RefreshCoreAsync(feedId, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await RecordTimeoutFailureAsync(feedId, ct);
        }
    }

    /// <summary>
    /// Records a stalled fetch as an ordinary failure: same backoff curve, same
    /// auto-pause counter, same NextDueUtc advancement as any other failure. The
    /// feed is re-read here rather than reusing anything from the timed-out
    /// attempt, since nothing from that attempt's snapshot can be trusted to
    /// still be current.
    /// </summary>
    private async Task<FeedRefreshOutcome> RecordTimeoutFailureAsync(long feedId, CancellationToken ct)
    {
        var feed = await _feeds.GetAsync(feedId, ct);
        if (feed is null)
            return new FeedRefreshOutcome(feedId, false, 0, false, "The feed no longer exists.");

        var settings = EffectiveFeedSettings.Resolve(feed, _settings());
        var now = _time.GetUtcNow();
        const string error = "The fetch did not complete within the allotted time.";

        await RecordFailureAsync(feed, error, now, settings, ct);
        return new FeedRefreshOutcome(feedId, false, 0, false, error);
    }

    /// <summary>
    /// Turns any otherwise-unhandled exception from a queued refresh into an
    /// ordinary recorded failure, so Completed still fires and backoff still
    /// advances no matter what went wrong.
    ///
    /// Recording the failure is itself best-effort: the most likely reason this
    /// method runs at all is that the database is the thing that just failed, so
    /// a second failure while trying to write the first one is an expected
    /// possibility here, not a surprising one. It must not prevent Completed from
    /// firing with the original error - that would reopen exactly the hole this
    /// method exists to close, just one exception later. The original error is
    /// always what reaches the caller through the outcome, regardless of whether
    /// the write below succeeded.
    /// </summary>
    private async Task<FeedRefreshOutcome> RecordUnexpectedFailureAsync(
        long feedId, Exception ex, CancellationToken ct)
    {
        try
        {
            var feed = await _feeds.GetAsync(feedId, ct);
            if (feed is not null)
            {
                var settings = EffectiveFeedSettings.Resolve(feed, _settings());
                var now = _time.GetUtcNow();
                await RecordFailureAsync(feed, ex.Message, now, settings, ct);
            }
        }
        catch (Exception)
        {
            // Best-effort, see the summary above.
        }

        return new FeedRefreshOutcome(feedId, false, 0, false, ex.Message);
    }

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
        // the user set for themselves. Written through a narrow update that
        // touches only these two columns: the `feed` in scope here is a
        // snapshot from the start of this refresh, and by now the user may
        // have edited the folder, overrides, enabled state or anything else
        // on the row while the fetch was in flight. Writing the whole record
        // back (UpdateAsync's normal contract) would silently revert that edit.
        if (parsed.Title is not null || parsed.SiteUrl is not null)
        {
            await _feeds.UpdateTitleAndSiteUrlAsync(
                feed.Id, parsed.Title ?? feed.Title, parsed.SiteUrl ?? feed.SiteUrl, ct);
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

        // Narrow update for the same reason as the title/site adoption above:
        // `feed` is a stale snapshot by the time auto-pause fires, and writing
        // the whole record back would revert whatever the user changed since.
        if (BackoffPolicy.ShouldAutoPause(failures) && feed.IsEnabled)
            await _feeds.SetEnabledAsync(feed.Id, false, ct);
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
