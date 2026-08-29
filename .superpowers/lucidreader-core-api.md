# LucidReader.Core public API reference

Generated from the source at the Plan 1 merge commit. Authoritative over any signature quoted in a plan task. If this disagrees with the source, the source wins and this file should be corrected.

`net10.0`, nullable enabled. **There is no composition root, no DI registration and no factory in this project.** Everything is constructed by hand; see "Ordering" at the end.

## Package and project references

- `Microsoft.Data.Sqlite` 10.0.9
- `Mostlylucid.Ephemeral` 3.0.0
- `Mostlylucid.Ephemeral.Sqlite.SingleWriter` 3.0.0
- `Mostlylucid.Ephemeral.Atoms.Retry` 3.0.0
- `System.Text.Encoding.CodePages` 10.0.0
- ProjectReference: `Mostlylucid.LucidView.Content`
- `InternalsVisibleTo`: `LucidReader.Core.Tests`

A `[ModuleInitializer]` registers `CodePagesEncodingProvider` when the assembly loads, so legacy code pages decode with no setup call.

---

## LucidReader.Core.Model

```csharp
public enum ContentSource { Feed = 0, Extracted = 1 }
public enum OfflineState { None = 0, Pending = 1, Downloaded = 2, Failed = 3 }

public sealed record Folder
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public int SortOrder { get; init; }
    public long? ParentId { get; init; }
}

public sealed record Feed
{
    public long Id { get; init; }
    public long? FolderId { get; init; }
    public required string FeedUrl { get; init; }
    public string? SiteUrl { get; init; }
    public string? Title { get; init; }
    public string? TitleOverride { get; init; }
    public string? IconPath { get; init; }
    public bool IsEnabled { get; init; } = true;
    public DateTimeOffset? LastFetchedUtc { get; init; }
    public DateTimeOffset? LastSuccessUtc { get; init; }
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
    public int ConsecutiveFailures { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset? NextDueUtc { get; init; }
    public DateTimeOffset? AutoPausedUtc { get; init; }
    public int? RefreshIntervalMinutes { get; init; }
    public bool? AutoDownload { get; init; }
    public bool? FetchFullText { get; init; }
    public int? RetentionDays { get; init; }
    public string DisplayTitle { get; }   // TitleOverride, then Title, then FeedUrl
}

public sealed record FeedItem
{
    public long Id { get; init; }
    public long FeedId { get; init; }
    public required string Guid { get; init; }
    public string? Link { get; init; }
    public string? Title { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PublishedUtc { get; init; }
    public DateTimeOffset? UpdatedUtc { get; init; }
    public string? Summary { get; init; }
    public string? ContentMarkdown { get; init; }
    public ContentSource ContentSource { get; init; }
    public bool IsRead { get; init; }
    public bool IsStarred { get; init; }
    public DateTimeOffset FirstSeenUtc { get; init; }
    public OfflineState OfflineState { get; init; }
    public string? OfflineError { get; init; }
}
```

**`AutoPausedUtc` distinguishes an automatic pause from a user disable.** Set only by `FeedRepository.AutoPauseAsync`. Null for a feed the user disabled by hand. Cleared by `SetEnabledAsync(true)`.

### ReaderSettings

`public sealed record`. `static readonly TimeSpan MinimumRefreshInterval = 5 minutes` (a floor, not a default). `static ReaderSettings Defaults { get; }`.

| Property | Type | Default |
|---|---|---|
| DefaultRefreshIntervalMinutes | int | 30 |
| RefreshOnStartup | bool | true |
| PauseWhenOffline | bool | true |
| MaxConcurrentFetches | int | 4 |
| AutoDownloadArticles | bool | true |
| FetchFullText | bool | true |
| CacheImages | bool | true |
| MaxImageBytes | int | 5242880 |
| MaxConcurrentDownloads | int | 2 |
| KeepReadArticlesDays | int | 30 |
| KeepUnreadForever | bool | true |
| KeepUnreadDays | int | 180 |
| MaxArticlesPerFeed | int | 500 |
| NeverDeleteStarred | bool | true |
| Theme | string | "Auto" |
| FontSize | double | 15 |
| ColumnWidth | double | 760 |
| MarkReadDwellMilliseconds | int | 800 |
| OpenLinksExternally | bool | true |

### EffectiveFeedSettings

```csharp
public readonly record struct EffectiveFeedSettings(
    TimeSpan RefreshInterval, bool AutoDownload, bool FetchFullText, int? RetentionDays)
{
    public static EffectiveFeedSettings Resolve(Feed feed, ReaderSettings globals);
}
```

Null per-feed value means inherit. `RefreshInterval` is clamped up to `MinimumRefreshInterval`. `RetentionDays` falls back to `globals.KeepReadArticlesDays` and in practice is never null.

---

## LucidReader.Core.Storage

### ReaderDatabase — `sealed`, `IAsyncDisposable`, private constructor

```csharp
public static Task<ReaderDatabase> OpenAsync(string databasePath, CancellationToken ct = default);
public string ConnectionString { get; }
public SqliteSingleWriter Writer { get; }
public Task<T> QueryAsync<T>(Func<SqliteConnection, Task<T>> reader, CancellationToken ct = default);
public Task<int> WriteAsync(string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default);
public Task<long> WriteReturningIdAsync(string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default);
public ValueTask DisposeAsync();
```

- **One instance per database path per process.** A second `OpenAsync` on the same normalised path while the first is undisposed throws `InvalidOperationException`. The underlying `SqliteSingleWriter` is process-wide shared, so disposing one instance would break the other.
- `DisposeAsync` is idempotent.
- WAL on, foreign keys enforced on every connection, shared cache deliberately OFF (it breaks concurrent reads).
- `WriteAsync` returns rows affected. Parameters are `$name` style; the class binds them itself.

### Other storage types

```csharp
public static class ReaderPaths
{
    public const string AppFolderName = "lucidREADER";
    public static string AppDataDirectory { get; }
    public static string DefaultDatabasePath { get; }    // <AppData>/lucidREADER/reader.db
    public static string DefaultSettingsPath { get; }    // <AppData>/lucidREADER/settings.json
}

public static class SchemaMigrator
{
    public static Task<int> MigrateAsync(SqliteConnection connection, CancellationToken ct = default);
    public static Exception? LastIncrementalVacuumConversionError { get; }
}

public static class Migrations { public static IReadOnlyList<string> All { get; } }  // V1, V2

public static class SettingsStore
{
    public static Task<ReaderSettings> LoadAsync(string path, CancellationToken ct = default);
    public static Task SaveAsync(string path, ReaderSettings settings, CancellationToken ct = default);
}
```

`LoadAsync` never throws: a corrupt file is copied aside to `<path>.corrupt` and defaults are returned. `SaveAsync` writes to a temp file then moves it into place.

### FeedRepository — NOT sealed (tests override `GetDueAsync`)

```csharp
public FeedRepository(ReaderDatabase db);

public Task<long> AddAsync(Feed feed, CancellationToken ct = default);
public Task<Feed?> GetAsync(long id, CancellationToken ct = default);
public Task<Feed?> GetByUrlAsync(string feedUrl, CancellationToken ct = default);
public Task<IReadOnlyList<Feed>> GetAllAsync(CancellationToken ct = default);
public virtual Task<IReadOnlyList<Feed>> GetDueAsync(DateTimeOffset nowUtc, int limit, CancellationToken ct = default);
public Task UpdateAsync(Feed feed, CancellationToken ct = default);
public Task RecordSuccessAsync(long feedId, string? etag, string? lastModified, DateTimeOffset nowUtc, DateTimeOffset nextDueUtc, CancellationToken ct = default);
public Task RecordFailureAsync(long feedId, string error, DateTimeOffset nowUtc, DateTimeOffset nextDueUtc, CancellationToken ct = default);
public Task DeleteAsync(long id, CancellationToken ct = default);
public Task UpdateTitleAndSiteUrlAsync(long feedId, string? title, string? siteUrl, CancellationToken ct = default);
public Task SetEnabledAsync(long feedId, bool isEnabled, CancellationToken ct = default);
public Task AutoPauseAsync(long feedId, DateTimeOffset nowUtc, CancellationToken ct = default);
```

- `UpdateAsync` never writes etag, last-modified, failure count, last error or next-due, so it cannot clobber fetch bookkeeping. It DOES write folder, title override, icon, enabled and all four overrides.
- `SetEnabledAsync(true)` also clears `consecutive_failures`, `last_error` and `auto_paused_utc`. Without that a re-enabled feed is auto-paused again on its first failure.
- `SetEnabledAsync(false)` touches only `is_enabled`, so a manual disable is never mistaken for an auto-pause.

### ItemRepository — `sealed`

```csharp
public ItemRepository(ReaderDatabase db);

public Task<long> UpsertAsync(FeedItem item, CancellationToken ct = default);      // -1 if blocked by a tombstone
public Task<int> UpsertManyAsync(IReadOnlyList<FeedItem> items, CancellationToken ct = default);  // count of NEW rows
public Task<FeedItem?> GetAsync(long id, CancellationToken ct = default);
public Task<IReadOnlyList<FeedItem>> QueryAsync(ItemQuery query, CancellationToken ct = default);
public Task<IReadOnlyList<FeedItem>> GetPendingOfflineAsync(int limit, CancellationToken ct = default);
public Task SetReadAsync(long id, bool isRead, CancellationToken ct = default);
public Task SetStarredAsync(long id, bool isStarred, CancellationToken ct = default);
public Task MarkFeedReadAsync(long feedId, CancellationToken ct = default);
public Task SetContentAsync(long id, string markdown, ContentSource source, CancellationToken ct = default);
public Task SetOfflineFailedAsync(long id, string error, CancellationToken ct = default);
public Task<int> GetUnreadCountAsync(long feedId, CancellationToken ct = default);
```

- `UpsertManyAsync` throws `ArgumentException` if the batch spans more than one feed.
- Upsert never touches read, starred, content or offline state on conflict.
- `SetContentAsync` also sets offline state to Downloaded and clears the error.
- A query with `Limit <= 0` uses an internal default of 200 rather than returning nothing.

### FolderRepository, SearchRepository, ItemQuery

```csharp
public sealed class FolderRepository(ReaderDatabase db)
{
    public Task<long> AddAsync(string name, long? parentId = null, CancellationToken ct = default);
    public Task<IReadOnlyList<Folder>> GetAllAsync(CancellationToken ct = default);
    public Task RenameAsync(long id, string name, CancellationToken ct = default);
    public Task DeleteAsync(long id, CancellationToken ct = default);   // feeds are reparented, never deleted
}

public sealed class SearchRepository(ReaderDatabase db)
{
    public Task<IReadOnlyList<FeedItem>> SearchAsync(string query, int limit, CancellationToken ct = default);
}

public enum ItemFilter { All = 0, Unread = 1, Starred = 2 }
public readonly record struct ItemQuery(long? FeedId, long? FolderId, ItemFilter Filter, int Limit, int Offset);
```

`SearchAsync` sanitises input into quoted FTS5 phrases, so no user input can throw. Consequence: an explicit phrase query or a trailing `*` prefix wildcard is treated as literal text, not FTS5 syntax.

---

## LucidReader.Core.Feeds

```csharp
public static class FeedDateParser { public static DateTimeOffset? TryParse(string? value); }

public sealed class FeedFetcher(HttpClient http)
{
    public const string UserAgentString = "lucidREADER/1.0 (+https://www.mostlylucid.net)";
    public Task<FeedFetchResult> FetchAsync(string feedUrl, string? etag, string? lastModified, CancellationToken ct = default);
}

public abstract record FeedFetchResult
{
    public sealed record Fetched(string Content, string? ETag, string? LastModified) : FeedFetchResult;
    public sealed record NotModified : FeedFetchResult;
    public sealed record Failed(string Error, bool IsTransient, TimeSpan? RetryAfter = null) : FeedFetchResult;
}

public interface IFeedParser
{
    bool CanParse(string content);
    ParsedFeed Parse(string content, Uri sourceUri);   // throws FeedParseException
}

public sealed partial class FeedParser : IFeedParser { public FeedParser(); }
public sealed class FeedParseException(string message, Exception? inner = null) : Exception;

public sealed record ParsedFeed(string? Title, string? SiteUrl, IReadOnlyList<ParsedItem> Items, int SkippedItemCount);

public sealed record ParsedItem
{
    public string? Guid { get; init; }
    public string? Link { get; init; }
    public string? Title { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PublishedUtc { get; init; }
    public DateTimeOffset? UpdatedUtc { get; init; }
    public string? ContentHtml { get; init; }
    public string? Summary { get; init; }
}
```

Body reads are capped at 8 MiB by a streamed bound, not just `Content-Length`. Charset precedence: HTTP header, then the XML declaration, then a BOM, then UTF-8. `RetryAfter` is populated from a 429 but nothing consumes it yet.

**Security: `ParsedItem.Link` may carry ANY scheme, including `javascript:`.** Core never navigates. Any UI that opens or follows a link must allowlist `http` and `https` first.

---

## LucidReader.Core.Sync

```csharp
public sealed class BackoffPolicy(Random? random = null)
{
    public const int AutoPauseThreshold = 20;
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(6);
    public DateTimeOffset NextDueAfterSuccess(DateTimeOffset nowUtc, EffectiveFeedSettings settings);
    public DateTimeOffset NextDueAfterFailure(DateTimeOffset nowUtc, int consecutiveFailures, EffectiveFeedSettings settings);
    public static bool ShouldAutoPause(int consecutiveFailures);
}

public readonly record struct FeedRefreshRequest(long FeedId, bool IsManual);
public readonly record struct FeedRefreshOutcome(long FeedId, bool Success, int NewItemCount, bool NotModified, string? Error);

public sealed class FeedRefreshService : IAsyncDisposable
{
    public static readonly TimeSpan MaxFeedFetchDuration = TimeSpan.FromSeconds(60);

    public FeedRefreshService(
        FeedRepository feeds, ItemRepository items, FeedFetcher fetcher, IFeedParser parser,
        BackoffPolicy backoff, Func<ReaderSettings> settings, TimeProvider timeProvider,
        int maxConcurrency = 4, TimeSpan? maxFetchDuration = null);

    public int PendingCount { get; }
    public int ActiveCount { get; }
    public int TotalFailed { get; }
    public event Action<FeedRefreshOutcome>? Completed;

    public bool TryQueue(long feedId, bool isManual = false);   // false if already in flight
    public Task QueueAsync(long feedId, bool isManual = false, CancellationToken ct = default);
    public Task<FeedRefreshOutcome> RefreshNowAsync(long feedId, CancellationToken ct = default);  // bypasses coalescing
    public void Pause();
    public void Resume();
    public ValueTask DisposeAsync();
}

public sealed class RefreshScheduler : IAsyncDisposable
{
    public RefreshScheduler(FeedRepository feeds, FeedRefreshService refresh, TimeProvider timeProvider, TimeSpan? tickInterval = null);
    public bool IsRunning { get; }
    public string? LastTickError { get; }
    public int ConsecutiveTickFailures { get; }
    public void Start();                 // throws ObjectDisposedException after disposal
    public Task StopAsync();             // restart-safe
    public Task<int> TickAsync(CancellationToken ct = default);
    public ValueTask DisposeAsync();
}
```

- `Completed` fires exactly once per attempt including timeouts and unexpected exceptions, so a subscriber never hangs. It does NOT fire on genuine app shutdown.
- **`IsRunning` says nothing about health.** A scheduler whose every tick throws still reports running. Read `LastTickError` and `ConsecutiveTickFailures` alongside it.
- Both `FeedRefreshService` and `OfflineDownloader` enforce their own timeouts with a linked `CancellationTokenSource`, because Ephemeral 3.0.0's `maxBodyDuration` does not cancel the body, it orphans it.
- `TickAsync` queues at most 200 due feeds per tick.

---

## LucidReader.Core.Offline

```csharp
public sealed class ArticleFetcher(HttpClient http)
{
    public Task<string?> FetchHtmlAsync(string url, CancellationToken ct = default);   // null on any failure
}

public static partial class StubDetector
{
    public const int FullArticleThreshold = 1500;
    public static bool IsStub(string? contentHtml);
}

public sealed class OfflineDownloader : IAsyncDisposable
{
    public static readonly TimeSpan MaxArticleFetchDuration = TimeSpan.FromSeconds(180);

    public OfflineDownloader(
        ItemRepository items, FeedRepository feeds, ArticleFetcher articles,
        IHtmlToMarkdownService converter, Func<ReaderSettings> settings, TimeProvider timeProvider,
        int maxConcurrency = 2, TimeSpan? maxFetchDuration = null);

    public int PendingCount { get; }
    public int ActiveCount { get; }
    public event Action<long>? Completed;    // item id

    public bool TryQueue(long itemId);
    public Task<int> QueuePendingAsync(int limit = 200, CancellationToken ct = default);
    public Task DownloadNowAsync(long itemId, CancellationToken ct = default);
    public ValueTask DisposeAsync();
}
```

`IHtmlToMarkdownService` is `MarkdownViewer.Services.IHtmlToMarkdownService`, from `Mostlylucid.LucidView.Content`:
`Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default)`.

Download decision: not a stub, or full-text disabled, or no link, means convert the feed summary as `ContentSource.Feed`. Otherwise fetch the page and convert as `ContentSource.Extracted`; a fetch failure records an offline failure and leaves the summary readable.

**Article images are NOT cached.** Spec 4.3 step 4 was deferred because `ImageCacheService` needs Avalonia.

---

## LucidReader.Core.Maintenance

```csharp
public sealed class RetentionService(
    ReaderDatabase db, FeedRepository feeds, Func<ReaderSettings> settings, TimeProvider timeProvider)
{
    public Task<int> PruneAsync(CancellationToken ct = default);          // rows deleted
    public Task<long> GetDatabaseSizeBytesAsync(CancellationToken ct = default);
}
```

Not disposable, holds no coordinator, **and has no timer**. Something must call `PruneAsync` periodically.

Per call: per-feed read-item prune honouring each feed's effective retention; an unread prune only when `KeepUnreadForever` is false; a per-feed count cap; then a tombstone prune at 400 days. Every delete writes an `item_tombstones` row in the same transaction, which is what stops a pruned item being resurrected by the next refresh. Runs `PRAGMA incremental_vacuum` when anything was deleted, so the reported size actually shrinks.

**Behaviour worth knowing:** `MaxArticlesPerFeed` (default 500) does not respect `KeepUnreadForever` (default true). Unread items past the cap are deleted and tombstoned, so they do not come back.

---

## Ordering

1. `SettingsStore.LoadAsync`
2. `ReaderDatabase.OpenAsync` (exactly once per path per process; it runs migrations internally)
3. Repositories, all taking the database
4. `FeedFetcher`, `ArticleFetcher`, `FeedParser`, `BackoffPolicy`
5. `FeedRefreshService`, taking repositories and a **live** `Func<ReaderSettings>`, not a snapshot
6. `RefreshScheduler`, taking the refresh service
7. `OfflineDownloader`, taking repositories plus an `IHtmlToMarkdownService`
8. `RetentionService`, taking the database directly plus the feed repository

Shutdown is the reverse and is a caller obligation that nothing enforces: stop the scheduler, then drain `FeedRefreshService` and `OfflineDownloader` whose in-flight work still writes, then dispose `ReaderDatabase`.
