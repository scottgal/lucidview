using LucidReader.Core.Feeds;
using LucidReader.Core.Maintenance;
using LucidReader.Core.Model;
using LucidReader.Core.Offline;
using LucidReader.Core.Storage;
using LucidReader.Core.Sync;
using MarkdownViewer.Services;

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
    private readonly PeriodicTimer? _retentionTimer;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _retentionLoop;
    private ReaderSettings _settings;
    private int _disposed;

    private ReaderServices(
        string settingsPath,
        ReaderSettings settings,
        HttpClient http,
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
        int fetchConcurrency,
        int downloadConcurrency)
    {
        _settingsPath = settingsPath;
        _settings = settings;
        _http = http;
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
        ConfiguredFetchConcurrency = fetchConcurrency;
        ConfiguredDownloadConcurrency = downloadConcurrency;

        // The one hop that makes offline download actually happen. Nothing in
        // Core connects a finished refresh to the download queue.
        Refresh.Completed += OnRefreshCompleted;

        _retentionTimer = new PeriodicTimer(RetentionInterval);
        _retentionLoop = RunRetentionLoopAsync();
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

        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

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

        var downloader = new OfflineDownloader(
            items, feeds,
            new ArticleFetcher(http),
            ResolveConverter(),
            Current,
            time,
            downloadConcurrency);

        var retention = new RetentionService(database, feeds, Current, time);

        built = new ReaderServices(
            setPath, settings, http, database, folders, feeds, items, search,
            tags, refresh, scheduler, downloader, retention,
            fetchConcurrency, downloadConcurrency)
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

    private void OnRefreshCompleted(FeedRefreshOutcome outcome)
    {
        if (!outcome.Success || outcome.NewItemCount <= 0) return;

        // Fire and forget on purpose: this runs on the refresh coordinator's
        // thread and must not block it. Failures here are not fatal, the items
        // stay pending and the next queue sweep picks them up.
        _ = Task.Run(async () =>
        {
            try { await Downloader.QueuePendingAsync(ct: _shutdown.Token); }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        });
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
    /// first so no new work is queued, then drain the two coordinators whose
    /// in-flight work still writes to the database, and only then close the
    /// database itself.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Refresh.Completed -= OnRefreshCompleted;

        await _shutdown.CancelAsync();
        _retentionTimer?.Dispose();
        try { await _retentionLoop; } catch (OperationCanceledException) { }

        await Scheduler.DisposeAsync();
        await Refresh.DisposeAsync();
        await Downloader.DisposeAsync();
        await Database.DisposeAsync();

        _http.Dispose();
        _shutdown.Dispose();
    }
}
