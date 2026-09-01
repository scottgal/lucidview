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
    private readonly TagRepository _tags;
    private readonly FeedFetcher _fetcher;
    private readonly IFeedParser _parser;
    private readonly BackoffPolicy _backoff;
    private readonly Func<ReaderSettings> _settings;
    private readonly TimeProvider _time;
    private readonly TimeSpan _maxFetchDuration;
    private readonly TimeSpan _drainTimeout;
    private readonly EphemeralWorkCoordinator<FeedRefreshRequest> _coordinator;
    private readonly ConcurrentDictionary<long, byte> _inFlight = new();

    /// <summary>
    /// Where a scraped feed's learned template is kept, or null when this
    /// service is running without a profile directory, which is what most
    /// tests are. Null means every scrape asks the detector, which is what
    /// every scrape did before templates existed.
    /// </summary>
    private readonly ScrapeTemplateStore? _scrapeTemplates;

    /// <summary>
    /// Fills in an icon for a feed that has none, or null when this service is
    /// running without one - which most tests are, and which simply means no
    /// icon is ever backfilled. See <see cref="BackfillIconAsync"/>.
    /// </summary>
    private readonly FeedIconResolver? _icons;

    public FeedRefreshService(
        FeedRepository feeds,
        ItemRepository items,
        TagRepository tags,
        FeedFetcher fetcher,
        IFeedParser parser,
        BackoffPolicy backoff,
        Func<ReaderSettings> settings,
        TimeProvider timeProvider,
        int maxConcurrency = 4,
        TimeSpan? maxFetchDuration = null,
        TimeSpan? drainTimeout = null,
        ScrapeTemplateStore? scrapeTemplates = null,
        FeedIconResolver? icons = null)
    {
        _icons = icons;
        _scrapeTemplates = scrapeTemplates;
        _feeds = feeds;
        _items = items;
        _tags = tags;
        _fetcher = fetcher;
        _parser = parser;
        _backoff = backoff;
        _settings = settings;
        _time = timeProvider;
        _maxFetchDuration = maxFetchDuration ?? MaxFeedFetchDuration;
        _drainTimeout = drainTimeout ?? DefaultDrainTimeout;

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
    /// Whether this feed is queued or currently being fetched. The same set
    /// <see cref="TryQueue"/> coalesces on, exposed so a UI can say "refreshing
    /// now" for the feed the user is looking at instead of having to keep its
    /// own guess at what is in flight.
    /// </summary>
    public bool IsInFlight(long feedId) => _inFlight.ContainsKey(feedId);

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
    /// Refreshes one feed inline, without going through the queue. Used by the
    /// synchronous refresh path and by tests. Runs under the same timeout guard
    /// as the queued path, for the same reason: a manual "refresh this feed"
    /// action should not hang forever against a stalled server either.
    ///
    /// It also takes the same _inFlight slot the queued path takes. Skipping
    /// that was what made two refreshes of one feed overlap without needing a
    /// timing coincidence at all: a scheduler tick and a click on Refresh land
    /// on the same feed routinely, and both would then fetch, both would store,
    /// and both would record failures against a counter each had read before
    /// the other wrote. When the slot is already taken there is a refresh of
    /// this feed running right now, so this reports "nothing changed" rather
    /// than starting a second one; it is not a failure and nothing is recorded
    /// against the feed for it.
    /// </summary>
    public async Task<FeedRefreshOutcome> RefreshNowAsync(long feedId, CancellationToken ct = default)
    {
        if (!_inFlight.TryAdd(feedId, 0))
            return new FeedRefreshOutcome(feedId, true, 0, true, null);

        try
        {
            return await RefreshWithTimeoutGuardAsync(feedId, ct);
        }
        finally
        {
            _inFlight.TryRemove(feedId, out _);
        }
    }

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
            parsed = feed.IsScraped
                ? await ScrapeAsync(feed, fetched.Content, ct)
                : _parser.Parse(fetched.Content, new Uri(feed.FeedUrl));
        }
        catch (FeedScrapeException ex)
        {
            // A scrape that stops finding articles is the failure this feature
            // most has to get right. The site changed its markup, or started
            // rendering its list in JavaScript, or moved the page - and every
            // one of those looks exactly like "nothing new today" if it is
            // recorded as a success with zero items. It is recorded as a
            // failure instead, which puts the reason on the feed row, marks the
            // sidebar row with a problem, backs the schedule off, and after
            // BackoffPolicy.AutoPauseThreshold consecutive failures auto-pauses
            // the feed and reports it in the status bar's health line. So a
            // broken scrape surfaces on the same path a dead feed does rather
            // than going quiet forever.
            await RecordFailureAsync(feed, ex.Message, now, settings, ct);
            return new FeedRefreshOutcome(feed.Id, false, 0, false, ex.Message);
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
                ContentHtml = RicherThanSummary(item),
                ContentMarkdown = null,
                ContentSource = ContentSource.Feed,
                // The picture the feed named for this item, where it named
                // one. Null for a publisher that names none, which leaves the
                // item exactly where it was before: waiting for
                // OfflineDownloader to find an og:image on the article page.
                // The upsert fills a null image_url and never overwrites a set
                // one, so this cannot displace a picture already captured from
                // the page (see ItemRepository's upsert).
                ImageUrl = item.ImageUrl,
                FirstSeenUtc = now,
                OfflineState = settings.AutoDownload ? OfflineState.Pending : OfflineState.None
            })
            .ToList();

        var outcomes = await _items.UpsertBatchAsync(items, ct);
        var newCount = outcomes.Count(outcome => outcome.IsNewRow);

        await ImportPublisherCategoriesAsync(parsed.Items, outcomes, ct);

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

        await BackfillIconAsync(feed, parsed, ct);

        return new FeedRefreshOutcome(feed.Id, true, newCount, false, null);
    }

    /// <summary>
    /// Gives a feed with no icon a chance at one, on every successful refresh.
    ///
    /// This is here, rather than at the point a subscription is created,
    /// because there are five ways to create one and only ever one of them
    /// looked: the Add Feed dialog got an icon out of autodiscovery, and the
    /// starter feeds a first run seeds, an OPML import, a pasted feed address
    /// and the catalogue all wrote a null icon_path that nothing would ever
    /// come back and fill. Refresh is the one path every subscription takes,
    /// whatever created it, so it is the one place a fix reaches all of them -
    /// including the rows already sitting in a profile with a grey placeholder
    /// beside them.
    ///
    /// Deliberately AFTER RecordSuccessAsync and outside everything the outcome
    /// depends on. An icon is a nicety; a fetch for one must never turn a
    /// refresh that worked into a refresh that failed, must never touch the
    /// backoff curve, and must never delay the items that have already been
    /// stored. Hence the catch-all, and hence resolving nothing at all when
    /// this service was built without a resolver.
    ///
    /// The snapshot's icon_path is what gates the work, so a feed that has an
    /// icon costs one field test and no request, on every refresh, forever. The
    /// write itself re-checks the same condition in SQL, because the snapshot
    /// can be stale by the time it runs.
    /// </summary>
    private async Task BackfillIconAsync(Feed feed, ParsedFeed parsed, CancellationToken ct)
    {
        if (_icons is null) return;
        if (!string.IsNullOrWhiteSpace(feed.IconPath)) return;

        try
        {
            var icon = await _icons.ResolveAsync(
                feed.FeedUrl, parsed.SiteUrl ?? feed.SiteUrl, parsed.IconUrl, ct);

            if (icon is not null)
                await _feeds.UpdateIconPathIfMissingAsync(feed.Id, icon, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The app is stopping. Nothing to record and nothing to retry; the
            // next refresh of this feed will look again.
        }
        catch (Exception)
        {
            // Best effort by design, see the summary above. The feed keeps its
            // placeholder and the next refresh tries again.
        }
    }

    /// <summary>
    /// Applies the categories a publisher put on an item as tags, ONCE, when
    /// the article first enters this database. Never again.
    ///
    /// The rule is the whole design, so it is worth stating why it is that and
    /// not "keep the tags in step with the feed".
    ///
    /// A feed's XML window relists every item on every fetch, categories and
    /// all. Re-applying them on each poll would mean a user who takes a tag
    /// off an article is arguing with the poller: the tag comes back within
    /// the refresh interval, with nothing on screen to say why, and no way to
    /// win short of unsubscribing. A tag in this app is the user's own filing
    /// - they can rename it across every article, merge it into another, and
    /// delete it outright - so a write the user can undo but cannot make stick
    /// is the one behaviour that would make the whole Tags section untrustworthy.
    /// Importing once gives the publisher's suggestion and then gets out of the
    /// way, which is what every other reader-owned column already does (see the
    /// SET list ItemRepository's upsert deliberately leaves alone).
    ///
    /// The cost, stated plainly: a category a publisher adds to an article
    /// after we first stored it never arrives. That is the right way round.
    /// Missing a late category costs the user a tag they can type in three
    /// seconds; re-adding a removed one costs them the ability to curate at all.
    ///
    /// IsNewArticle, not merely IsNewRow, is what gates the import, and that is
    /// the dedupe interaction. The same article routinely arrives twice, under
    /// an RSS feed and an Atom one, as two rows sharing a canonical_id; read
    /// and starred already propagate across those twins, and so does tagging
    /// (see TagRepository's doc comment - AddToItemAsync writes to every copy).
    /// So a second copy arriving is a new ROW but not a new ARTICLE, and
    /// importing its categories would re-apply, to the article the user has
    /// already curated, exactly the tags they removed from it - the very thing
    /// the once-only rule exists to prevent, arriving by the back door the
    /// moment they subscribe to the same site's other feed. The article
    /// already carries whatever tags it should; the new copy inherits them
    /// through the twin write, and nothing is imported for it.
    ///
    /// No schema change was made for this, and the tags stored here are not
    /// marked as the publisher's. Telling them apart would need a per-link
    /// origin column on item_tags (V9), and it would buy nothing that can be
    /// acted on. An imported tag is an ordinary tag in every respect that
    /// matters: the user can rename it, merge it, remove it from one article
    /// or delete it everywhere, and once they have done any of those the
    /// origin marker is either wrong or meaningless. The sidebar's Tags
    /// section, which is the place tags are actually used, aggregates by NAME
    /// across every article, and a name will routinely have both origins at
    /// once - "Architecture" imported from one post and typed onto another -
    /// so there is no honest badge to put on that row at all. A marker on the
    /// chip would be a distinction the user cannot use and cannot correct.
    ///
    /// A tag write failing here is not swallowed. It is a write to the same
    /// database the items just went into, so if it fails the refresh really
    /// did go wrong, and letting it out means the feed's failure is recorded
    /// and reported the same way any other one is.
    /// </summary>
    private async Task ImportPublisherCategoriesAsync(
        IReadOnlyList<ParsedItem> parsed,
        IReadOnlyList<ItemUpsertOutcome> outcomes,
        CancellationToken ct)
    {
        // Positional: UpsertBatchAsync reports one outcome per item passed, in
        // order, and the items were built from parsed.Items in order.
        for (var i = 0; i < outcomes.Count && i < parsed.Count; i++)
        {
            if (outcomes[i] is not { IsNewRow: true, IsNewArticle: true, Id: { } itemId }) continue;

            foreach (var category in parsed[i].Categories)
                await _tags.AddToItemAsync(itemId, category, ct);
        }
    }

    private async Task RecordFailureAsync(
        Feed feed,
        string error,
        DateTimeOffset now,
        EffectiveFeedSettings settings,
        CancellationToken ct)
    {
        // The backoff step is computed from the snapshot's count, which is only
        // ever used to space out the next attempt and is harmless if it is one
        // behind. The auto-pause decision is not: it comes from what
        // RecordFailureAsync read back after incrementing in SQL. Deciding it
        // from `feed.ConsecutiveFailures + 1` meant two overlapping refreshes
        // of one feed could both compute 4 while the database went to 5, so the
        // threshold was stepped over rather than hit and a dead feed never
        // paused. The returned is_enabled matters for the same reason: the
        // snapshot could say enabled for a feed the user paused, or paused for
        // one they just resumed, during the fetch.
        var projected = feed.ConsecutiveFailures + 1;
        var state = await _feeds.RecordFailureAsync(
            feed.Id, error, now,
            _backoff.NextDueAfterFailure(now, projected, settings), ct);

        if (!state.Found) return;

        // Narrow update for the same reason as the title/site adoption above:
        // `feed` is a stale snapshot by the time auto-pause fires, and writing
        // the whole record back would revert whatever the user changed since.
        // AutoPauseAsync (not SetEnabledAsync's disable branch) so the feed's
        // auto_paused_utc records that this disable was automatic, not a
        // deliberate user action - see FeedRepository.SetEnabledAsync's remarks.
        if (BackoffPolicy.ShouldAutoPause(state.ConsecutiveFailures) && state.IsEnabled)
            await _feeds.AutoPauseAsync(feed.Id, now, ct);
    }

    /// <summary>
    /// Reads a scraped page's articles, in place of the XML parse a published
    /// feed gets. Either from a template learned on an earlier refresh or, when
    /// there is none that still holds, from the article-list detector.
    ///
    /// Everything downstream of this is unchanged, which is the whole design:
    /// the detection is turned into a ParsedFeed and stored by exactly the code
    /// that stores a real feed's items, so canonical_id dedupe, tombstones,
    /// tags, read and starred state, retention and the offline queue all keep
    /// working without knowing a scrape happened. The guid is the canonical id,
    /// so an article scraped today and the same article arriving tomorrow from
    /// a feed the site later publishes are one item, not two.
    ///
    /// A refresh that finds nothing throws rather than storing an empty batch.
    /// See the catch in StoreAsync for why that matters, and
    /// <see cref="ScrapedPageReader"/> for how a stored template is checked
    /// before its answer is used.
    /// </summary>
    private async Task<ParsedFeed> ScrapeAsync(Feed feed, string html, CancellationToken ct)
    {
        var pageUri = new Uri(feed.FeedUrl);
        var reading = await ScrapedPageReader.ReadAsync(html, pageUri, _scrapeTemplates, ct);

        // The page's title is not re-read here. A scraped feed is named once,
        // when the user approves it, and a site that changes its title element
        // must not silently rename a subscription the user has filed and sorted.
        return reading.Articles.ToParsedFeed(feed.Title, pageUri);
    }

    /// <summary>
    /// The publisher's full body when the feed offered one, or null when all it
    /// offered was the summary already being stored beside it.
    ///
    /// FeedParser sets ContentHtml to "content:encoded, or failing that the
    /// description", so for the majority of feeds - the ones that publish a
    /// teaser and nothing else - it is character-for-character the summary.
    /// Writing it anyway would double the stored size of every such item to
    /// hold a second copy of the same string, forever, on every feed the user
    /// subscribes to. Null instead, which is exactly what the column means
    /// ("the feed gave us nothing richer than the summary"), and what
    /// OfflineDownloader already falls back to.
    ///
    /// Ordinal comparison, not a trimmed or normalised one: the two strings
    /// come from the same parse of the same document and are either the same
    /// object's value or genuinely different content.
    /// </summary>
    private static string? RicherThanSummary(ParsedItem item) =>
        string.Equals(item.ContentHtml, item.Summary, StringComparison.Ordinal)
            ? null
            : item.ContentHtml;

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

    /// <summary>
    /// How long disposal waits for refreshes already dispatched to finish
    /// before giving up on them.
    ///
    /// Deliberately short, and NOT MaxFeedFetchDuration plus slack. The drain
    /// is here so a refresh that is milliseconds from writing gets to finish,
    /// not so a wedged fetch can hold the quit open: App.axaml.cs bounds the
    /// whole shutdown at 10 seconds, so anything longer than that is time the
    /// app cannot spend anyway. Sizing it at 70s also made
    /// FeedRefreshServiceTests take 70 seconds, because a test that disposes
    /// with a deliberately stalled body waits out the entire bound.
    /// </summary>
    private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Complete, then drain, then dispose.
    ///
    /// The drain in the middle is the part that used to be missing.
    /// EphemeralWorkCoordinator dispatches each body fire-and-forget, and its
    /// DisposeAsync cancels and then awaits only the processing loop - which
    /// is parked in ReadAllAsync and so completes as cancelled immediately.
    /// DisposeAsync therefore returned while dispatched refreshes were still
    /// running, and ReaderServices closed the database underneath them a few
    /// lines later: their writes threw ChannelClosedException into a catch-all
    /// and the work was lost with no trace. Draining first gives them the
    /// chance to finish, bounded so a wedged fetch cannot hang the quit.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _coordinator.Complete();

        try { await _coordinator.DrainAsync().WaitAsync(_drainTimeout); }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { /* best effort; disposal must still proceed */ }

        await _coordinator.DisposeAsync();
    }
}
