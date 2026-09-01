using System.Collections.Concurrent;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using MarkdownViewer.Services;
using Mostlylucid.Ephemeral;

namespace LucidReader.Core.Offline;

/// <summary>
/// Converts feed items into stored markdown, fetching the original page when
/// the feed only gave a teaser.
///
/// This runs on its own coordinator rather than sharing the refresh one: page
/// fetches take much longer than feed fetches, and a burst of new items must
/// not starve feed refreshing.
/// </summary>
public sealed class OfflineDownloader : IAsyncDisposable
{
    /// <summary>
    /// The coordinator requires an explicit bound, and rightly so: a server
    /// that accepts a connection and then stalls would otherwise hold its
    /// concurrency slot until the app closes.
    ///
    /// This bound is enforced twice, deliberately, for the exact same reason
    /// documented on FeedRefreshService.MaxFeedFetchDuration.
    /// Mostlylucid.Ephemeral 3.0.0's EphemeralWorkCoordinator races the queued
    /// body against a Task.WaitAsync(maxBodyDuration) call that lives OUTSIDE
    /// the body (verified by decompiling BodyDurationGuard.RunBoundedAsync):
    /// the token it hands the body is the coordinator's own long-lived
    /// shutdown token, never cancelled by the duration timer itself. If the
    /// coordinator's timer alone were relied on, a stalled article fetch would
    /// keep running forever as an orphaned task: this class's own token would
    /// never see cancellation, _inFlight would never be released, Completed
    /// would never fire, and no failure would ever be recorded - exactly the
    /// "server accepts a connection and stalls" scenario this bound exists to
    /// guard against. So the same duration is enforced again, independently,
    /// inside DownloadWithTimeoutGuardAsync, using a timer this class controls
    /// directly.
    /// </summary>
    public static readonly TimeSpan MaxArticleFetchDuration = TimeSpan.FromSeconds(180);

    private readonly ItemRepository _items;
    private readonly FeedRepository _feeds;
    private readonly ArticleFetcher _articles;
    private readonly IHtmlToMarkdownService _converter;
    private readonly Func<ReaderSettings> _settings;
    private readonly TimeSpan _maxFetchDuration;
    private readonly TimeSpan _drainTimeout;
    private readonly IArticleImageCache? _imageCache;
    private readonly EphemeralWorkCoordinator<long> _coordinator;
    private readonly ConcurrentDictionary<long, byte> _inFlight = new();

    public OfflineDownloader(
        ItemRepository items,
        FeedRepository feeds,
        ArticleFetcher articles,
        IHtmlToMarkdownService converter,
        Func<ReaderSettings> settings,
        TimeProvider timeProvider,
        int maxConcurrency = 2,
        TimeSpan? maxFetchDuration = null,
        IArticleImageCache? imageCache = null,
        TimeSpan? drainTimeout = null)
    {
        _items = items;
        _feeds = feeds;
        _articles = articles;
        _converter = converter;
        _settings = settings;
        _maxFetchDuration = maxFetchDuration ?? MaxArticleFetchDuration;
        _drainTimeout = drainTimeout ?? DefaultDrainTimeout;
        _imageCache = imageCache;

        _coordinator = new EphemeralWorkCoordinator<long>(
            RunAsync,
            _maxFetchDuration,
            new EphemeralOptions
            {
                MaxConcurrency = maxConcurrency,
                // A first sync of a large OPML import can produce thousands of
                // pending items at once, and the 200 default would block the
                // enqueueing caller.
                MaxTrackedOperations = 8192
            },
            timeProvider);
    }

    public int PendingCount => _coordinator.PendingCount;
    public int ActiveCount => _coordinator.ActiveCount;

    public event Action<long>? Completed;

    public bool TryQueue(long itemId)
    {
        if (!_inFlight.TryAdd(itemId, 0)) return false;

        if (_coordinator.TryEnqueue(itemId)) return true;

        _inFlight.TryRemove(itemId, out _);
        return false;
    }

    public async Task<int> QueuePendingAsync(int limit = 200, CancellationToken ct = default)
    {
        var pending = await _items.GetPendingOfflineAsync(limit, ct);
        return pending.Count(item => TryQueue(item.Id));
    }

    /// <summary>
    /// The coordinator's own body-duration bound does not reach this token
    /// (see the comment on MaxArticleFetchDuration), so nothing here relies on
    /// it. Everything else - releasing _inFlight, always raising Completed -
    /// happens whether the body below returns normally, hits the timeout guard
    /// inside DownloadNowAsync, or throws something unexpected out of a
    /// repository or the converter.
    ///
    /// `ct` cancelling here is a genuine caller-requested stop (the
    /// coordinator's own shutdown token, cancelled only by Cancel()/
    /// DisposeAsync, never by the per-body duration timer): Completed
    /// deliberately does not fire for it, since the whole downloader is
    /// stopping and there is no per-item failure to report.
    /// </summary>
    private async Task RunAsync(long itemId, CancellationToken ct)
    {
        try
        {
            try
            {
                await DownloadNowAsync(itemId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await RecordUnexpectedFailureAsync(itemId, ex, ct);
            }
        }
        finally
        {
            _inFlight.TryRemove(itemId, out _);
        }

        Completed?.Invoke(itemId);
    }

    /// <summary>
    /// Turns any otherwise-unhandled exception from a queued download into a
    /// recorded failure, so Completed still fires and the item is left in a
    /// state the reading pane can offer a retry from no matter what went
    /// wrong. Recording the failure is itself best-effort: the most likely
    /// reason this method runs at all is that the database is the thing that
    /// just failed, so a second failure while trying to write the first one
    /// is an expected possibility here, not a surprising one, and it must not
    /// prevent Completed from firing.
    /// </summary>
    private async Task RecordUnexpectedFailureAsync(long itemId, Exception ex, CancellationToken ct)
    {
        try
        {
            await _items.SetOfflineFailedAsync(itemId, ex.Message, ct);
        }
        catch (Exception)
        {
            // Best-effort, see the summary above.
        }
    }

    public Task DownloadNowAsync(long itemId, CancellationToken ct = default) =>
        DownloadWithTimeoutGuardAsync(itemId, ct);

    /// <summary>
    /// Runs one download under a timer this class owns, rather than trusting
    /// the coordinator's own body-duration bound to cancel anything (see the
    /// comment on MaxArticleFetchDuration for why that bound does not reach
    /// this token).
    ///
    /// `ct` cancelling is a genuine caller-requested stop; the linked
    /// timeoutCts firing on its own timer is a distinct, later condition. The
    /// two are told apart below by testing the ORIGINAL `ct` - not the linked
    /// token handed down into the download, which is cancelled in both cases -
    /// after catching: if `ct` itself is still live, only our own timer could
    /// have fired.
    /// </summary>
    private async Task DownloadWithTimeoutGuardAsync(long itemId, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_maxFetchDuration);

        try
        {
            await DownloadCoreAsync(itemId, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await RecordTimeoutFailureAsync(itemId, ct);
        }
    }

    /// <summary>
    /// Records a stalled download as an ordinary failure, so the reading pane
    /// has a useful error and a retry path instead of the item sitting in
    /// Pending forever.
    /// </summary>
    private async Task RecordTimeoutFailureAsync(long itemId, CancellationToken ct)
    {
        try
        {
            await _items.SetOfflineFailedAsync(
                itemId, "The download did not complete within the allotted time.", ct);
        }
        catch (Exception)
        {
            // Best-effort: a database failure here must not stop Completed
            // from eventually firing for this item (RunAsync's finally block
            // still runs regardless of what this write does).
        }
    }

    private async Task DownloadCoreAsync(long itemId, CancellationToken ct)
    {
        var item = await _items.GetAsync(itemId, ct);
        if (item is null) return;

        var feed = await _feeds.GetAsync(item.FeedId, ct);
        if (feed is null) return;

        var settings = EffectiveFeedSettings.Resolve(feed, _settings());

        // The richest thing the publisher sent, which is content_html when the
        // feed put a body in content:encoded or an Atom content element and the
        // summary otherwise. Reading only the summary here is what used to send
        // this downloader to the network for articles mylo had already been
        // given in full: alvinashcraft.com ships a 281-character description
        // beside a 27,536-character content:encoded, so every item looked like
        // a stub and every one cost a page fetch that could fail and leave the
        // reader with the teaser.
        var feedContent = item.ContentHtml ?? item.Summary;
        var isStub = StubDetector.IsStub(feedContent);

        var canFetchPage = settings.FetchFullText && !string.IsNullOrWhiteSpace(item.Link);

        // The feed already gave us the whole thing, or we are not allowed to
        // go looking for more. Either way, convert what we have.
        //
        // Which of those two it was is recorded, not flattened into "came from
        // the feed": a body that is not a stub IS the article, and marking it
        // FeedArticle is what stops the reading pane badging a complete post as
        // a summary and what stops NeedsFullArticle re-fetching it on every
        // open. Only the genuinely-a-teaser case stays ContentSource.Feed.
        if (!isStub || !canFetchPage)
        {
            var source = isStub ? ContentSource.Feed : ContentSource.FeedArticle;

            if (await StoreAsync(itemId, feedContent, item.Link, item.Title, source, ct))
                return;

            // The feed's body produced nothing usable - it was empty, or the
            // converter kept none of it. When there is no page to go to,
            // StoreAsync has already recorded that as a failure and the reading
            // pane can offer a retry. When there IS one, falling through and
            // fetching it is strictly better than leaving the reader with a
            // blank article, so this is deliberately not an early return.
            if (!canFetchPage) return;
        }

        var fetched = await _articles.FetchArticleAsync(item.Link!, ct);
        if (fetched is null)
        {
            await _items.SetOfflineFailedAsync(
                itemId, $"Could not fetch {item.Link}", ct);
            return;
        }

        try
        {
            var link = new Uri(item.Link);

            // The site handed back its own markdown source, either through
            // content negotiation or through a markdown alternate link.
            // Converting that would mean parsing prose as HTML and losing
            // whatever the converter does not round-trip, so it is stored as
            // written. There is no HTML in that case, and so no page for
            // SiteMetadataExtractor to read an image out of: passing no
            // imageUrl leaves any image already recorded for the item alone
            // rather than blanking it.
            var isMarkdown = fetched.Kind == ArticleBodyKind.Markdown;
            var markdown = isMarkdown
                ? fetched.Body
                : await _converter.ConvertAsync(fetched.Body, link, ct);

            // Applied to the site's own markdown too. That body is stored as
            // written in every other respect, but a page served as markdown
            // still opens with its own title heading, and the reading pane
            // has already drawn that heading above the body.
            markdown = ArticleMarkdownTidy.Clean(markdown, item.Title);

            markdown = await CacheImagesAsync(markdown, item.Link, ct);

            // Reads the SAME article html the converter above just consumed -
            // no second fetch. SiteMetadataExtractor.Extract is pure parsing
            // over a string already in memory.
            var imageUrl = isMarkdown
                ? null
                : SiteMetadataExtractor.Extract(fetched.Body, link).ImageUrl;

            await _items.SetContentAsync(itemId, markdown, ContentSource.Extracted, imageUrl, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _items.SetOfflineFailedAsync(itemId, ex.Message, ct);
        }
    }

    /// <summary>
    /// Stores content that came out of the feed document, not a fetched article
    /// page - the stub-with-no-link case and the already-complete-feed-content
    /// case in DownloadCoreAsync. Both go through the same HTML-to-markdown
    /// conversion, the same ArticleMarkdownTidy pass and the same image caching
    /// as a fetched page; the caller's `source` is the only thing that differs,
    /// and it says which of the two this was. There is no
    /// page here for SiteMetadataExtractor to read, so no image is ever
    /// captured on this path: SetContentAsync is called with no imageUrl,
    /// which leaves any existing image_url column untouched rather than
    /// blanking it.
    ///
    /// Returns whether an article was actually stored. False means the feed's
    /// body came to nothing - it was empty, or the conversion kept none of it -
    /// which is the caller's cue to go to the page instead if there is one. A
    /// blank body is never stored: an article that renders as nothing is worse
    /// than a recorded failure, because a failure carries a reason and a retry
    /// button and a blank one carries neither.
    /// </summary>
    private async Task<bool> StoreAsync(
        long itemId,
        string? html,
        string? link,
        string? itemTitle,
        ContentSource source,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            await _items.SetOfflineFailedAsync(itemId, "The feed supplied no content.", ct);
            return false;
        }

        try
        {
            var uri = Uri.TryCreate(link, UriKind.Absolute, out var parsed) ? parsed : null;

            // Wrapped, not passed through. The converter classifies blocks as
            // article or furniture, and a bare feed fragment has no document
            // around it to say which it is. See FeedContentHtml for the
            // measurement: unwrapped, alvinashcraft.com's content:encoded
            // converted to a single character.
            var markdown = await _converter.ConvertAsync(
                FeedContentHtml.AsDocument(html), uri, ct);
            markdown = ArticleMarkdownTidy.Clean(markdown, itemTitle);

            // Checked after the tidy, not before: Clean strips the article's
            // own title heading, and a body that was nothing but that heading
            // is nothing at all once it has gone.
            if (string.IsNullOrWhiteSpace(markdown))
            {
                await _items.SetOfflineFailedAsync(
                    itemId, "The feed's content could not be read as an article.", ct);
                return false;
            }

            markdown = await CacheImagesAsync(markdown, link, ct);
            await _items.SetContentAsync(itemId, markdown, source, ct: ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _items.SetOfflineFailedAsync(itemId, ex.Message, ct);
            return false;
        }
    }

    /// <summary>
    /// Rewrites remote image references to local cached copies when an image
    /// cache is configured. Best effort by design: a caching failure must not
    /// cost the user the article, which is the whole point of downloading it.
    /// </summary>
    private async Task<string> CacheImagesAsync(string markdown, string? link, CancellationToken ct)
    {
        if (_imageCache is null) return markdown;

        try
        {
            var baseUri = Uri.TryCreate(link, UriKind.Absolute, out var parsed) ? parsed : null;
            return await _imageCache.RewriteAsync(markdown, baseUri, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return markdown;
        }
    }

    /// <summary>
    /// How long disposal waits for downloads already dispatched to finish.
    /// An article fetch is bounded at MaxArticleFetchDuration (180s), so this
    /// is that plus slack.
    /// </summary>
    private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Complete, then drain, then dispose. Same reasoning as
    /// FeedRefreshService.DisposeAsync: the coordinator dispatches bodies
    /// fire-and-forget, so disposing without draining returned while a
    /// download was still fetching, and ReaderServices then closed the
    /// database under it - the finished article was thrown away with its
    /// SetContentAsync failing into a catch-all. Bounded so a stalled fetch
    /// cannot hang the quit.
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
