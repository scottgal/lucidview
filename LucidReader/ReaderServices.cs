using LucidReader.Core.Feeds;
using LucidReader.Core.Maintenance;
using LucidReader.Core.Model;
using LucidReader.Core.Offline;
using LucidReader.Core.Storage;
using LucidReader.Core.Sync;
using LucidReader.Services;
using MarkdownViewer.Services;
using Mostlylucid.LucidView.Markdown.Services;

namespace LucidReader;

/// <summary>
/// The composition root. LucidReader.Core deliberately ships no DI registration
/// and no factory, so this is the single place that knows how the engine is
/// assembled and, just as importantly, the order it must be torn down in.
///
/// Only one of these may exist per database path per process: ReaderDatabase
/// enforces that itself and throws on a second open.
/// </summary>
public sealed class ReaderServices : IAsyncDisposable
{
    private readonly string _settingsPath;
    private readonly HttpClient _http;
    private readonly ImageCacheService _imageCache;
    private readonly PeriodicTimer? _retentionTimer;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _retentionLoop;
    private readonly SemaphoreSlim _downloadQueueSignal = new(0);
    private readonly Task _downloadQueueLoop;
    private ReaderSettings _settings;
    private int _disposed;

    // Bounds how long DisposeAsync waits for the download-queue loop to drain
    // whatever QueuePendingAsync call was in flight or signalled when shutdown
    // began. A stuck call (network hang, wedged writer) must not hang app
    // shutdown forever; OfflineDownloader's own MaxArticleFetchDuration (180s)
    // is the thing we are actually bounding here, so this needs to be at least
    // that long plus slack.
    private static readonly TimeSpan DownloadQueueDrainTimeout = TimeSpan.FromSeconds(200);

    /// <summary>
    /// Test-only observation point (internal + InternalsVisibleTo, same as
    /// OnRefreshCompleted): lets a test assert that DisposeAsync actually
    /// awaited this task to completion, rather than merely asserting that
    /// disposal did not throw.
    /// </summary>
    internal Task DownloadQueueLoop => _downloadQueueLoop;

    private ReaderServices(
        string settingsPath,
        ReaderSettings settings,
        HttpClient http,
        ImageCacheService imageCache,
        ReaderDatabase database,
        FolderRepository folders,
        FeedRepository feeds,
        ItemRepository items,
        SearchRepository search,
        TagRepository tags,
        FeedRefreshService refresh,
        RefreshScheduler scheduler,
        OfflineDownloader downloader,
        RetentionService retention,
        ImageResolver images,
        IFeedSearch feedSearch,
        int fetchConcurrency,
        int downloadConcurrency)
    {
        _settingsPath = settingsPath;
        _settings = settings;
        _http = http;
        _imageCache = imageCache;
        Database = database;
        Folders = folders;
        Feeds = feeds;
        Items = items;
        Search = search;
        Tags = tags;
        Refresh = refresh;
        Scheduler = scheduler;
        Downloader = downloader;
        Retention = retention;
        Images = images;
        FeedSearch = feedSearch;
        ConfiguredFetchConcurrency = fetchConcurrency;
        ConfiguredDownloadConcurrency = downloadConcurrency;

        // The one hop that makes offline download actually happen. Nothing in
        // Core connects a finished refresh to the download queue.
        Refresh.Completed += OnRefreshCompleted;

        _retentionTimer = new PeriodicTimer(RetentionInterval);
        _retentionLoop = RunRetentionLoopAsync();
        _downloadQueueLoop = RunDownloadQueueLoopAsync();
    }

    private static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(6);

    public ReaderDatabase Database { get; }
    public FolderRepository Folders { get; }
    public FeedRepository Feeds { get; }
    public ItemRepository Items { get; }
    public SearchRepository Search { get; }
    public TagRepository Tags { get; }
    public FeedRefreshService Refresh { get; }
    public RefreshScheduler Scheduler { get; }
    public OfflineDownloader Downloader { get; }
    public RetentionService Retention { get; }

    /// <summary>
    /// Resolves a favicon or OpenGraph image URL to a local cached path for
    /// the sidebar/list/reading-pane surfaces (Task 8c). Built over the same
    /// ImageCacheService instance and live settings func as
    /// AvaloniaArticleImageCache, so the two share one on-disk cache and are
    /// governed by the same CacheImages switch - there is no second image
    /// pipeline here.
    /// </summary>
    public ImageResolver Images { get; }

    /// <summary>
    /// Topic feed search (Task 8d). Wired over the same HttpClient and live
    /// settings func as every other fetcher here, so it shares the app's
    /// infinite-timeout client rather than opening a second one, and it
    /// gates on the live EnableOnlineFeedSearch setting rather than the
    /// value that was in effect at startup.
    /// </summary>
    public IFeedSearch FeedSearch { get; }

    /// <summary>
    /// The one HttpClient this app owns. Exposed so short-lived helpers built
    /// in the UI layer (FeedAutodiscovery, per add-feed dialog) share this
    /// connection pool and this client's deliberately infinite timeout rather
    /// than constructing a second client per dialog. Internal, not public:
    /// this is a composition-root detail, not part of the app's surface.
    /// </summary>
    internal HttpClient Http => _http;

    public int ConfiguredFetchConcurrency { get; }
    public int ConfiguredDownloadConcurrency { get; }

    /// <summary>
    /// A non-fatal problem detected during startup, currently only a failed
    /// auto-vacuum conversion. Null when startup was clean. The shell surfaces
    /// this rather than letting it vanish into a static field nobody reads.
    /// </summary>
    public Exception? StartupWarning { get; private init; }

    public ReaderSettings Settings => Volatile.Read(ref _settings!);

    public event Action<ReaderSettings>? SettingsChanged;

    public static async Task<ReaderServices> StartAsync(
        string? databasePath = null,
        string? settingsPath = null,
        TimeProvider? timeProvider = null,
        CancellationToken ct = default)
    {
        var dbPath = databasePath ?? ReaderPaths.DefaultDatabasePath;
        var setPath = settingsPath ?? ReaderPaths.DefaultSettingsPath;
        var time = timeProvider ?? TimeProvider.System;

        var settings = await SettingsStore.LoadAsync(setPath, ct);
        var database = await ReaderDatabase.OpenAsync(dbPath, ct);

        var folders = new FolderRepository(database);
        var feeds = new FeedRepository(database);
        var items = new ItemRepository(database);
        var search = new SearchRepository(database);
        var tags = new TagRepository(database);

        // AllowAutoRedirect off, and redirects followed by PolicyHttpHandler
        // instead, one hop at a time with FeedUrlPolicy applied to each. The
        // default handler follows up to 50 hops silently, which turns every
        // pre-request policy check in the app (OPML import, autodiscovery,
        // the article fetch) into a check on the first URL only: a clean
        // public address answering 302 with a private Location was followed
        // and its body returned. Doing it here means the gate sits under
        // every request this app makes rather than at each call site.
        var http = new HttpClient(
            new PolicyHttpHandler(new SocketsHttpHandler { AllowAutoRedirect = false }))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        // Timeouts are enforced per operation by FeedRefreshService and
        // OfflineDownloader with their own linked cancellation tokens, because
        // the Ephemeral coordinator's maxBodyDuration does not cancel the body.
        // HttpClient's own timeout is therefore disabled to avoid two competing
        // clocks producing confusing errors.

        ReaderServices? built = null;
        ReaderSettings Current() => built?.Settings ?? settings;

        var fetchConcurrency = Math.Max(1, settings.MaxConcurrentFetches);
        var downloadConcurrency = Math.Max(1, settings.MaxConcurrentDownloads);

        var refresh = new FeedRefreshService(
            feeds, items,
            new FeedFetcher(http),
            new FeedParser(),
            new BackoffPolicy(),
            Current,
            time,
            fetchConcurrency);

        var scheduler = new RefreshScheduler(feeds, refresh, time);

        var imageCache = new ImageCacheService();

        var downloader = new OfflineDownloader(
            items, feeds,
            new ArticleFetcher(http),
            ResolveConverter(),
            Current,
            time,
            downloadConcurrency,
            imageCache: new AvaloniaArticleImageCache(
                new ImageCacheServiceRemoteImageFetcher(imageCache), Current));

        var retention = new RetentionService(database, feeds, Current, time);

        var images = new ImageResolver(new ImageCacheServiceRemoteImageFetcher(imageCache), Current);

        var feedSearch = new FeedlyFeedSearch(http, Current);

        built = new ReaderServices(
            setPath, settings, http, imageCache, database, folders, feeds, items, search,
            tags, refresh, scheduler, downloader, retention,
            images, feedSearch, fetchConcurrency, downloadConcurrency)
        {
            StartupWarning = SchemaMigrator.LastIncrementalVacuumConversionError
        };

        if (settings.RefreshOnStartup)
            scheduler.Start();

        return built;
    }

    /// <summary>
    /// The lean build converts article HTML with the AngleSharp and StyloExtract
    /// pipeline shared with lucidVIEW. Plan 3 substitutes the FULL implementation
    /// here behind an #if FULL, exactly as lucidVIEW does.
    /// </summary>
    private static IHtmlToMarkdownService ResolveConverter() => new HtmlToMarkdownService();

    /// <summary>
    /// Queues every item currently marked pending. Returns how many were queued.
    /// </summary>
    public Task<int> QueuePendingDownloadsAsync(int limit = 200, CancellationToken ct = default) =>
        Downloader.QueuePendingAsync(limit, ct);

    public async Task UpdateSettingsAsync(ReaderSettings settings, CancellationToken ct = default)
    {
        Volatile.Write(ref _settings!, settings);
        await SettingsStore.SaveAsync(_settingsPath, settings, ct);
        SettingsChanged?.Invoke(settings);
    }

    /// <summary>
    /// Runs on the refresh coordinator's thread, so this must be fast,
    /// non-blocking and non-throwing. SemaphoreSlim.Release() is exactly that:
    /// no I/O, no await, and the only exception it can raise
    /// (SemaphoreFullException, if the count would overflow int.MaxValue) is
    /// not a real-world concern here. The earlier version of this method used
    /// a bare `Task.Run` per completion, which is what let a queue call
    /// survive past DisposeAsync uncounted and undisposed-of: nothing tracked
    /// it, so disposal could not wait for it. Signalling a single long-lived
    /// loop instead means there is exactly one task to await at shutdown.
    ///
    /// internal rather than private, with InternalsVisibleTo to
    /// LucidReader.Core.Tests, so the disposal-ordering guarantee can be
    /// tested by simulating a refresh completion without spinning up a real
    /// HTTP feed and racing the coordinator's own concurrency (see
    /// Disposal_awaits_the_in_flight_download_queue_sweep_triggered_by_a_refresh_completion
    /// in ReaderServicesTests). It is still only ever wired up as an
    /// Action&lt;FeedRefreshOutcome&gt; event handler in production code.
    /// </summary>
    internal void OnRefreshCompleted(FeedRefreshOutcome outcome)
    {
        if (!outcome.Success || outcome.NewItemCount <= 0) return;

        try { _downloadQueueSignal.Release(); }
        catch (ObjectDisposedException) { }
        catch (SemaphoreFullException) { }
    }

    /// <summary>
    /// The only place QueuePendingAsync is called from a refresh completion.
    /// One signal is enough to trigger a sweep (QueuePendingAsync picks up
    /// everything currently pending, not just the feed that just refreshed),
    /// so this deliberately does not try to coalesce multiple pending signals
    /// into one sweep: a few redundant sweeps in a row are cheap and harmless,
    /// and a coalescing channel would add complexity for no real benefit here.
    /// </summary>
    private async Task RunDownloadQueueLoopAsync()
    {
        try
        {
            while (true)
            {
                await _downloadQueueSignal.WaitAsync(_shutdown.Token);
                try { await Downloader.QueuePendingAsync(ct: _shutdown.Token); }
                catch (OperationCanceledException) { }
                catch (Exception) { /* items stay pending; the next sweep picks them up */ }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunRetentionLoopAsync()
    {
        if (_retentionTimer is null) return;

        try
        {
            while (await _retentionTimer.WaitForNextTickAsync(_shutdown.Token))
            {
                try { await Retention.PruneAsync(_shutdown.Token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { /* a failed prune must not kill the loop */ }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Shutdown order matters and nothing in Core enforces it. Stop scheduling
    /// first so no new work is queued, then drain the loops whose in-flight
    /// work still writes to the database, then drain the two coordinators, and
    /// only then close the database itself.
    ///
    /// The download-queue loop matters here specifically: a refresh that
    /// completes just before shutdown signals it to run QueuePendingAsync,
    /// and that call must finish (or be given up on, under the bound) before
    /// Downloader.DisposeAsync() and Database.DisposeAsync() run a few lines
    /// later. Awaiting it here, with a bound so a stuck call cannot hang
    /// shutdown forever, is what makes "await services.DisposeAsync() means
    /// everything has stopped" actually true.
    ///
    /// What this does NOT promise, and did claim before: that every dispatched
    /// body has finished. FeedRefreshService and OfflineDownloader now drain
    /// their coordinators before disposing them, under their own bounds, so a
    /// download that was mid-flight when shutdown began normally gets to write
    /// its content before the database closes. A body that outlives that bound
    /// is still abandoned, and its write will fail against a closed writer.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Refresh.Completed -= OnRefreshCompleted;

        // Scheduling stops first, before the cancel and before either drain.
        // This used to sit below both, and it mattered: RefreshScheduler reads
        // its own _stopping flag, not _shutdown, so cancelling _shutdown never
        // stopped it. Its timer kept firing for the whole drain window - up to
        // the 200 second download-queue bound - queuing fresh HTTP fetches and
        // database writes into an engine that was being torn down.
        await Scheduler.DisposeAsync();

        await _shutdown.CancelAsync();
        _retentionTimer?.Dispose();
        try { await _retentionLoop; } catch (OperationCanceledException) { }

        try { await _downloadQueueLoop.WaitAsync(DownloadQueueDrainTimeout); }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { /* best effort; disposal must still proceed */ }

        await Refresh.DisposeAsync();
        await Downloader.DisposeAsync();
        await Database.DisposeAsync();

        _http.Dispose();
        _imageCache.Dispose();
        _shutdown.Dispose();
        _downloadQueueSignal.Dispose();
    }
}
