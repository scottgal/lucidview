# lucidREADER App Implementation Plan (Plan 2 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the lucidREADER desktop application on top of the finished `LucidReader.Core` engine: a three-pane Avalonia window, item actions, search, global and per-feed settings, OPML import and export, and the composition root that wires the engine together and shuts it down cleanly.

**Architecture:** A second Avalonia app alongside lucidVIEW, following lucidVIEW's own conventions: manual construction with no DI container, code-behind windows with `DataContext = this` and `RelayCommand`, XAML `KeyBindings`, and dialogs shown with `ShowDialog(this)`. The reading pane reuses `LucidMarkdownView` from `Mostlylucid.LucidView.Markdown`. All feed behaviour comes from `LucidReader.Core`; this plan adds no feed logic beyond four small Core services the app needs (tags, autodiscovery, OPML, image caching).

**Tech Stack:** .NET 10, Avalonia 11.3.12, FluentAvaloniaUI, FluentIcons, xunit 2.9.3, Mostlylucid.Avalonia.UITesting (Debug only).

**Spec:** `docs/superpowers/specs/2026-08-28-lucidreader-design.md`

**Plan 1 (complete, merged):** `docs/superpowers/plans/2026-08-28-lucidreader-core.md`

**Core API reference:** every signature this plan calls is listed in `.superpowers/lucidreader-core-api.md`. Read it before writing calling code; it is authoritative over any signature quoted in a task, which may have drifted.

**Scope:** spec build-order items 6 to 10, plus the composition and wiring items Plan 1 deliberately deferred. Packaging, the macOS signed native SQLite work and the FULL StyloExtract binding are Plan 3.

## Global Constraints

- Target framework `net10.0`. Nullable enabled, implicit usings enabled.
- `AvaloniaUseCompiledBindingsByDefault` is **false** in this solution. Bindings are reflection-based; do not assume compiled bindings.
- lucidREADER publishes self-contained, single-file, ReadyToRun, **not** trimmed and **not** AOT. `IncludeNativeLibrariesForSelfExtract` must stay **false** (see spec 7.1; it breaks the macOS hardened runtime for the SQLite native library).
- `LucidReader.Core` must keep having NO Avalonia dependency. Anything needing Avalonia lives in the app project or in `Mostlylucid.LucidView.*`.
- No `DateTime.Now`; time comes from `TimeProvider`.
- All engine writes go through `LucidReader.Core`; the app never opens its own SQLite connection.
- **`ReaderDatabase.OpenAsync` must be called exactly once per process.** A second open of the same path throws `InvalidOperationException` by design.
- lucidVIEW's suites must stay exactly at: `MarkdownViewer.Tests` 76 passed / 2 skipped, `MarkdownViewer.Full.Tests` 12 passed. `LucidReader.Core.Tests` must stay at 222 passed or grow.
- No emdash characters in C# comments, XAML, or user-facing strings.
- **UI is verified through the UITesting harness, never by constructing windows in xunit.** This repo ships `Mostlylucid.Avalonia.UITesting` (Debug-wired via `UseUITesting` in `Program.cs`), which gives three modes:
  - `dotnet run --project LucidReader/LucidReader.csproj -- --ux-repl` for interactive development: `list`, `tree`, `get #Control.Prop`, `set`, `click`, `type`, `press`, `assert`, `describe` (screenshot plus ASCII art), `screenshot`, `waitfor`.
  - `dotnet run --project LucidReader/LucidReader.csproj -- --ux-mcp --output screenshots` to drive the running app over JSON-RPC, which is the fastest loop when building a pane.
  - `dotnet run --project LucidReader/LucidReader.csproj -- --ux-test --script <yaml> --output <dir>` for repeatable scripted runs.
  Constructing an Avalonia `Window` inside a plain xunit test does not work here and must not be attempted. Anything worth asserting about a window is asserted through the harness. Anything worth unit testing must be extracted into a plain class with no Avalonia base type, which is better design regardless.
- CI runs `LucidReader.Core.Tests` and `MarkdownViewer.Tests` in the `build` job and `MarkdownViewer.Full.Tests` in `build-full`. Any new test project must be added to CI, on a single line (the matrix includes windows-latest, where a trailing backslash is not a line continuation).

---

## Carried forward from Plan 1

These were consciously deferred and are now in scope. Each is assigned to a task below.

| Item | Why deferred | Task |
|---|---|---|
| Image caching for downloaded articles | `ImageCacheService` needs Avalonia; Core may not reference it | 5 |
| Wiring `MaxConcurrentFetches` / `MaxConcurrentDownloads` | Nothing composed the engine | 1 |
| `TagRepository` | Nothing headless read tags | 2 |
| OPML import and export | Pure file handling over existing repositories | 4, 13 |
| Feed autodiscovery from a site URL | Same | 3, 13 |
| Retention scheduling | `RetentionService` has no timer | 1 |
| `NewItemCount` to `QueuePendingAsync` hop | The only thing that makes offline download happen | 1 |
| Surfacing `LastTickError` / `ConsecutiveTickFailures` | No UI existed | 14 |
| Auto-pause prompt and resume | No UI existed | 14 |
| `SchemaMigrator.LastIncrementalVacuumConversionError` routing | No logging existed | 1 |
| **`javascript:` scheme allowlist** | Harmless in Core, which never navigates | 8 |

**Security note, task 8.** `FeedParser.ResolveLink` passes any scheme through untouched, including `javascript:`. Core never navigates so this is inert there. The reading pane DOES navigate, and a feed is attacker-controlled input. Task 8 must allowlist `http` and `https` before opening or following any link. This is not optional.

---

## Two decisions this plan makes

**PDF export.** Spec 5.4 says article export reuses lucidVIEW's `PdfExportService`. Plan 1 established that is not possible: it depends on `MarkdownService`, a large mermaid and rendering service that was never in scope to move. **Decision: lucidREADER v1 exports an article as markdown only.** PDF export is dropped from v1 rather than triggering an unplanned extraction. Revisit once the reader is usable. Task 10 implements markdown export; no task implements PDF.

**Per-feed settings depth.** Spec 6.2 lists four inheritable overrides. `EffectiveFeedSettings.Resolve` already computes all four and `RetentionService` honours per-feed retention. The per-feed dialog therefore edits exactly those four plus title override, folder, and enable/disable. Nothing else becomes per-feed in v1.

---

## File Structure

**New `LucidReader/`**: the Avalonia app. Follows lucidVIEW's layout.
- `Program.cs`: entry point, crash logging, `UseUITesting` under `#if DEBUG`.
- `App.axaml` / `App.axaml.cs`: application, theme application, main window creation.
- `ReaderServices.cs`: the composition root. Owns construction order, the settings holder, and shutdown order.
- `Views/MainWindow.axaml` / `.axaml.cs`: the three-pane shell and commands.
- `Views/MainWindow.Items.cs`: item list and mark-as-read dwell.
- `Views/MainWindow.Reading.cs`: reading pane.
- `Views/MainWindow.Actions.cs`: commands, keyboard navigation, markdown export.
- `Views/MainWindow.Search.cs`: search.
- `Views/MainWindow.Settings.cs`, `Views/MainWindow.FeedMenu.cs`, `Views/MainWindow.Subscriptions.cs`: dialogs and subscriptions.
- `Views/MainWindow.Health.cs`: refresh health and auto-paused feeds.
- `Views/SettingsDialog.axaml` / `.axaml.cs`: global settings.
- `Views/FeedSettingsDialog.axaml` / `.axaml.cs`: per-feed settings.
- `Views/AddFeedDialog.axaml` / `.axaml.cs`: add by URL, with autodiscovery.
- `Views/ConfirmDialog.axaml` / `.axaml.cs`: reusable confirmation for destructive actions.
- `Views/RelayCommand.cs`: copied from lucidVIEW's, which is not in a shared library.
- `Services/SafeLinkOpener.cs`: the only sanctioned way to open a URL from feed content.
- `Services/AvaloniaArticleImageCache.cs`: article image caching over `ImageCacheService`.
- `Models/FeedTreeNode.cs`, `Models/ItemRow.cs`: view models for the two list panes.

**Additions to `LucidReader.Core/`**: engine pieces the app needs, still Avalonia-free.
- `Storage/TagRepository.cs`
- `Feeds/FeedAutodiscovery.cs`
- `Opml/OpmlDocument.cs`, `Opml/OpmlReader.cs`, `Opml/OpmlWriter.cs`, `Opml/OpmlService.cs`

**New `Mostlylucid.LucidView.Content` addition**: image caching bridge, Avalonia-free.
- `IArticleImageCache.cs`: the interface Core's download path calls.

**New `LucidReader.Core.Tests/Ui/`**: UI tests via the Debug-only harness.
**New `ux-scripts/reader-*.yaml`**: UI driving scripts.

---

## Task 1: The composition root

**Files:**
- Create: `LucidReader/LucidReader.csproj`
- Create: `LucidReader/ReaderServices.cs`
- Create: `LucidReader/Program.cs`
- Create: `LucidReader/App.axaml`, `LucidReader/App.axaml.cs`
- Create: `LucidReader/Views/MainWindow.axaml`, `.axaml.cs` (placeholder window, filled in by Task 6)
- Test: `LucidReader.Core.Tests/Composition/ReaderServicesTests.cs`

**Interfaces:**
- Consumes: the whole of `LucidReader.Core` (see the API reference file).
- Produces:
  - `sealed class ReaderServices : IAsyncDisposable` with `static Task<ReaderServices> StartAsync(string? databasePath = null, string? settingsPath = null, TimeProvider? timeProvider = null, CancellationToken ct = default)`, and properties `ReaderDatabase Database`, `FolderRepository Folders`, `FeedRepository Feeds`, `ItemRepository Items`, `SearchRepository Search`, `TagRepository Tags`, `FeedRefreshService Refresh`, `RefreshScheduler Scheduler`, `OfflineDownloader Downloader`, `RetentionService Retention`, `ReaderSettings Settings { get; }`, `Task UpdateSettingsAsync(ReaderSettings settings, CancellationToken ct = default)`, `event Action<ReaderSettings>? SettingsChanged`, `Exception? StartupWarning { get; }`.

**Why this task exists and why it is first.** Plan 1 built nine components and wired none of them together. Nothing currently makes offline download happen, nothing prunes on a schedule, and nothing disposes in the right order. Every later task in this plan depends on this one.

- [ ] **Step 1: Create the app project**

Create `LucidReader/LucidReader.csproj`. Note what is deliberately different from lucidVIEW: `IncludeNativeLibrariesForSelfExtract` is **false**, because the SQLite native library must stay beside the executable for the macOS hardened runtime to load it (spec 7.1).

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>

    <AssemblyName>lucidREADER</AssemblyName>
    <RootNamespace>LucidReader</RootNamespace>
    <Version>0.1.0</Version>
    <Product>lucidREADER</Product>
    <Description>A native RSS and Atom reader built on the lucidVIEW rendering stack</Description>

    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <PublishReadyToRun>true</PublishReadyToRun>
    <PublishReadyToRunComposite>false</PublishReadyToRunComposite>
    <PublishTrimmed>false</PublishTrimmed>
    <!-- Must stay false. Self-extracting the SQLite native library into a temp
         directory makes the macOS hardened runtime refuse to load it, which
         kills the app at first database access on a notarised build. -->
    <IncludeNativeLibrariesForSelfExtract>false</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.12" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.12" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.12" />
    <PackageReference Include="FluentAvaloniaUI" Version="2.5.0" />
    <PackageReference Include="FluentIcons.Avalonia.Fluent" Version="2.0.321" />
    <PackageReference Include="Avalonia.Diagnostics" Version="11.3.12" Condition="'$(Configuration)' == 'Debug'" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\LucidReader.Core\LucidReader.Core.csproj" />
    <ProjectReference Include="..\Mostlylucid.LucidView.Markdown\Mostlylucid.LucidView.Markdown.csproj" />
    <ProjectReference Include="..\Mostlylucid.LucidView.Shell\Mostlylucid.LucidView.Shell.csproj" />
    <ProjectReference Include="..\Mostlylucid.LucidView.Content\Mostlylucid.LucidView.Content.csproj" />
  </ItemGroup>

  <ItemGroup Condition="'$(Configuration)' == 'Debug'">
    <ProjectReference Include="..\external\lucidRESUME\src\Mostlylucid.Avalonia.UITesting\Mostlylucid.Avalonia.UITesting.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing composition tests**

These live in `LucidReader.Core.Tests` rather than a UI test project, because `ReaderServices` is deliberately UI-free and testing it should not need a window. Add a project reference from `LucidReader.Core.Tests` to `LucidReader`.

Create `LucidReader.Core.Tests/Composition/ReaderServicesTests.cs`:

```csharp
using LucidReader;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Core.Tests.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Composition;

public class ReaderServicesTests
{
    private static (string db, string settings, string dir) TempPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lucidreader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (Path.Combine(dir, "reader.db"), Path.Combine(dir, "settings.json"), dir);
    }

    [Fact]
    public async Task Starting_builds_every_component()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            await using var services = await ReaderServices.StartAsync(db, settings);

            Assert.NotNull(services.Database);
            Assert.NotNull(services.Folders);
            Assert.NotNull(services.Feeds);
            Assert.NotNull(services.Items);
            Assert.NotNull(services.Search);
            Assert.NotNull(services.Tags);
            Assert.NotNull(services.Refresh);
            Assert.NotNull(services.Scheduler);
            Assert.NotNull(services.Downloader);
            Assert.NotNull(services.Retention);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Disposing_then_starting_again_on_the_same_path_succeeds()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            await (await ReaderServices.StartAsync(db, settings)).DisposeAsync();
            await using var second = await ReaderServices.StartAsync(db, settings);
            Assert.NotNull(second.Database);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Disposing_twice_does_not_throw()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            var services = await ReaderServices.StartAsync(db, settings);
            await services.DisposeAsync();
            await services.DisposeAsync();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Settings_round_trip_and_raise_the_change_event()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            await using var services = await ReaderServices.StartAsync(db, settings);
            ReaderSettings? seen = null;
            services.SettingsChanged += s => seen = s;

            await services.UpdateSettingsAsync(services.Settings with { FontSize = 21 });

            Assert.Equal(21, services.Settings.FontSize);
            Assert.NotNull(seen);
            Assert.Equal(21, seen!.FontSize);
            Assert.Equal(21, (await SettingsStore.LoadAsync(settings)).FontSize);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Refresh_and_download_concurrency_come_from_settings()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            await SettingsStore.SaveAsync(settings, ReaderSettings.Defaults with
            {
                MaxConcurrentFetches = 7,
                MaxConcurrentDownloads = 3
            });

            await using var services = await ReaderServices.StartAsync(db, settings);

            Assert.Equal(7, services.ConfiguredFetchConcurrency);
            Assert.Equal(3, services.ConfiguredDownloadConcurrency);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task A_completed_refresh_that_found_items_queues_them_for_download()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            await using var services = await ReaderServices.StartAsync(db, settings);
            var feedId = await services.Feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });

            var itemId = await services.Items.UpsertAsync(new FeedItem
            {
                FeedId = feedId,
                Guid = "g1",
                Title = "An item",
                FirstSeenUtc = DateTimeOffset.UtcNow,
                OfflineState = OfflineState.Pending
            });

            var queued = await services.QueuePendingDownloadsAsync();

            Assert.True(queued >= 1);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task A_vacuum_conversion_failure_is_surfaced_rather_than_hidden()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            await using var services = await ReaderServices.StartAsync(db, settings);

            // Normally null. The property exists so a failed conversion is
            // visible to the app rather than dying silently in the migrator.
            Assert.Equal(SchemaMigrator.LastIncrementalVacuumConversionError, services.StartupWarning);
        }
        finally { Directory.Delete(dir, true); }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter ReaderServicesTests 2>&1 | tail -10
```

Expected: compilation failure, `ReaderServices` does not exist.

- [ ] **Step 4: Write ReaderServices**

Create `LucidReader/ReaderServices.cs`. Read the ordering notes in the Core API reference before writing this; construction order is a real constraint, not a style preference.

```csharp
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
```

Two things to be careful with. `Current()` closes over `built`, which is null until construction finishes, so it falls back to the loaded settings during construction; that is deliberate and the fallback must stay. And `HttpClient.Timeout` is disabled on purpose because the two services enforce their own per-operation timeouts; leaving the default 100 seconds in place would produce a second, competing clock.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter ReaderServicesTests 2>&1 | tail -5
```

Expected: 7 passed.

`Refresh_and_download_concurrency_come_from_settings` will fail if you left the constructor defaults in place instead of passing the settings values. That is the point of the test: the settings existed in Plan 1 and were wired to nothing.

- [ ] **Step 6: Write the app bootstrap**

Create `LucidReader/Program.cs`, following lucidVIEW's `Program.cs` closely, including its crash-log handlers and the Debug-only UI testing harness:

```csharp
using Avalonia;
using Avalonia.Data.Core.Plugins;
#if DEBUG
using Mostlylucid.Avalonia.UITesting;
#endif

namespace LucidReader;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        BindingPlugins.DataValidators.Clear();

        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

#if DEBUG
        builder = builder.UseUITesting(opts =>
        {
            opts.DefaultScreenshotDir = "ux-screenshots";
            opts.Log = Console.WriteLine;
            opts.EnableCrossWindowTracking = true;
            opts.CaptureScreenshotsByDefault = false;
        });
#endif

        return builder.AfterSetup(_ => BindingPlugins.DataValidators.Clear());
    }
}
```

Create `LucidReader/App.axaml.cs`. The engine is started before the window exists, and disposed when the desktop lifetime exits:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LucidReader.Views;

namespace LucidReader;

public class App : Application
{
    public ReaderServices? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Services = ReaderServices.StartAsync().GetAwaiter().GetResult();
            desktop.MainWindow = new MainWindow(Services);
            desktop.ShutdownRequested += async (_, _) =>
            {
                if (Services is not null) await Services.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

Blocking on `StartAsync` here is deliberate: the window cannot be constructed without the engine, and Avalonia's initialisation callback is not async. It opens one SQLite file and reads one small JSON file.

Create `LucidReader/App.axaml` and a placeholder `Views/MainWindow.axaml` with a title and an empty grid, plus a constructor taking `ReaderServices`. Task 6 replaces the layout.

- [ ] **Step 7: Confirm the app builds and starts**

```bash
dotnet build LucidReader/LucidReader.csproj 2>&1 | tail -5
```

Expected: 0 errors.

- [ ] **Step 8: Add the new projects to CI**

In `.github/workflows/ci.yml`, add a build step for `LucidReader/LucidReader.csproj` to the `build` job. Keep it on a single line: the matrix includes windows-latest, where the default shell is pwsh and a trailing backslash is not a line continuation.

- [ ] **Step 9: Commit**

```bash
git add LucidReader LucidReader.Core.Tests .github/workflows/ci.yml
git commit -m "feat(reader): composition root wiring the engine together"
```

---

## Task 2: TagRepository

**Files:**
- Create: `LucidReader.Core/Storage/TagRepository.cs`
- Test: `LucidReader.Core.Tests/Storage/TagRepositoryTests.cs`

**Interfaces:**
- Consumes: `ReaderDatabase`.
- Produces: `sealed class TagRepository(ReaderDatabase db)` with `Task<long> GetOrCreateAsync(string name, CancellationToken ct = default)`, `Task<IReadOnlyList<string>> GetAllAsync(CancellationToken ct = default)`, `Task<IReadOnlyList<string>> GetForItemAsync(long itemId, CancellationToken ct = default)`, `Task AddToItemAsync(long itemId, string tagName, CancellationToken ct = default)`, `Task RemoveFromItemAsync(long itemId, string tagName, CancellationToken ct = default)`, `Task<IReadOnlyList<FeedItem>> GetItemsWithTagAsync(string tagName, int limit, CancellationToken ct = default)`, `Task<int> DeleteUnusedAsync(CancellationToken ct = default)`.

The `tags` and `item_tags` tables were created by migration V1 in Plan 1 and have had no repository until now.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Storage/TagRepositoryTests.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class TagRepositoryTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private TagRepository _tags = null!;
    private ItemRepository _items = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _tags = new TagRepository(_db);
        _items = new ItemRepository(_db);
        _feedId = await new FeedRepository(_db).AddAsync(
            new Feed { FeedUrl = "https://example.com/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private Task<long> AddItemAsync(string guid) => _items.UpsertAsync(new FeedItem
    {
        FeedId = _feedId,
        Guid = guid,
        Title = guid,
        FirstSeenUtc = DateTimeOffset.Parse("2026-08-29T10:00:00Z")
    });

    [Fact]
    public async Task Creating_the_same_tag_twice_returns_the_same_id()
    {
        var first = await _tags.GetOrCreateAsync("dotnet");
        var second = await _tags.GetOrCreateAsync("dotnet");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Tag_names_are_matched_case_insensitively()
    {
        var lower = await _tags.GetOrCreateAsync("dotnet");
        var upper = await _tags.GetOrCreateAsync("DotNet");

        Assert.Equal(lower, upper);
        Assert.Single(await _tags.GetAllAsync());
    }

    [Fact]
    public async Task A_tag_can_be_added_to_an_item_and_read_back()
    {
        var id = await AddItemAsync("g1");

        await _tags.AddToItemAsync(id, "reading");

        Assert.Equal(new[] { "reading" }, await _tags.GetForItemAsync(id));
    }

    [Fact]
    public async Task Adding_the_same_tag_to_an_item_twice_is_harmless()
    {
        var id = await AddItemAsync("g1");

        await _tags.AddToItemAsync(id, "reading");
        await _tags.AddToItemAsync(id, "reading");

        Assert.Single(await _tags.GetForItemAsync(id));
    }

    [Fact]
    public async Task Removing_a_tag_leaves_the_others()
    {
        var id = await AddItemAsync("g1");
        await _tags.AddToItemAsync(id, "a");
        await _tags.AddToItemAsync(id, "b");

        await _tags.RemoveFromItemAsync(id, "a");

        Assert.Equal(new[] { "b" }, await _tags.GetForItemAsync(id));
    }

    [Fact]
    public async Task Items_can_be_listed_by_tag()
    {
        var one = await AddItemAsync("g1");
        var two = await AddItemAsync("g2");
        await AddItemAsync("g3");
        await _tags.AddToItemAsync(one, "keep");
        await _tags.AddToItemAsync(two, "keep");

        var tagged = await _tags.GetItemsWithTagAsync("keep", 50);

        Assert.Equal(2, tagged.Count);
    }

    [Fact]
    public async Task Deleting_an_item_removes_its_tag_links()
    {
        var id = await AddItemAsync("g1");
        await _tags.AddToItemAsync(id, "keep");

        await new FeedRepository(_db).DeleteAsync(_feedId);

        Assert.Empty(await _tags.GetItemsWithTagAsync("keep", 50));
    }

    [Fact]
    public async Task Unused_tags_can_be_cleaned_up_but_used_ones_survive()
    {
        var id = await AddItemAsync("g1");
        await _tags.AddToItemAsync(id, "used");
        await _tags.GetOrCreateAsync("orphan");

        var removed = await _tags.DeleteUnusedAsync();

        Assert.Equal(1, removed);
        Assert.Equal(new[] { "used" }, await _tags.GetAllAsync());
    }

    [Fact]
    public async Task A_blank_tag_name_is_rejected()
    {
        var id = await AddItemAsync("g1");

        await Assert.ThrowsAsync<ArgumentException>(() => _tags.AddToItemAsync(id, "   "));
    }
}
```

The case-insensitivity test matters: without it, "DotNet" and "dotnet" become two tags and the user's tag list quietly fills with near-duplicates. Note migration V1 created `ix_tags_name` as a plain unique index, so case-insensitive matching has to be done in the query rather than relying on the index collation.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter TagRepositoryTests 2>&1 | tail -10
```

Expected: compilation failure, `TagRepository` does not exist.

- [ ] **Step 3: Write TagRepository**

Create `LucidReader.Core/Storage/TagRepository.cs`:

```csharp
using LucidReader.Core.Model;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

/// <summary>
/// User tags on items. The tags and item_tags tables were created in the very
/// first migration but had no repository until the UI needed one.
///
/// Tag names are matched case-insensitively, so "DotNet" and "dotnet" are the
/// same tag. The unique index on tags.name is case-sensitive, so the matching
/// is done with NOCASE in the queries rather than by the index.
/// </summary>
public sealed class TagRepository(ReaderDatabase db)
{
    public async Task<long> GetOrCreateAsync(string name, CancellationToken ct = default)
    {
        var trimmed = Normalise(name);

        var existing = await db.QueryAsync<long?>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM tags WHERE name = $name COLLATE NOCASE LIMIT 1;";
            command.Parameters.AddWithValue("$name", trimmed);
            var result = await command.ExecuteScalarAsync(ct);
            return result is null or DBNull ? null : Convert.ToInt64(result);
        }, ct);

        if (existing is { } id) return id;

        return await db.WriteReturningIdAsync(
            "INSERT INTO tags (name) VALUES ($name);",
            new Dictionary<string, object?> { ["$name"] = trimmed },
            ct);
    }

    public Task<IReadOnlyList<string>> GetAllAsync(CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<string>>(async connection =>
        {
            var names = new List<string>();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM tags ORDER BY name COLLATE NOCASE;";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
            return names;
        }, ct);

    public Task<IReadOnlyList<string>> GetForItemAsync(long itemId, CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<string>>(async connection =>
        {
            var names = new List<string>();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT t.name FROM tags t
                JOIN item_tags it ON it.tag_id = t.id
                WHERE it.item_id = $itemId
                ORDER BY t.name COLLATE NOCASE;
                """;
            command.Parameters.AddWithValue("$itemId", itemId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
            return names;
        }, ct);

    public async Task AddToItemAsync(long itemId, string tagName, CancellationToken ct = default)
    {
        var tagId = await GetOrCreateAsync(tagName, ct);

        // The composite primary key makes a repeat add a no-op rather than an error.
        await db.WriteAsync(
            "INSERT OR IGNORE INTO item_tags (item_id, tag_id) VALUES ($itemId, $tagId);",
            new Dictionary<string, object?> { ["$itemId"] = itemId, ["$tagId"] = tagId },
            ct);
    }

    public Task RemoveFromItemAsync(long itemId, string tagName, CancellationToken ct = default) =>
        db.WriteAsync(
            """
            DELETE FROM item_tags
            WHERE item_id = $itemId
              AND tag_id IN (SELECT id FROM tags WHERE name = $name COLLATE NOCASE);
            """,
            new Dictionary<string, object?> { ["$itemId"] = itemId, ["$name"] = Normalise(tagName) },
            ct);

    public Task<IReadOnlyList<FeedItem>> GetItemsWithTagAsync(
        string tagName, int limit, CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<FeedItem>>(async connection =>
        {
            var results = new List<FeedItem>();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT i.* FROM items i
                JOIN item_tags it ON it.item_id = i.id
                JOIN tags t ON t.id = it.tag_id
                WHERE t.name = $name COLLATE NOCASE
                ORDER BY COALESCE(i.published_utc, i.first_seen_utc) DESC, i.id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$name", Normalise(tagName));
            command.Parameters.AddWithValue("$limit", limit);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(RowMappers.ReadItem((SqliteDataReader)reader));
            return results;
        }, ct);

    /// <summary>
    /// Removes tags no item references. Deleting an item cascades its item_tags
    /// rows away but leaves the tag itself behind, so without this the tag list
    /// only ever grows.
    /// </summary>
    public Task<int> DeleteUnusedAsync(CancellationToken ct = default) =>
        db.WriteAsync(
            "DELETE FROM tags WHERE id NOT IN (SELECT DISTINCT tag_id FROM item_tags);",
            new Dictionary<string, object?>(),
            ct);

    private static string Normalise(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("A tag name cannot be blank.", nameof(name));
        return trimmed;
    }
}
```

`RowMappers.ReadItem` is `internal`, and this class is inside `LucidReader.Core`, so it is reachable. Do not duplicate the mapper.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter TagRepositoryTests 2>&1 | tail -5
```

Expected: 9 passed.

- [ ] **Step 5: Commit**

```bash
git add LucidReader.Core/Storage/TagRepository.cs LucidReader.Core.Tests/Storage/TagRepositoryTests.cs
git commit -m "feat(reader): tag repository"
```

---

## Task 3: Feed autodiscovery

**Files:**
- Create: `LucidReader.Core/Feeds/FeedAutodiscovery.cs`
- Test: `LucidReader.Core.Tests/Feeds/FeedAutodiscoveryTests.cs`
- Test fixtures: `LucidReader.Core.Tests/Fixtures/Html/*.html`

**Interfaces:**
- Consumes: `ArticleFetcher` for page fetching, `FeedFetcher` and `FeedParser` for validation.
- Produces:
  - `readonly record struct DiscoveredFeed(string FeedUrl, string? Title)`
  - `sealed class FeedAutodiscovery(HttpClient http)` with `Task<IReadOnlyList<DiscoveredFeed>> DiscoverAsync(string inputUrl, CancellationToken ct = default)`.

**Behaviour.** Given whatever the user pasted, return the feed URLs worth subscribing to. If the input is already a feed, return it. Otherwise fetch the page and read `<link rel="alternate">` tags whose type is an RSS or Atom media type, resolving relative hrefs. Return an empty list rather than throwing when nothing is found.

- [ ] **Step 1: Create the HTML fixtures**

```bash
mkdir -p LucidReader.Core.Tests/Fixtures/Html
```

`LucidReader.Core.Tests/Fixtures/Html/single-feed.html`:

```html
<!doctype html>
<html><head>
  <title>Example Blog</title>
  <link rel="alternate" type="application/rss+xml" title="Example Blog RSS" href="/feed.xml">
</head><body><h1>Hello</h1></body></html>
```

`LucidReader.Core.Tests/Fixtures/Html/two-feeds.html`:

```html
<!doctype html>
<html><head>
  <title>Example Blog</title>
  <link rel="alternate" type="application/rss+xml" title="All posts" href="https://example.com/feed.xml">
  <link rel="alternate" type="application/atom+xml" title="Comments" href="https://example.com/comments.atom">
  <link rel="stylesheet" href="/style.css">
  <link rel="alternate" type="application/json" title="JSON Feed" href="/feed.json">
</head><body></body></html>
```

`LucidReader.Core.Tests/Fixtures/Html/no-feed.html`:

```html
<!doctype html>
<html><head><title>Nothing here</title></head><body><p>No feeds.</p></body></html>
```

Register all three in `FixtureCorpusTests` if that test asserts on the HTML directory as well as the feeds directory. Check before assuming; it currently asserts an exact set for `Fixtures/Feeds` only.

- [ ] **Step 2: Write the failing tests**

Create `LucidReader.Core.Tests/Feeds/FeedAutodiscoveryTests.cs`:

```csharp
using System.Net;
using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

public class FeedAutodiscoveryTests
{
    private static string Html(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", name));

    [Fact]
    public async Task A_url_that_is_already_a_feed_is_returned_as_is()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"), mediaType: "application/rss+xml");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/feed.xml");

        var one = Assert.Single(found);
        Assert.Equal("https://example.com/feed.xml", one.FeedUrl);
        Assert.Equal("Example Blog", one.Title);
    }

    [Fact]
    public async Task A_page_with_one_feed_link_yields_it_with_an_absolute_url()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("single-feed.html"), mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/blog");

        var one = Assert.Single(found);
        Assert.Equal("https://example.com/feed.xml", one.FeedUrl);
        Assert.Equal("Example Blog RSS", one.Title);
    }

    [Fact]
    public async Task A_page_with_two_feeds_yields_both_in_document_order()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("two-feeds.html"), mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/");

        Assert.Equal(2, found.Count);
        Assert.Equal("https://example.com/feed.xml", found[0].FeedUrl);
        Assert.Equal("https://example.com/comments.atom", found[1].FeedUrl);
    }

    [Fact]
    public async Task A_json_feed_link_is_ignored_because_the_parser_cannot_read_it()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("two-feeds.html"), mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        var found = await discovery.DiscoverAsync("https://example.com/");

        Assert.DoesNotContain(found, f => f.FeedUrl.EndsWith("feed.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_page_with_no_feed_returns_empty_rather_than_throwing()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Html("no-feed.html"), mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        Assert.Empty(await discovery.DiscoverAsync("https://example.com/"));
    }

    [Fact]
    public async Task An_unreachable_url_returns_empty_rather_than_throwing()
    {
        var handler = StubHttpHandler.Throwing(new HttpRequestException("no route to host"));
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        Assert.Empty(await discovery.DiscoverAsync("https://nope.invalid/"));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    public async Task A_url_that_is_not_http_is_refused(string input)
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html></html>", mediaType: "text/html");
        var discovery = new FeedAutodiscovery(handler.CreateClient());

        Assert.Empty(await discovery.DiscoverAsync(input));
        Assert.Empty(handler.Requests);
    }
}
```

The last test is a security check, not a validation nicety. This method takes a string a user pasted and turns it into a network request, so the scheme allowlist belongs here as well as in the reading pane.

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FeedAutodiscoveryTests 2>&1 | tail -10
```

Expected: failures. `StubHttpHandler.Returning` may not yet take a `mediaType` argument; it gained one during Plan 1's Task 17, so check its current signature and add the parameter if it is missing.

- [ ] **Step 4: Write FeedAutodiscovery**

Create `LucidReader.Core/Feeds/FeedAutodiscovery.cs`:

```csharp
using System.Text.RegularExpressions;

namespace LucidReader.Core.Feeds;

public readonly record struct DiscoveredFeed(string FeedUrl, string? Title);

/// <summary>
/// Turns whatever the user pasted into feed URLs worth subscribing to.
///
/// Uses a regex over the head rather than a full HTML parser on purpose: the
/// only thing being read is link elements, and pulling AngleSharp into this
/// path to do it would be a heavier dependency than the job deserves. A missed
/// link costs the user one manual paste of the feed URL.
/// </summary>
public sealed partial class FeedAutodiscovery(HttpClient http)
{
    private static readonly string[] FeedMediaTypes =
    [
        "application/rss+xml",
        "application/atom+xml",
        "application/rdf+xml",
        "text/xml",
        "application/xml"
    ];

    public async Task<IReadOnlyList<DiscoveredFeed>> DiscoverAsync(
        string inputUrl,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(inputUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Array.Empty<DiscoveredFeed>();

        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", FeedFetcher.UserAgentString);
            request.Headers.TryAddWithoutValidation(
                "Accept",
                "application/atom+xml, application/rss+xml, text/html;q=0.9, */*;q=0.8");

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return Array.Empty<DiscoveredFeed>();

            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Array.Empty<DiscoveredFeed>();
        }

        // Already a feed? Then there is nothing to discover.
        var parser = new FeedParser();
        if (parser.CanParse(body))
        {
            string? title = null;
            try { title = parser.Parse(body, uri).Title; }
            catch (FeedParseException) { }
            return [new DiscoveredFeed(uri.ToString(), title)];
        }

        var found = new List<DiscoveredFeed>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in LinkTagPattern().Matches(body))
        {
            var tag = match.Value;

            if (!AttributeValue(tag, "rel").Contains("alternate", StringComparison.OrdinalIgnoreCase))
                continue;

            var type = AttributeValue(tag, "type");
            if (!FeedMediaTypes.Any(t => type.Contains(t, StringComparison.OrdinalIgnoreCase)))
                continue;

            var href = AttributeValue(tag, "href");
            if (href.Length == 0) continue;
            if (!Uri.TryCreate(uri, href, out var absolute)) continue;
            if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps) continue;
            if (!seen.Add(absolute.ToString())) continue;

            var linkTitle = AttributeValue(tag, "title");
            found.Add(new DiscoveredFeed(
                absolute.ToString(),
                linkTitle.Length > 0 ? linkTitle : null));
        }

        return found;
    }

    private static string AttributeValue(string tag, string attribute)
    {
        var match = Regex.Match(
            tag,
            attribute + @"\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["v"].Value.Trim() : string.Empty;
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTagPattern();
}
```

Note `application/json` is deliberately absent from `FeedMediaTypes`: `FeedParser` cannot read JSON Feed, so offering one would produce a subscription that fails on every refresh.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FeedAutodiscoveryTests 2>&1 | tail -5
```

Expected: 9 passed (three of them from the `[Theory]`).

- [ ] **Step 6: Commit**

```bash
git add LucidReader.Core/Feeds LucidReader.Core.Tests/Feeds LucidReader.Core.Tests/Fixtures/Html
git commit -m "feat(reader): feed autodiscovery from a site URL"
```

---

## Task 4: OPML import and export

**Files:**
- Create: `LucidReader.Core/Opml/OpmlOutline.cs`, `LucidReader.Core/Opml/OpmlReader.cs`, `LucidReader.Core/Opml/OpmlWriter.cs`, `LucidReader.Core/Opml/OpmlService.cs`
- Test: `LucidReader.Core.Tests/Opml/OpmlReaderTests.cs`, `LucidReader.Core.Tests/Opml/OpmlServiceTests.cs`
- Fixtures: `LucidReader.Core.Tests/Fixtures/Opml/*.opml`

**Interfaces:**
- Consumes: `FolderRepository`, `FeedRepository`.
- Produces:
  - `sealed record OpmlOutline(string Title, string? FeedUrl, string? SiteUrl, IReadOnlyList<OpmlOutline> Children)`
  - `static class OpmlReader` with `IReadOnlyList<OpmlOutline> Parse(string opml)`, throwing `OpmlParseException`.
  - `static class OpmlWriter` with `string Write(IReadOnlyList<OpmlOutline> outlines, string title, DateTimeOffset nowUtc)`.
  - `sealed class OpmlParseException(string message, Exception? inner = null) : Exception`
  - `readonly record struct OpmlImportResult(int FoldersCreated, int FeedsAdded, int FeedsSkipped)`
  - `sealed class OpmlService(FolderRepository folders, FeedRepository feeds)` with `Task<OpmlImportResult> ImportAsync(string opml, CancellationToken ct = default)` and `Task<string> ExportAsync(DateTimeOffset nowUtc, CancellationToken ct = default)`.

OPML is how a user brings years of subscriptions across from another reader, so import must be forgiving about the many shapes real exporters produce and must never lose feeds to a partial failure.

- [ ] **Step 1: Create the fixtures**

```bash
mkdir -p LucidReader.Core.Tests/Fixtures/Opml
```

`LucidReader.Core.Tests/Fixtures/Opml/flat.opml`: no folders, the simplest export.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<opml version="2.0">
  <head><title>Subscriptions</title></head>
  <body>
    <outline text="Example Blog" type="rss" xmlUrl="https://example.com/feed.xml" htmlUrl="https://example.com/"/>
    <outline text="Another Blog" type="rss" xmlUrl="https://another.example/atom.xml"/>
  </body>
</opml>
```

`LucidReader.Core.Tests/Fixtures/Opml/foldered.opml`: folders as nested outlines with no `xmlUrl`, which is the convention.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<opml version="2.0">
  <head><title>Subscriptions</title></head>
  <body>
    <outline text="News">
      <outline text="World" type="rss" xmlUrl="https://news.example/world.xml"/>
      <outline text="Tech" type="rss" xmlUrl="https://news.example/tech.xml"/>
    </outline>
    <outline text="Personal">
      <outline text="A friend" type="rss" xmlUrl="https://friend.example/feed.xml"/>
    </outline>
    <outline text="Loose feed" type="rss" xmlUrl="https://loose.example/feed.xml"/>
  </body>
</opml>
```

`LucidReader.Core.Tests/Fixtures/Opml/awkward.opml`: the shapes real exporters emit: `title` instead of `text`, a missing `type`, deep nesting beyond one level, an entry with no `xmlUrl` that is not a folder either, and a duplicate feed.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<opml version="1.0">
  <head><title>Exported</title></head>
  <body>
    <outline title="Titled not texted" xmlUrl="https://a.example/feed.xml"/>
    <outline text="Outer">
      <outline text="Inner">
        <outline text="Deep feed" xmlUrl="https://b.example/feed.xml"/>
      </outline>
    </outline>
    <outline text="Empty container"/>
    <outline text="Duplicate" type="rss" xmlUrl="https://a.example/feed.xml"/>
  </body>
</opml>
```

`LucidReader.Core.Tests/Fixtures/Opml/not-opml.xml`

```xml
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>Not an OPML file</title></channel></rss>
```

Add `<None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />` already covers this directory; confirm the files reach the test output before writing the tests.

- [ ] **Step 2: Write the failing reader tests**

Create `LucidReader.Core.Tests/Opml/OpmlReaderTests.cs`:

```csharp
using LucidReader.Core.Opml;
using Xunit;

namespace LucidReader.Core.Tests.Opml;

public class OpmlReaderTests
{
    private static string Opml(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Opml", name));

    [Fact]
    public void A_flat_export_yields_one_outline_per_feed()
    {
        var outlines = OpmlReader.Parse(Opml("flat.opml"));

        Assert.Equal(2, outlines.Count);
        Assert.Equal("Example Blog", outlines[0].Title);
        Assert.Equal("https://example.com/feed.xml", outlines[0].FeedUrl);
        Assert.Equal("https://example.com/", outlines[0].SiteUrl);
        Assert.Empty(outlines[0].Children);
    }

    [Fact]
    public void A_foldered_export_preserves_the_structure()
    {
        var outlines = OpmlReader.Parse(Opml("foldered.opml"));

        Assert.Equal(3, outlines.Count);

        var news = outlines[0];
        Assert.Equal("News", news.Title);
        Assert.Null(news.FeedUrl);
        Assert.Equal(2, news.Children.Count);

        var loose = outlines[2];
        Assert.Equal("Loose feed", loose.Title);
        Assert.NotNull(loose.FeedUrl);
    }

    [Fact]
    public void A_title_attribute_is_accepted_when_text_is_missing()
    {
        var outlines = OpmlReader.Parse(Opml("awkward.opml"));

        Assert.Equal("Titled not texted", outlines[0].Title);
    }

    [Fact]
    public void A_missing_type_attribute_does_not_disqualify_a_feed()
    {
        var outlines = OpmlReader.Parse(Opml("awkward.opml"));

        Assert.Equal("https://a.example/feed.xml", outlines[0].FeedUrl);
    }

    [Fact]
    public void Nesting_deeper_than_one_level_is_preserved_by_the_reader()
    {
        var outlines = OpmlReader.Parse(Opml("awkward.opml"));

        var outer = outlines.Single(o => o.Title == "Outer");
        var inner = Assert.Single(outer.Children);
        Assert.Equal("Inner", inner.Title);
        Assert.Equal("Deep feed", Assert.Single(inner.Children).Title);
    }

    [Fact]
    public void An_outline_with_neither_a_feed_nor_children_is_still_returned()
    {
        var outlines = OpmlReader.Parse(Opml("awkward.opml"));

        var empty = outlines.Single(o => o.Title == "Empty container");
        Assert.Null(empty.FeedUrl);
        Assert.Empty(empty.Children);
    }

    [Fact]
    public void A_document_that_is_not_opml_is_rejected()
    {
        Assert.Throws<OpmlParseException>(() => OpmlReader.Parse(Opml("not-opml.xml")));
    }

    [Fact]
    public void Malformed_xml_is_rejected_with_a_clear_exception()
    {
        Assert.Throws<OpmlParseException>(() => OpmlReader.Parse("<opml><body>"));
    }

    [Fact]
    public void Writing_then_reading_round_trips()
    {
        var original = OpmlReader.Parse(Opml("foldered.opml"));

        var written = OpmlWriter.Write(original, "Subscriptions",
            DateTimeOffset.Parse("2026-08-29T10:00:00Z"));
        var reread = OpmlReader.Parse(written);

        Assert.Equal(original.Count, reread.Count);
        Assert.Equal(original[0].Title, reread[0].Title);
        Assert.Equal(original[0].Children.Count, reread[0].Children.Count);
        Assert.Equal(original[2].FeedUrl, reread[2].FeedUrl);
    }

    [Fact]
    public void Written_opml_escapes_characters_that_would_break_the_document()
    {
        var outlines = new[]
        {
            new OpmlOutline("Ampersands & \"quotes\" & <angles>", "https://x.example/feed.xml?a=1&b=2", null, [])
        };

        var written = OpmlWriter.Write(outlines, "T", DateTimeOffset.Parse("2026-08-29T10:00:00Z"));

        var reread = OpmlReader.Parse(written);
        Assert.Equal("Ampersands & \"quotes\" & <angles>", reread[0].Title);
        Assert.Equal("https://x.example/feed.xml?a=1&b=2", reread[0].FeedUrl);
    }
}
```

The escaping test is the one that matters most in the writer: a feed URL with a query string containing an ampersand is completely normal, and emitting it unescaped produces an OPML file no other reader can open.

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter OpmlReaderTests 2>&1 | tail -10
```

Expected: compilation failure.

- [ ] **Step 4: Write the OPML types**

Create `LucidReader.Core/Opml/OpmlOutline.cs`:

```csharp
namespace LucidReader.Core.Opml;

/// <summary>
/// One outline node from an OPML document. An outline with a FeedUrl is a
/// subscription; one without is a folder. Real exporters produce both, and
/// occasionally something that is neither, which is preserved rather than
/// discarded so import can report honestly on what it saw.
/// </summary>
public sealed record OpmlOutline(
    string Title,
    string? FeedUrl,
    string? SiteUrl,
    IReadOnlyList<OpmlOutline> Children);

public sealed class OpmlParseException(string message, Exception? inner = null)
    : Exception(message, inner);
```

Create `LucidReader.Core/Opml/OpmlReader.cs`:

```csharp
using System.Xml.Linq;

namespace LucidReader.Core.Opml;

public static class OpmlReader
{
    public static IReadOnlyList<OpmlOutline> Parse(string opml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(opml);
        }
        catch (Exception ex)
        {
            throw new OpmlParseException("The file is not well-formed XML.", ex);
        }

        var root = document.Root
            ?? throw new OpmlParseException("The file has no root element.");

        if (!string.Equals(root.Name.LocalName, "opml", StringComparison.OrdinalIgnoreCase))
            throw new OpmlParseException(
                $"Expected an <opml> root element but found <{root.Name.LocalName}>.");

        var body = root.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, "body", StringComparison.OrdinalIgnoreCase))
            ?? throw new OpmlParseException("The OPML file has no <body> element.");

        return ReadOutlines(body);
    }

    private static IReadOnlyList<OpmlOutline> ReadOutlines(XElement parent)
    {
        var results = new List<OpmlOutline>();

        foreach (var element in parent.Elements().Where(e =>
                     string.Equals(e.Name.LocalName, "outline", StringComparison.OrdinalIgnoreCase)))
        {
            // Exporters disagree about which attribute holds the label.
            var title = Attribute(element, "text")
                        ?? Attribute(element, "title")
                        ?? Attribute(element, "xmlUrl")
                        ?? "Untitled";

            // The type attribute is often missing or wrong. The presence of an
            // xmlUrl is what actually decides whether this is a subscription.
            var feedUrl = Attribute(element, "xmlUrl");
            var siteUrl = Attribute(element, "htmlUrl");

            results.Add(new OpmlOutline(title, feedUrl, siteUrl, ReadOutlines(element)));
        }

        return results;
    }

    private static string? Attribute(XElement element, string name)
    {
        var attribute = element.Attributes().FirstOrDefault(a =>
            string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
        var value = attribute?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
```

Create `LucidReader.Core/Opml/OpmlWriter.cs`:

```csharp
using System.Xml.Linq;

namespace LucidReader.Core.Opml;

public static class OpmlWriter
{
    public static string Write(
        IReadOnlyList<OpmlOutline> outlines,
        string title,
        DateTimeOffset nowUtc)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("opml",
                new XAttribute("version", "2.0"),
                new XElement("head",
                    new XElement("title", title),
                    new XElement("dateCreated", nowUtc.ToUniversalTime().ToString("r"))),
                new XElement("body", outlines.Select(ToElement))));

        // XDocument handles escaping, which is why the writer builds a tree
        // rather than concatenating strings: a feed URL with a query string
        // containing an ampersand is normal and must not corrupt the document.
        return document.Declaration + Environment.NewLine + document;
    }

    private static XElement ToElement(OpmlOutline outline)
    {
        var element = new XElement("outline", new XAttribute("text", outline.Title));

        if (outline.FeedUrl is { } feedUrl)
        {
            element.Add(new XAttribute("type", "rss"));
            element.Add(new XAttribute("xmlUrl", feedUrl));
            if (outline.SiteUrl is { } siteUrl)
                element.Add(new XAttribute("htmlUrl", siteUrl));
        }

        foreach (var child in outline.Children)
            element.Add(ToElement(child));

        return element;
    }
}
```

- [ ] **Step 5: Write the failing service tests**

Create `LucidReader.Core.Tests/Opml/OpmlServiceTests.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Core.Opml;
using LucidReader.Core.Storage;
using LucidReader.Core.Tests.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Opml;

public class OpmlServiceTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private FolderRepository _folders = null!;
    private FeedRepository _feeds = null!;
    private OpmlService _service = null!;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _folders = new FolderRepository(_db);
        _feeds = new FeedRepository(_db);
        _service = new OpmlService(_folders, _feeds);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private static string Opml(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Opml", name));

    [Fact]
    public async Task Importing_a_flat_export_adds_every_feed_at_the_top_level()
    {
        var result = await _service.ImportAsync(Opml("flat.opml"));

        Assert.Equal(2, result.FeedsAdded);
        Assert.Equal(0, result.FoldersCreated);
        var feeds = await _feeds.GetAllAsync();
        Assert.Equal(2, feeds.Count);
        Assert.All(feeds, f => Assert.Null(f.FolderId));
    }

    [Fact]
    public async Task Importing_a_foldered_export_creates_the_folders()
    {
        var result = await _service.ImportAsync(Opml("foldered.opml"));

        Assert.Equal(2, result.FoldersCreated);
        Assert.Equal(4, result.FeedsAdded);

        var folders = await _folders.GetAllAsync();
        Assert.Contains(folders, f => f.Name == "News");
        Assert.Contains(folders, f => f.Name == "Personal");

        var feeds = await _feeds.GetAllAsync();
        var newsFolder = folders.Single(f => f.Name == "News");
        Assert.Equal(2, feeds.Count(f => f.FolderId == newsFolder.Id));
        Assert.Equal(1, feeds.Count(f => f.FolderId is null));
    }

    [Fact]
    public async Task A_feed_already_subscribed_is_skipped_rather_than_duplicated()
    {
        await _feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });

        var result = await _service.ImportAsync(Opml("flat.opml"));

        Assert.Equal(1, result.FeedsAdded);
        Assert.Equal(1, result.FeedsSkipped);
        Assert.Equal(2, (await _feeds.GetAllAsync()).Count);
    }

    [Fact]
    public async Task A_duplicate_within_the_same_file_is_only_added_once()
    {
        var result = await _service.ImportAsync(Opml("awkward.opml"));

        Assert.Equal(1, result.FeedsSkipped);
        Assert.Single((await _feeds.GetAllAsync()).Where(
            f => f.FeedUrl == "https://a.example/feed.xml"));
    }

    [Fact]
    public async Task Nesting_deeper_than_one_level_is_flattened_to_the_outermost_folder()
    {
        await _service.ImportAsync(Opml("awkward.opml"));

        var folders = await _folders.GetAllAsync();
        Assert.Contains(folders, f => f.Name == "Outer");
        Assert.DoesNotContain(folders, f => f.Name == "Inner");

        var outer = folders.Single(f => f.Name == "Outer");
        var deep = (await _feeds.GetAllAsync()).Single(f => f.FeedUrl == "https://b.example/feed.xml");
        Assert.Equal(outer.Id, deep.FolderId);
    }

    [Fact]
    public async Task Importing_the_same_folder_name_twice_reuses_the_folder()
    {
        await _service.ImportAsync(Opml("foldered.opml"));
        await _service.ImportAsync(Opml("foldered.opml"));

        Assert.Equal(2, (await _folders.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Exporting_then_importing_into_an_empty_database_reproduces_the_structure()
    {
        await _service.ImportAsync(Opml("foldered.opml"));
        var exported = await _service.ExportAsync(DateTimeOffset.Parse("2026-08-29T10:00:00Z"));

        using var second = new TempDatabase();
        await using var db2 = await ReaderDatabase.OpenAsync(second.Path);
        var service2 = new OpmlService(new FolderRepository(db2), new FeedRepository(db2));

        var result = await service2.ImportAsync(exported);

        Assert.Equal(4, result.FeedsAdded);
        Assert.Equal(2, result.FoldersCreated);
    }

    [Fact]
    public async Task Importing_a_file_that_is_not_opml_throws_before_writing_anything()
    {
        await Assert.ThrowsAsync<OpmlParseException>(
            () => _service.ImportAsync(Opml("not-opml.xml")));

        Assert.Empty(await _feeds.GetAllAsync());
        Assert.Empty(await _folders.GetAllAsync());
    }

    [Fact]
    public async Task An_outline_with_no_feed_and_no_children_creates_nothing()
    {
        await _service.ImportAsync(Opml("awkward.opml"));

        Assert.DoesNotContain(await _folders.GetAllAsync(), f => f.Name == "Empty container");
    }
}
```

Two decisions are pinned by these tests and worth understanding. Deeper-than-one-level nesting is flattened to the outermost folder, because the schema supports one level and silently dropping the deep feeds would be worse than putting them somewhere findable. And a parse failure writes nothing at all, so a bad file cannot leave a half-imported subscription list.

- [ ] **Step 6: Write OpmlService**

Create `LucidReader.Core/Opml/OpmlService.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Core.Storage;

namespace LucidReader.Core.Opml;

public readonly record struct OpmlImportResult(int FoldersCreated, int FeedsAdded, int FeedsSkipped);

public sealed class OpmlService(FolderRepository folders, FeedRepository feeds)
{
    /// <summary>
    /// Imports subscriptions. Parsing happens first and completely, so a file
    /// that turns out not to be OPML cannot leave a half-imported list behind.
    ///
    /// The schema supports one level of folders. Outlines nested deeper are
    /// flattened onto their outermost folder rather than dropped, because a
    /// feed the user can find in the wrong folder beats a feed that silently
    /// did not import.
    /// </summary>
    public async Task<OpmlImportResult> ImportAsync(string opml, CancellationToken ct = default)
    {
        var outlines = OpmlReader.Parse(opml);

        var existing = (await feeds.GetAllAsync(ct))
            .Select(f => f.FeedUrl)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingFolders = (await folders.GetAllAsync(ct))
            .ToDictionary(f => f.Name, f => f.Id, StringComparer.OrdinalIgnoreCase);

        var foldersCreated = 0;
        var added = 0;
        var skipped = 0;

        async Task ImportLevelAsync(IReadOnlyList<OpmlOutline> level, long? folderId, string? folderName)
        {
            foreach (var outline in level)
            {
                if (outline.FeedUrl is { } feedUrl)
                {
                    if (!existing.Add(feedUrl))
                    {
                        skipped++;
                        continue;
                    }

                    await feeds.AddAsync(new Feed
                    {
                        FeedUrl = feedUrl,
                        SiteUrl = outline.SiteUrl,
                        Title = outline.Title,
                        FolderId = folderId
                    }, ct);
                    added++;
                    continue;
                }

                if (outline.Children.Count == 0) continue;

                // Already inside a folder: keep the current one rather than
                // creating a second level the schema cannot represent.
                if (folderId is not null)
                {
                    await ImportLevelAsync(outline.Children, folderId, folderName);
                    continue;
                }

                if (!existingFolders.TryGetValue(outline.Title, out var id))
                {
                    id = await folders.AddAsync(outline.Title, null, ct);
                    existingFolders[outline.Title] = id;
                    foldersCreated++;
                }

                await ImportLevelAsync(outline.Children, id, outline.Title);
            }
        }

        await ImportLevelAsync(outlines, null, null);

        return new OpmlImportResult(foldersCreated, added, skipped);
    }

    public async Task<string> ExportAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var allFolders = await folders.GetAllAsync(ct);
        var allFeeds = await feeds.GetAllAsync(ct);

        static OpmlOutline ToOutline(Feed feed) =>
            new(feed.DisplayTitle, feed.FeedUrl, feed.SiteUrl, []);

        var outlines = new List<OpmlOutline>();

        foreach (var folder in allFolders)
        {
            var children = allFeeds
                .Where(f => f.FolderId == folder.Id)
                .Select(ToOutline)
                .ToList();

            outlines.Add(new OpmlOutline(folder.Name, null, null, children));
        }

        outlines.AddRange(allFeeds.Where(f => f.FolderId is null).Select(ToOutline));

        return OpmlWriter.Write(outlines, "lucidREADER subscriptions", nowUtc);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter "OpmlReaderTests|OpmlServiceTests" 2>&1 | tail -5
```

Expected: 19 passed.

- [ ] **Step 8: Commit**

```bash
git add LucidReader.Core/Opml LucidReader.Core.Tests/Opml LucidReader.Core.Tests/Fixtures/Opml
git commit -m "feat(reader): OPML import and export"
```

---

## Task 5: Article image caching

**Files:**
- Create: `Mostlylucid.LucidView.Content/IArticleImageCache.cs`
- Modify: `LucidReader.Core/Offline/OfflineDownloader.cs`
- Create: `LucidReader/Services/AvaloniaArticleImageCache.cs`
- Test: `LucidReader.Core.Tests/Offline/ArticleImageCacheTests.cs`

**Interfaces:**
- Produces:
  - `interface IArticleImageCache { Task<string> RewriteAsync(string markdown, Uri? baseUri, CancellationToken ct = default); }` in namespace `MarkdownViewer.Services`.
  - `OfflineDownloader` gains an optional constructor parameter `IArticleImageCache? imageCache = null`, appended last so existing call sites keep compiling.
  - `sealed class AvaloniaArticleImageCache(ImageCacheService cache, Func<ReaderSettings> settings) : IArticleImageCache` in the app project.

**Why the interface lives in Content.** Spec 4.3 step 4 wants downloaded articles to reference locally cached images so offline reading needs no network. `ImageCacheService` lives in `Mostlylucid.LucidView.Markdown`, which references Avalonia, and a hard constraint forbids Core depending on Avalonia. A one-method interface in Content, implemented in the app, satisfies both.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Offline/ArticleImageCacheTests.cs`:

```csharp
using System.Net;
using LucidReader.Core.Model;
using LucidReader.Core.Offline;
using LucidReader.Core.Storage;
using LucidReader.Core.Tests.Feeds;
using LucidReader.Core.Tests.Storage;
using MarkdownViewer.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Offline;

/// <summary>
/// Records what it was asked to rewrite and returns a marked-up result, so a
/// test can prove the downloader routed content through the cache without
/// depending on any real image fetching.
/// </summary>
internal sealed class RecordingImageCache : IArticleImageCache
{
    public List<string> Rewritten { get; } = [];
    public Uri? LastBaseUri { get; private set; }

    public Task<string> RewriteAsync(string markdown, Uri? baseUri, CancellationToken ct = default)
    {
        Rewritten.Add(markdown);
        LastBaseUri = baseUri;
        return Task.FromResult(markdown + "\n\n<!-- images cached -->");
    }
}

internal sealed class PassthroughConverter : IHtmlToMarkdownService
{
    public Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default) =>
        Task.FromResult("# Converted\n\n![pic](https://cdn.example/pic.png)");
}

public class ArticleImageCacheTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-08-29T12:00:00Z"));
    private readonly RecordingImageCache _cache = new();

    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private FeedRepository _feeds = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _feeds = new FeedRepository(_db);
        _feedId = await _feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private OfflineDownloader CreateDownloader(IArticleImageCache? cache)
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html>page</html>", mediaType: "text/html");
        return new OfflineDownloader(
            _items, _feeds, new ArticleFetcher(handler.CreateClient()),
            new PassthroughConverter(), () => ReaderSettings.Defaults, _time,
            imageCache: cache);
    }

    private Task<long> AddPendingAsync() => _items.UpsertAsync(new FeedItem
    {
        FeedId = _feedId,
        Guid = "g1",
        Title = "An article",
        Link = "https://example.com/article",
        Summary = "<p>" + new string('x', 2000) + "</p>",
        FirstSeenUtc = _time.GetUtcNow(),
        OfflineState = OfflineState.Pending
    });

    [Fact]
    public async Task Downloaded_content_is_routed_through_the_image_cache()
    {
        await using var downloader = CreateDownloader(_cache);
        var id = await AddPendingAsync();

        await downloader.DownloadNowAsync(id);

        Assert.Single(_cache.Rewritten);
        var stored = await _items.GetAsync(id);
        Assert.Contains("images cached", stored!.ContentMarkdown);
    }

    [Fact]
    public async Task The_item_link_is_passed_as_the_base_uri_for_relative_images()
    {
        await using var downloader = CreateDownloader(_cache);
        var id = await AddPendingAsync();

        await downloader.DownloadNowAsync(id);

        Assert.Equal(new Uri("https://example.com/article"), _cache.LastBaseUri);
    }

    [Fact]
    public async Task No_cache_supplied_stores_the_markdown_unchanged()
    {
        await using var downloader = CreateDownloader(null);
        var id = await AddPendingAsync();

        await downloader.DownloadNowAsync(id);

        var stored = await _items.GetAsync(id);
        Assert.DoesNotContain("images cached", stored!.ContentMarkdown);
        Assert.Equal(OfflineState.Downloaded, stored.OfflineState);
    }

    [Fact]
    public async Task A_failing_image_cache_still_stores_the_article()
    {
        var failing = new ThrowingImageCache();
        await using var downloader = CreateDownloader(failing);
        var id = await AddPendingAsync();

        await downloader.DownloadNowAsync(id);

        var stored = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Downloaded, stored!.OfflineState);
        Assert.Contains("Converted", stored.ContentMarkdown);
    }

    private sealed class ThrowingImageCache : IArticleImageCache
    {
        public Task<string> RewriteAsync(string markdown, Uri? baseUri, CancellationToken ct = default) =>
            throw new IOException("disk full");
    }
}
```

The last test is the important one. Image caching is an enhancement, not a precondition: if it fails, the reader should still have the article, with remote image URLs, rather than losing the download entirely.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter ArticleImageCacheTests 2>&1 | tail -10
```

Expected: compilation failure, `IArticleImageCache` does not exist and `OfflineDownloader` has no such parameter.

- [ ] **Step 3: Write the interface**

Create `Mostlylucid.LucidView.Content/IArticleImageCache.cs`. Namespace `MarkdownViewer.Services`, matching everything else in this library:

```csharp
namespace MarkdownViewer.Services;

/// <summary>
/// Downloads the images a converted article references and rewrites the
/// markdown to point at local copies, so an article read offline shows its
/// pictures.
///
/// This interface exists in Content rather than in the reader engine because
/// the implementation needs Avalonia, and the engine must not.
///
/// An implementation should be best effort: an image it cannot fetch should
/// be left as a remote URL, not turned into a broken link or an exception.
/// </summary>
public interface IArticleImageCache
{
    Task<string> RewriteAsync(string markdown, Uri? baseUri, CancellationToken ct = default);
}
```

- [ ] **Step 4: Wire it into OfflineDownloader**

Modify `LucidReader.Core/Offline/OfflineDownloader.cs`. Add the parameter LAST so existing call sites keep compiling, store it, and apply it in the one place markdown is produced. Both the extracted-page path and the feed-summary path go through the same `StoreAsync`/`SetContentAsync` seam; route both.

Add to the constructor signature, after `maxFetchDuration`:

```csharp
        TimeSpan? maxFetchDuration = null,
        IArticleImageCache? imageCache = null)
```

Store it in a field, then add this helper and call it immediately before every `SetContentAsync`:

```csharp
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
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter ArticleImageCacheTests 2>&1 | tail -5
```

Expected: 4 passed. Then run the whole suite once, because this touched the download path every offline test exercises:

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj 2>&1 | tail -3
```

Expected: no regressions.

- [ ] **Step 6: Write the Avalonia implementation**

Create `LucidReader/Services/AvaloniaArticleImageCache.cs`. Check `ImageCacheService`'s real signatures before writing this; from Plan 1's exploration it exposes `Task<string> CacheRemoteImageAsync(string url, CancellationToken ct = default)` and `string? GetCachedPath(string url)`, but confirm rather than assume.

```csharp
using System.Text.RegularExpressions;
using LucidReader.Core.Model;
using MarkdownViewer.Services;

namespace LucidReader.Services;

/// <summary>
/// Article image caching on top of lucidVIEW's ImageCacheService.
///
/// Rewrites markdown image references to local file paths so an article read
/// offline still shows its pictures. Any image that cannot be fetched keeps
/// its original remote URL, which renders fine when online and degrades to a
/// missing image when not, rather than breaking the article.
/// </summary>
public sealed partial class AvaloniaArticleImageCache(
    ImageCacheService cache,
    Func<ReaderSettings> settings) : IArticleImageCache
{
    public async Task<string> RewriteAsync(
        string markdown,
        Uri? baseUri,
        CancellationToken ct = default)
    {
        if (!settings().CacheImages) return markdown;

        var matches = MarkdownImagePattern().Matches(markdown);
        if (matches.Count == 0) return markdown;

        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in matches)
        {
            ct.ThrowIfCancellationRequested();

            var url = match.Groups["url"].Value.Trim();
            if (url.Length == 0 || replacements.ContainsKey(url)) continue;

            // Skip anything already local, and refuse any scheme other than
            // http and https: a feed is attacker-controlled input.
            if (!Uri.TryCreate(baseUri, url, out var absolute)) continue;
            if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps) continue;

            try
            {
                var local = await cache.CacheRemoteImageAsync(absolute.ToString(), ct);
                if (!string.IsNullOrEmpty(local)) replacements[url] = local;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Leave this image remote and carry on with the rest.
            }
        }

        if (replacements.Count == 0) return markdown;

        return MarkdownImagePattern().Replace(markdown, match =>
        {
            var url = match.Groups["url"].Value.Trim();
            return replacements.TryGetValue(url, out var local)
                ? $"![{match.Groups["alt"].Value}]({local})"
                : match.Value;
        });
    }

    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex MarkdownImagePattern();
}
```

Then pass it into `OfflineDownloader` from `ReaderServices.StartAsync`. That means `ReaderServices` gains an `ImageCacheService`, which is why the app project references `Mostlylucid.LucidView.Markdown`.

- [ ] **Step 7: Confirm the app still builds**

```bash
dotnet build LucidReader/LucidReader.csproj 2>&1 | tail -5
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add Mostlylucid.LucidView.Content LucidReader.Core/Offline LucidReader/Services LucidReader/ReaderServices.cs LucidReader.Core.Tests/Offline
git commit -m "feat(reader): cache article images for offline reading"
```

---

## Task 6: The three-pane shell

**Files:**
- Create: `LucidReader/Views/RelayCommand.cs`
- Rewrite: `LucidReader/Views/MainWindow.axaml`
- Rewrite: `LucidReader/Views/MainWindow.axaml.cs`
- Create: `LucidReader/Models/FeedTreeNode.cs`, `LucidReader/Models/ItemRow.cs`
- Test: `LucidReader.Core.Tests/LucidReader.Core.Tests.csproj`, `LucidReader.Core.Tests/Ui/ShellLayoutTests.cs`

**Interfaces:**
- Consumes: `ReaderServices` (Task 1).
- Produces: `MainWindow(ReaderServices services)`, with `DataContext = this` and named controls `FeedTree`, `ItemList`, `ReadingPane`, `SearchBox`, `StatusText`, `FilterAll`, `FilterUnread`, `FilterStarred`. Later tasks attach behaviour to these exact names; the UI test scripts target them by name, so do not rename them.

**Convention note.** lucidVIEW uses code-behind with `DataContext = this` and `RelayCommand` properties bound from XAML `KeyBindings`. There are no ViewModels and compiled bindings are off. Follow that. Introducing MVVM here would make the two apps inconsistent for no gain.

- [ ] **Step 1: Copy RelayCommand**

`RelayCommand` lives in `MarkdownViewer/Views/RelayCommand.cs` and is not in a shared library. Copy it to `LucidReader/Views/RelayCommand.cs`, changing only the namespace to `LucidReader.Views`. Do not extract it into a shared library as part of this task; that is a refactor of working lucidVIEW code with no benefit to this plan.

- [ ] **Step 2: Write the pane models**

Create `LucidReader/Models/FeedTreeNode.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LucidReader.Core.Model;

namespace LucidReader.Models;

/// <summary>
/// One row in the feed tree. Covers four shapes: the three smart rows at the
/// top, a folder, and a feed. Kept as one type rather than a hierarchy because
/// the tree binds to a single flat ObservableCollection.
/// </summary>
public sealed class FeedTreeNode : INotifyPropertyChanged
{
    private int _unreadCount;
    private bool _isExpanded = true;

    public required string Title { get; init; }
    public FeedTreeNodeKind Kind { get; init; }
    public long? FeedId { get; init; }
    public long? FolderId { get; init; }
    public ItemFilter SmartFilter { get; init; }

    /// <summary>Populated for feed rows only, so the tree can show a warning.</summary>
    public int ConsecutiveFailures { get; init; }
    public string? LastError { get; init; }
    public bool IsAutoPaused { get; init; }
    public bool IsEnabled { get; init; } = true;

    public int UnreadCount
    {
        get => _unreadCount;
        set { if (_unreadCount == value) return; _unreadCount = value; Raise(); Raise(nameof(HasUnread)); Raise(nameof(UnreadLabel)); }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; Raise(); }
    }

    public bool HasUnread => _unreadCount > 0;
    public string UnreadLabel => _unreadCount > 0 ? _unreadCount.ToString() : string.Empty;
    public bool HasProblem => ConsecutiveFailures > 0 || IsAutoPaused;

    /// <summary>Indent for feeds inside a folder. Folders and smart rows sit flush.</summary>
    public double Indent => Kind == FeedTreeNodeKind.Feed && FolderId is not null ? 16 : 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum FeedTreeNodeKind { Smart, Folder, Feed }
```

Create `LucidReader/Models/ItemRow.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LucidReader.Core.Model;

namespace LucidReader.Models;

/// <summary>
/// One row in the item list. Wraps a FeedItem so read and starred state can
/// change in place without requerying, which matters because the list is
/// virtualised and requerying on every keystroke of J would be visible.
/// </summary>
public sealed class ItemRow : INotifyPropertyChanged
{
    private bool _isRead;
    private bool _isStarred;

    public required FeedItem Item { get; init; }
    public required string FeedName { get; init; }

    public long Id => Item.Id;
    public string Title => string.IsNullOrWhiteSpace(Item.Title) ? "Untitled" : Item.Title!;

    public bool IsRead
    {
        get => _isRead;
        set { if (_isRead == value) return; _isRead = value; Raise(); Raise(nameof(TitleWeight)); }
    }

    public bool IsStarred
    {
        get => _isStarred;
        set { if (_isStarred == value) return; _isStarred = value; Raise(); Raise(nameof(StarGlyph)); }
    }

    public string TitleWeight => _isRead ? "Normal" : "SemiBold";
    public string StarGlyph => _isStarred ? "★" : "☆";

    /// <summary>
    /// Relative age, computed against a clock the caller supplies so tests are
    /// not at the mercy of the wall clock.
    /// </summary>
    public string RelativeDate { get; init; } = string.Empty;

    public static string FormatRelative(DateTimeOffset when, DateTimeOffset nowUtc)
    {
        var span = nowUtc - when;
        if (span < TimeSpan.Zero) return "just now";
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes}m";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours}h";
        if (span < TimeSpan.FromDays(7)) return $"{(int)span.TotalDays}d";
        return when.ToLocalTime().ToString("d MMM yyyy");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

- [ ] **Step 3: Write the failing shell tests**

Two kinds of coverage, and it matters which is which.

**Plain unit tests** for anything that is not a window: `ItemRow.FormatRelative` and `FeedTreeNode` are ordinary classes and belong in `LucidReader.Core.Tests` alongside the engine tests. No Avalonia session, no window.

**Harness verification** for the window itself. Do NOT construct `MainWindow` in a test. Drive the running app instead:

```bash
dotnet run --project LucidReader/LucidReader.csproj -- --ux-repl
```

then, in the REPL:

```
list                      # every named control, proves FeedTree/ItemList/ReadingPane/SearchBox/StatusText exist
tree                      # the visual tree, proves the three-pane layout is actually laid out
describe 01-empty-shell   # screenshot plus ASCII art, so you can SEE it
```

Record what `list` and `describe` showed in your report. A pane that exists in XAML but renders at zero width is exactly the failure a construction test would miss and `describe` catches.

Create the plain unit tests in `LucidReader.Core.Tests/Ui/ItemRowTests.cs`:



```csharp
using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class ItemRowTests
{
    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "30m")]
    [InlineData(120, "2h")]
    [InlineData(60 * 24 * 3, "3d")]
    public void Relative_dates_read_the_way_a_person_expects(int minutesAgo, string expected)
    {
        var now = DateTimeOffset.Parse("2026-08-29T12:00:00Z");

        Assert.Equal(expected, ItemRow.FormatRelative(now.AddMinutes(-minutesAgo), now));
    }

    [Fact]
    public void An_item_dated_in_the_future_does_not_render_a_negative_age()
    {
        var now = DateTimeOffset.Parse("2026-08-29T12:00:00Z");

        Assert.Equal("just now", ItemRow.FormatRelative(now.AddHours(3), now));
    }

    [Fact]
    public void An_old_item_falls_back_to_an_absolute_date()
    {
        var now = DateTimeOffset.Parse("2026-08-29T12:00:00Z");

        Assert.Contains("2026", ItemRow.FormatRelative(now.AddDays(-40), now));
    }

    [Fact]
    public void Marking_a_row_read_flips_its_weight()
    {
        var row = new ItemRow
        {
            Item = new LucidReader.Core.Model.FeedItem
            {
                FeedId = 1, Guid = "g", FirstSeenUtc = DateTimeOffset.UtcNow
            },
            FeedName = "Example"
        };

        Assert.Equal("SemiBold", row.TitleWeight);
        row.IsRead = true;
        Assert.Equal("Normal", row.TitleWeight);
    }
}
```

The future-dated test is not hypothetical: feeds routinely publish clock-skewed timestamps, and "-3h ago" in the list looks broken.

- [ ] **Step 4: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter ItemRowTests 2>&1 | tail -10
```

Expected: compilation failure, `ItemRow` does not exist.

- [ ] **Step 5: Write the window XAML**

Create `LucidReader/Views/MainWindow.axaml`. Three columns with splitters, a search box and filter chips above the item list, and a status bar. Named controls match the tests exactly.

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:md="clr-namespace:Mostlylucid.LucidView.Markdown;assembly=Mostlylucid.LucidView.Markdown"
        x:Class="LucidReader.Views.MainWindow"
        Title="lucidREADER"
        Width="1280" Height="820"
        MinWidth="820" MinHeight="520">

    <Window.KeyBindings>
        <KeyBinding Gesture="J" Command="{Binding NextItemCommand}" />
        <KeyBinding Gesture="K" Command="{Binding PreviousItemCommand}" />
        <KeyBinding Gesture="N" Command="{Binding NextUnreadCommand}" />
        <KeyBinding Gesture="P" Command="{Binding PreviousUnreadCommand}" />
        <KeyBinding Gesture="M" Command="{Binding ToggleReadCommand}" />
        <KeyBinding Gesture="S" Command="{Binding ToggleStarCommand}" />
        <KeyBinding Gesture="R" Command="{Binding RefreshCurrentFeedCommand}" />
        <KeyBinding Gesture="Shift+R" Command="{Binding RefreshAllCommand}" />
        <KeyBinding Gesture="O" Command="{Binding OpenOriginalCommand}" />
        <KeyBinding Gesture="OemQuestion" Command="{Binding FocusSearchCommand}" />
        <KeyBinding Gesture="Ctrl+F" Command="{Binding FindInArticleCommand}" />
        <KeyBinding Gesture="Ctrl+N" Command="{Binding AddFeedCommand}" />
        <KeyBinding Gesture="Ctrl+Oem Comma" Command="{Binding OpenSettingsCommand}" />
    </Window.KeyBindings>

    <Grid RowDefinitions="Auto,*,Auto">

        <Border Grid.Row="0" Padding="8,6" BorderThickness="0,0,0,1"
                BorderBrush="{DynamicResource SystemControlForegroundBaseLowBrush}">
            <Grid ColumnDefinitions="Auto,*,Auto">
                <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="4">
                    <Button x:Name="AddFeedButton" Content="Add feed"
                            Command="{Binding AddFeedCommand}" />
                    <Button x:Name="RefreshAllButton" Content="Refresh all"
                            Command="{Binding RefreshAllCommand}" />
                </StackPanel>

                <TextBox Grid.Column="1" x:Name="SearchBox" Margin="12,0"
                         Watermark="Search all articles"
                         Text="{Binding SearchText, Mode=TwoWay}" />

                <Button Grid.Column="2" x:Name="SettingsButton" Content="Settings"
                        Command="{Binding OpenSettingsCommand}" />
            </Grid>
        </Border>

        <Grid Grid.Row="1" ColumnDefinitions="260,4,340,4,*">

            <ListBox Grid.Column="0" x:Name="FeedTree"
                     ItemsSource="{Binding FeedNodes}"
                     SelectedItem="{Binding SelectedFeedNode, Mode=TwoWay}">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid ColumnDefinitions="Auto,*,Auto,Auto" Margin="{Binding Indent}">
                            <TextBlock Grid.Column="1" Text="{Binding Title}"
                                       FontWeight="{Binding TitleWeight, FallbackValue=Normal}"
                                       TextTrimming="CharacterEllipsis" />
                            <TextBlock Grid.Column="2" Text="!" Margin="4,0"
                                       IsVisible="{Binding HasProblem}"
                                       ToolTip.Tip="{Binding LastError}" />
                            <TextBlock Grid.Column="3" Text="{Binding UnreadLabel}"
                                       IsVisible="{Binding HasUnread}" Opacity="0.7" />
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <GridSplitter Grid.Column="1" ResizeDirection="Columns" />

            <Grid Grid.Column="2" RowDefinitions="Auto,*">
                <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="4" Margin="6">
                    <RadioButton x:Name="FilterAll" Content="All" GroupName="ItemFilter"
                                 IsChecked="{Binding IsFilterAll, Mode=TwoWay}" />
                    <RadioButton x:Name="FilterUnread" Content="Unread" GroupName="ItemFilter"
                                 IsChecked="{Binding IsFilterUnread, Mode=TwoWay}" />
                    <RadioButton x:Name="FilterStarred" Content="Starred" GroupName="ItemFilter"
                                 IsChecked="{Binding IsFilterStarred, Mode=TwoWay}" />
                </StackPanel>

                <ListBox Grid.Row="1" x:Name="ItemList"
                         ItemsSource="{Binding ItemRows}"
                         SelectedItem="{Binding SelectedItemRow, Mode=TwoWay}">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Grid ColumnDefinitions="Auto,*,Auto" Margin="0,2">
                                <TextBlock Grid.Column="0" Text="{Binding StarGlyph}" Margin="0,0,6,0" />
                                <StackPanel Grid.Column="1">
                                    <TextBlock Text="{Binding Title}"
                                               FontWeight="{Binding TitleWeight}"
                                               TextTrimming="CharacterEllipsis" />
                                    <TextBlock Text="{Binding FeedName}" Opacity="0.6" FontSize="11" />
                                </StackPanel>
                                <TextBlock Grid.Column="2" Text="{Binding RelativeDate}"
                                           Opacity="0.6" FontSize="11" VerticalAlignment="Top" />
                            </Grid>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </Grid>

            <GridSplitter Grid.Column="3" ResizeDirection="Columns" />

            <ScrollViewer Grid.Column="4" x:Name="ReadingScroll">
                <StackPanel Margin="24,16" MaxWidth="{Binding ColumnWidth}">
                    <TextBlock x:Name="ArticleTitle" Text="{Binding ArticleTitle}"
                               FontSize="22" FontWeight="SemiBold" TextWrapping="Wrap" />
                    <TextBlock x:Name="ArticleMeta" Text="{Binding ArticleMeta}"
                               Opacity="0.65" Margin="0,4,0,2" TextWrapping="Wrap" />
                    <Border x:Name="OfflineBadge" IsVisible="{Binding ShowOfflineBadge}"
                            Padding="6,3" Margin="0,4" CornerRadius="3"
                            Background="{DynamicResource SystemControlBackgroundListLowBrush}">
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <TextBlock Text="{Binding OfflineBadgeText}" FontSize="11" />
                            <Button x:Name="FetchFullArticleButton" Content="Fetch full article"
                                    FontSize="11" Padding="6,0"
                                    IsVisible="{Binding CanFetchFullArticle}"
                                    Command="{Binding FetchFullArticleCommand}" />
                        </StackPanel>
                    </Border>
                    <md:LucidMarkdownView x:Name="ReadingPane" Markdown="{Binding ArticleMarkdown}" />
                </StackPanel>
            </ScrollViewer>
        </Grid>

        <Border Grid.Row="2" Padding="8,4" BorderThickness="0,1,0,0"
                BorderBrush="{DynamicResource SystemControlForegroundBaseLowBrush}">
            <TextBlock x:Name="StatusText" Text="{Binding StatusMessage}" FontSize="11" Opacity="0.75" />
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 6: Write the window code-behind**

Create `LucidReader/Views/MainWindow.axaml.cs`. This task provides the shell, the properties the XAML binds, theme application and feed-tree loading. Later tasks fill in the behaviour partials.

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Models;
using MarkdownViewer.Models;
using MarkdownViewer.Services;

namespace LucidReader.Views;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ReaderServices _services;
    private readonly ThemeService _theme;

    private FeedTreeNode? _selectedFeedNode;
    private ItemRow? _selectedItemRow;
    private string _searchText = string.Empty;
    private string _statusMessage = string.Empty;
    private ItemFilter _filter = ItemFilter.All;

    public MainWindow(ReaderServices services)
    {
        _services = services;
        InitializeComponent();
        DataContext = this;

        _theme = new ThemeService(Application.Current!);
        ApplySettings(_services.Settings);
        _services.SettingsChanged += settings => Dispatcher.UIThread.Post(() => ApplySettings(settings));

        Opened += async (_, _) => await OnOpenedAsync();
        Closing += (_, _) => _services.SettingsChanged -= null;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public ObservableCollection<FeedTreeNode> FeedNodes { get; } = [];
    public ObservableCollection<ItemRow> ItemRows { get; } = [];

    public double ColumnWidth => _services.Settings.ColumnWidth;

    public FeedTreeNode? SelectedFeedNode
    {
        get => _selectedFeedNode;
        set
        {
            if (ReferenceEquals(_selectedFeedNode, value)) return;
            _selectedFeedNode = value;
            Raise();
            _ = LoadItemsAsync();
        }
    }

    public ItemRow? SelectedItemRow
    {
        get => _selectedItemRow;
        set
        {
            if (ReferenceEquals(_selectedItemRow, value)) return;
            _selectedItemRow = value;
            Raise();
            _ = OnItemSelectedAsync(value);
        }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (_searchText == value) return; _searchText = value; Raise(); _ = OnSearchTextChangedAsync(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage == value) return; _statusMessage = value; Raise(); }
    }

    public bool IsFilterAll
    {
        get => _filter == ItemFilter.All;
        set { if (value) SetFilter(ItemFilter.All); }
    }

    public bool IsFilterUnread
    {
        get => _filter == ItemFilter.Unread;
        set { if (value) SetFilter(ItemFilter.Unread); }
    }

    public bool IsFilterStarred
    {
        get => _filter == ItemFilter.Starred;
        set { if (value) SetFilter(ItemFilter.Starred); }
    }

    private void SetFilter(ItemFilter filter)
    {
        if (_filter == filter) return;
        _filter = filter;
        Raise(nameof(IsFilterAll));
        Raise(nameof(IsFilterUnread));
        Raise(nameof(IsFilterStarred));
        _ = LoadItemsAsync();
    }

    protected ItemFilter CurrentFilter => _filter;

    private async Task OnOpenedAsync()
    {
        if (_services.StartupWarning is { } warning)
            StatusMessage = "Storage maintenance could not run: " + warning.Message;

        await LoadFeedTreeAsync();
    }

    private void ApplySettings(ReaderSettings settings)
    {
        _theme.ApplyTheme(Enum.TryParse<AppTheme>(settings.Theme, true, out var parsed)
            ? parsed
            : AppTheme.Auto);
        Raise(nameof(ColumnWidth));
    }

    /// <summary>
    /// Rebuilds the whole tree: three smart rows, then folders with their feeds
    /// nested under them, then feeds with no folder.
    /// </summary>
    public async Task LoadFeedTreeAsync()
    {
        var folders = await _services.Folders.GetAllAsync();
        var feeds = await _services.Feeds.GetAllAsync();

        var unreadByFeed = new Dictionary<long, int>();
        foreach (var feed in feeds)
            unreadByFeed[feed.Id] = await _services.Items.GetUnreadCountAsync(feed.Id);

        FeedNodes.Clear();

        FeedNodes.Add(new FeedTreeNode
        {
            Title = "All items", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.All
        });
        FeedNodes.Add(new FeedTreeNode
        {
            Title = "Unread", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.Unread,
            UnreadCount = unreadByFeed.Values.Sum()
        });
        FeedNodes.Add(new FeedTreeNode
        {
            Title = "Starred", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.Starred
        });

        foreach (var folder in folders)
        {
            var children = feeds.Where(f => f.FolderId == folder.Id).ToList();

            FeedNodes.Add(new FeedTreeNode
            {
                Title = folder.Name,
                Kind = FeedTreeNodeKind.Folder,
                FolderId = folder.Id,
                UnreadCount = children.Sum(f => unreadByFeed.GetValueOrDefault(f.Id))
            });

            foreach (var feed in children) FeedNodes.Add(ToNode(feed, unreadByFeed));
        }

        foreach (var feed in feeds.Where(f => f.FolderId is null))
            FeedNodes.Add(ToNode(feed, unreadByFeed));
    }

    private static FeedTreeNode ToNode(Feed feed, IReadOnlyDictionary<long, int> unread) => new()
    {
        Title = feed.DisplayTitle,
        Kind = FeedTreeNodeKind.Feed,
        FeedId = feed.Id,
        FolderId = feed.FolderId,
        UnreadCount = unread.GetValueOrDefault(feed.Id),
        ConsecutiveFailures = feed.ConsecutiveFailures,
        LastError = feed.LastError,
        IsAutoPaused = feed.AutoPausedUtc is not null,
        IsEnabled = feed.IsEnabled
    };

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

The remaining members the XAML binds (`ItemRows` loading, article properties, and every command) are added by Tasks 7 through 11 as partial-class files. To keep this task compiling on its own, add stubs for them in this file that later tasks replace, and say in your report which stubs you left.

- [ ] **Step 7: Run the tests and confirm the app starts**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj 2>&1 | tail -5
dotnet build LucidReader/LucidReader.csproj 2>&1 | tail -3
```

Expected: tests pass, build clean.

- [ ] **Step 8: Add the test project to CI and commit**

CI already runs LucidReader.Core.Tests, which now also covers the app project plain classes, so no new test project needs wiring. Add a build step for LucidReader/LucidReader.csproj on a single line if Task 1 did not already add one.

```bash
git add LucidReader LucidReader.Core.Tests .github/workflows/ci.yml
git commit -m "feat(reader): three-pane shell"
```

---

## Task 7: The item list and mark-as-read dwell

**Files:**
- Create: `LucidReader/Views/MainWindow.Items.cs`
- Test: `LucidReader.Core.Tests/Ui/ItemListTests.cs`

**Interfaces:**
- Consumes: `ItemRepository.QueryAsync`, `ItemQuery`, `ItemFilter`, `SelectedFeedNode` (Task 6).
- Produces, on `MainWindow`: `Task LoadItemsAsync()`, `Task OnItemSelectedAsync(ItemRow? row)`, `Task MarkSelectedReadAsync()`, and `internal ItemQuery BuildQuery()` exposed for testing.

**The dwell rule.** Selecting an item marks it read after a delay, not instantly. Holding `J` to scan a list must not mark everything read behind you. The delay is `ReaderSettings.MarkReadDwellMilliseconds`, default 800.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Ui/ItemListTests.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class ItemListTests : IAsyncLifetime
{
    private string _dir = string.Empty;
    private ReaderServices _services = null!;

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lucidreader-uitests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _services = await ReaderServices.StartAsync(
            Path.Combine(_dir, "reader.db"), Path.Combine(_dir, "settings.json"));
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task Selecting_a_feed_queries_only_that_feed()
    {
        var a = await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        var b = await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://b.example/feed.xml" });
        await AddItemAsync(a, "a1");
        await AddItemAsync(b, "b1");

        var forA = await _services.Items.QueryAsync(new ItemQuery(a, null, ItemFilter.All, 200, 0));

        Assert.Single(forA);
    }

    [Fact]
    public async Task Selecting_a_folder_queries_every_feed_in_it()
    {
        var folder = await _services.Folders.AddAsync("News");
        var a = await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml", FolderId = folder });
        var b = await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://b.example/feed.xml", FolderId = folder });
        await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://c.example/feed.xml" });
        await AddItemAsync(a, "a1");
        await AddItemAsync(b, "b1");

        var inFolder = await _services.Items.QueryAsync(new ItemQuery(null, folder, ItemFilter.All, 200, 0));

        Assert.Equal(2, inFolder.Count);
    }

    [Fact]
    public async Task The_unread_filter_excludes_read_items()
    {
        var feed = await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        var read = await AddItemAsync(feed, "r");
        await AddItemAsync(feed, "u");
        await _services.Items.SetReadAsync(read, true);

        var unread = await _services.Items.QueryAsync(new ItemQuery(feed, null, ItemFilter.Unread, 200, 0));

        Assert.Single(unread);
        Assert.Equal("u", unread[0].Guid);
    }

    private Task<long> AddItemAsync(long feedId, string guid) => _services.Items.UpsertAsync(new FeedItem
    {
        FeedId = feedId,
        Guid = guid,
        Title = guid,
        FirstSeenUtc = DateTimeOffset.Parse("2026-08-29T10:00:00Z")
    });
}
```

- [ ] **Step 2: Run the tests to verify they pass or fail as expected**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter ItemListTests 2>&1 | tail -5
```

These exercise Core through the composition root, so they should pass immediately. They exist to pin the query shapes the UI relies on before the UI is written; if `BuildQuery` later produces a different shape these are the tests that notice.

- [ ] **Step 3: Write the item list partial**

Create `LucidReader/Views/MainWindow.Items.cs`:

```csharp
using Avalonia.Threading;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Models;

namespace LucidReader.Views;

public partial class MainWindow
{
    private CancellationTokenSource? _dwellCts;

    /// <summary>
    /// Builds the query for whatever is selected in the feed tree. A smart row
    /// queries across every feed and overrides the filter chips; a folder or a
    /// feed scopes the query and keeps the chosen filter.
    /// </summary>
    internal ItemQuery BuildQuery()
    {
        var node = SelectedFeedNode;

        if (node is null || node.Kind == FeedTreeNodeKind.Smart)
            return new ItemQuery(null, null, node?.SmartFilter ?? CurrentFilter, 500, 0);

        return new ItemQuery(
            node.Kind == FeedTreeNodeKind.Feed ? node.FeedId : null,
            node.Kind == FeedTreeNodeKind.Folder ? node.FolderId : null,
            CurrentFilter,
            500,
            0);
    }

    public async Task LoadItemsAsync()
    {
        var items = await _services.Items.QueryAsync(BuildQuery());
        var feeds = (await _services.Feeds.GetAllAsync())
            .ToDictionary(f => f.Id, f => f.DisplayTitle);
        var now = DateTimeOffset.UtcNow;

        ItemRows.Clear();
        foreach (var item in items)
        {
            ItemRows.Add(new ItemRow
            {
                Item = item,
                FeedName = feeds.GetValueOrDefault(item.FeedId, "Unknown feed"),
                IsRead = item.IsRead,
                IsStarred = item.IsStarred,
                RelativeDate = ItemRow.FormatRelative(
                    item.PublishedUtc ?? item.FirstSeenUtc, now)
            });
        }

        StatusMessage = ItemRows.Count == 0
            ? "No articles here yet."
            : $"{ItemRows.Count} articles";
    }

    private async Task OnItemSelectedAsync(ItemRow? row)
    {
        // Cancel any pending mark-as-read from the previously selected item.
        // Without this, holding J to scan a list marks every item read behind
        // you, which is the single most annoying thing a reader can do.
        if (_dwellCts is not null)
        {
            await _dwellCts.CancelAsync();
            _dwellCts.Dispose();
            _dwellCts = null;
        }

        await ShowArticleAsync(row);

        if (row is null || row.IsRead) return;

        _dwellCts = new CancellationTokenSource();
        var token = _dwellCts.Token;
        var dwell = TimeSpan.FromMilliseconds(
            Math.Max(0, _services.Settings.MarkReadDwellMilliseconds));

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(dwell, token);
                await _services.Items.SetReadAsync(row.Id, true, token);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    row.IsRead = true;
                    AdjustUnreadCount(row.Item.FeedId, -1);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    StatusMessage = "Could not mark the article as read: " + ex.Message);
            }
        }, token);
    }

    public async Task MarkSelectedReadAsync()
    {
        if (SelectedItemRow is not { } row) return;

        var target = !row.IsRead;
        await _services.Items.SetReadAsync(row.Id, target);
        row.IsRead = target;
        AdjustUnreadCount(row.Item.FeedId, target ? -1 : 1);
    }

    /// <summary>
    /// Nudges the cached unread counts rather than requerying the whole tree,
    /// so scanning a list stays responsive.
    /// </summary>
    private void AdjustUnreadCount(long feedId, int delta)
    {
        foreach (var node in FeedNodes)
        {
            var affected = node.Kind switch
            {
                FeedTreeNodeKind.Feed => node.FeedId == feedId,
                FeedTreeNodeKind.Smart => node.SmartFilter == ItemFilter.Unread,
                FeedTreeNodeKind.Folder => FeedNodes.Any(n =>
                    n.Kind == FeedTreeNodeKind.Feed && n.FeedId == feedId && n.FolderId == node.FolderId),
                _ => false
            };

            if (affected) node.UnreadCount = Math.Max(0, node.UnreadCount + delta);
        }
    }
}
```

- [ ] **Step 4: Run the suite and commit**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj 2>&1 | tail -3
git add LucidReader/Views/MainWindow.Items.cs LucidReader.Core.Tests/Ui/ItemListTests.cs
git commit -m "feat(reader): item list with mark-as-read dwell"
```

---

## Task 8: The reading pane, and link safety

**Files:**
- Create: `LucidReader/Views/MainWindow.Reading.cs`
- Create: `LucidReader/Services/SafeLinkOpener.cs`
- Test: `LucidReader.Core.Tests/Ui/SafeLinkOpenerTests.cs`

**Interfaces:**
- Produces:
  - `static class SafeLinkOpener` with `static bool IsSafe(string? url)` and `static bool TryOpen(string? url, out string? refusalReason)`.
  - On `MainWindow`: `Task ShowArticleAsync(ItemRow? row)`, `Task FetchFullArticleAsync()`, and the article properties the XAML binds (`ArticleTitle`, `ArticleMeta`, `ArticleMarkdown`, `ShowOfflineBadge`, `OfflineBadgeText`, `CanFetchFullArticle`).

**This task carries a security requirement, and it is the reason it exists as its own task.**

`FeedParser` resolves link hrefs without filtering the scheme. In Core that is inert, because Core never navigates. The reading pane does navigate, and every URL in it came from a remote feed, which is attacker-controlled input. A `javascript:` URL handed to a browser launcher, or a `file://` URL handed to a shell open, is a real exploit path, not a theoretical one.

Nothing may open, launch, or follow a URL from feed content without going through `SafeLinkOpener`. Allowlist `http` and `https`. Do not blocklist known-bad schemes; a blocklist is wrong by construction here because the set of dangerous schemes is open-ended and platform-specific.

- [ ] **Step 1: Write the failing security tests**

Create `LucidReader.Core.Tests/Ui/SafeLinkOpenerTests.cs`:

```csharp
using LucidReader.Services;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class SafeLinkOpenerTests
{
    [Theory]
    [InlineData("https://example.com/article")]
    [InlineData("http://example.com/article")]
    [InlineData("HTTPS://EXAMPLE.COM/SHOUTING")]
    [InlineData("https://example.com/path?a=1&b=2#frag")]
    public void Http_and_https_are_allowed(string url)
    {
        Assert.True(SafeLinkOpener.IsSafe(url));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("  javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("file://C:/Windows/System32/calc.exe")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("ms-msdt:/id")]
    [InlineData("smb://attacker.example/share")]
    [InlineData("ftp://example.com/file")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("about:blank")]
    [InlineData("chrome://settings")]
    public void Every_other_scheme_is_refused(string url)
    {
        Assert.False(SafeLinkOpener.IsSafe(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url at all")]
    [InlineData("/relative/path")]
    [InlineData("//protocol-relative.example/x")]
    public void Anything_that_is_not_an_absolute_http_url_is_refused(string? url)
    {
        Assert.False(SafeLinkOpener.IsSafe(url));
    }

    [Fact]
    public void A_refused_url_reports_a_reason_and_does_not_open()
    {
        var opened = SafeLinkOpener.TryOpen("javascript:alert(1)", out var reason);

        Assert.False(opened);
        Assert.NotNull(reason);
        Assert.Contains("javascript", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_url_with_an_embedded_newline_is_refused()
    {
        Assert.False(SafeLinkOpener.IsSafe("https://example.com/\njavascript:alert(1)"));
    }
}
```

The `mailto:` case is deliberately refused. It is not dangerous in the way `javascript:` is, but allowing it means deciding how to launch a mail client, and v1 does not need that. Refusing it is a product decision recorded by a test, not an oversight.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter SafeLinkOpenerTests 2>&1 | tail -10
```

Expected: compilation failure.

- [ ] **Step 3: Write SafeLinkOpener**

Create `LucidReader/Services/SafeLinkOpener.cs`:

```csharp
using System.Diagnostics;

namespace LucidReader.Services;

/// <summary>
/// The only sanctioned way to open a URL that came from feed content.
///
/// Every URL the reading pane shows was written by a remote publisher, so it
/// is attacker-controlled. Handing an arbitrary scheme to the platform's
/// "open this" mechanism is a real exploit path: javascript: and data: can run
/// script in some hosts, file: can read the disk, and several platform-specific
/// schemes launch handlers with arguments.
///
/// This is an allowlist of http and https, deliberately not a blocklist. The
/// set of dangerous schemes is open-ended and platform-specific, so enumerating
/// the bad ones is guaranteed to miss some.
/// </summary>
public static class SafeLinkOpener
{
    public static bool IsSafe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        // Control characters can be used to smuggle a second target past a
        // naive check or a shell.
        if (url.Any(char.IsControl)) return false;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    public static bool TryOpen(string? url, out string? refusalReason)
    {
        if (!IsSafe(url))
        {
            refusalReason = $"Refused to open a link that is not a web address: {Describe(url)}";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url!.Trim()) { UseShellExecute = true });
            refusalReason = null;
            return true;
        }
        catch (Exception ex)
        {
            refusalReason = "Could not open the link: " + ex.Message;
            return false;
        }
    }

    private static string Describe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "(empty)";
        var trimmed = url.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80] + "...";
    }
}
```

- [ ] **Step 4: Write the reading pane partial**

Create `LucidReader/Views/MainWindow.Reading.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Models;
using LucidReader.Services;

namespace LucidReader.Views;

public partial class MainWindow
{
    private string _articleTitle = string.Empty;
    private string _articleMeta = string.Empty;
    private string _articleMarkdown = string.Empty;
    private bool _showOfflineBadge;
    private string _offlineBadgeText = string.Empty;
    private bool _canFetchFullArticle;

    public string ArticleTitle
    {
        get => _articleTitle;
        private set { if (_articleTitle == value) return; _articleTitle = value; Raise(); }
    }

    public string ArticleMeta
    {
        get => _articleMeta;
        private set { if (_articleMeta == value) return; _articleMeta = value; Raise(); }
    }

    public string ArticleMarkdown
    {
        get => _articleMarkdown;
        private set { if (_articleMarkdown == value) return; _articleMarkdown = value; Raise(); }
    }

    public bool ShowOfflineBadge
    {
        get => _showOfflineBadge;
        private set { if (_showOfflineBadge == value) return; _showOfflineBadge = value; Raise(); }
    }

    public string OfflineBadgeText
    {
        get => _offlineBadgeText;
        private set { if (_offlineBadgeText == value) return; _offlineBadgeText = value; Raise(); }
    }

    public bool CanFetchFullArticle
    {
        get => _canFetchFullArticle;
        private set { if (_canFetchFullArticle == value) return; _canFetchFullArticle = value; Raise(); }
    }

    public async Task ShowArticleAsync(ItemRow? row)
    {
        if (row is null)
        {
            ArticleTitle = string.Empty;
            ArticleMeta = string.Empty;
            ArticleMarkdown = string.Empty;
            ShowOfflineBadge = false;
            CanFetchFullArticle = false;
            return;
        }

        // Re-read rather than trusting the row: the download may have completed
        // since the list was populated.
        var item = await _services.Items.GetAsync(row.Id) ?? row.Item;

        ArticleTitle = string.IsNullOrWhiteSpace(item.Title) ? "Untitled" : item.Title!;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Author)) parts.Add(item.Author!);
        parts.Add(row.FeedName);
        if (item.PublishedUtc is { } published)
            parts.Add(published.ToLocalTime().ToString("f"));
        ArticleMeta = string.Join("  ยท  ", parts);

        ArticleMarkdown = item.ContentMarkdown
            ?? item.Summary
            ?? "This article has no content yet.";

        (ShowOfflineBadge, OfflineBadgeText, CanFetchFullArticle) = item.OfflineState switch
        {
            OfflineState.Downloaded when item.ContentSource == ContentSource.Extracted =>
                (false, string.Empty, false),
            OfflineState.Downloaded =>
                (true, "Showing the summary the feed provided.", item.Link is not null),
            OfflineState.Failed =>
                (true, "The full article could not be downloaded. " + (item.OfflineError ?? string.Empty), true),
            OfflineState.Pending =>
                (true, "Downloading the full article...", false),
            _ =>
                (true, "Showing the summary the feed provided.", item.Link is not null)
        };
    }

    public async Task FetchFullArticleAsync()
    {
        if (SelectedItemRow is not { } row) return;

        StatusMessage = "Fetching the full article...";
        try
        {
            await _services.Downloader.DownloadNowAsync(row.Id);
            await ShowArticleAsync(row);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not fetch the article: " + ex.Message;
        }
    }

    /// <summary>
    /// Every link in the reading pane came from a remote feed, so it goes
    /// through the allowlist rather than straight to the platform opener.
    /// </summary>
    private void OnArticleLinkClicked(string? url)
    {
        if (!_services.Settings.OpenLinksExternally) return;

        if (!SafeLinkOpener.TryOpen(url, out var reason))
            StatusMessage = reason ?? "That link could not be opened.";
    }

    public void OpenOriginalArticle()
    {
        var link = SelectedItemRow?.Item.Link;
        if (!SafeLinkOpener.TryOpen(link, out var reason))
            StatusMessage = reason ?? "This article has no link to open.";
    }
}
```

Wire `LucidMarkdownView.LinkClick` to `OnArticleLinkClicked` in the window constructor. Check the exact `LinkClickedEventArgs` shape in `Mostlylucid.LucidView.Markdown/LucidMarkdownView.axaml.cs` before writing that line; the event exists, but confirm the property holding the URL rather than guessing its name.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter SafeLinkOpenerTests 2>&1 | tail -5
```

Expected: 25 passed.

- [ ] **Step 6: Commit**

```bash
git add LucidReader/Views/MainWindow.Reading.cs LucidReader/Services/SafeLinkOpener.cs LucidReader.Core.Tests/Ui/SafeLinkOpenerTests.cs
git commit -m "feat(reader): reading pane with an allowlist for feed links"
```

---

## Task 9: Item actions, keyboard navigation, tags and markdown export

**Files:**
- Create: `LucidReader/Views/MainWindow.Actions.cs`
- Test: `LucidReader.Core.Tests/Ui/ItemActionsTests.cs`

**Interfaces:**
- Produces, on `MainWindow`: every `ICommand` the XAML `KeyBindings` bind (`NextItemCommand`, `PreviousItemCommand`, `NextUnreadCommand`, `PreviousUnreadCommand`, `ToggleReadCommand`, `ToggleStarCommand`, `RefreshCurrentFeedCommand`, `RefreshAllCommand`, `OpenOriginalCommand`, `FocusSearchCommand`, `FindInArticleCommand`, `AddFeedCommand`, `OpenSettingsCommand`, `FetchFullArticleCommand`, `ExportArticleCommand`, `EditTagsCommand`), plus `internal int FindNextIndex(int current, bool forward, bool unreadOnly)` exposed for testing the navigation rules without a window.

**Note on scope.** PDF export is deliberately absent, per the decision recorded at the top of this plan. `ExportArticleCommand` writes markdown.

- [ ] **Step 1: Write the failing navigation tests**

Navigation is the part with real logic; the rest is one-line command bodies. Create `LucidReader.Core.Tests/Ui/ItemActionsTests.cs`:

```csharp
using LucidReader.Views;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class ItemActionsTests
{
    // read states of a five-item list: unread, read, unread, read, unread
    private static readonly bool[] Read = [false, true, false, true, false];

    private static int Next(int current, bool forward, bool unreadOnly) =>
        MainWindow.FindNextIndexIn(Read, current, forward, unreadOnly);

    [Fact]
    public void Next_moves_one_forward()
    {
        Assert.Equal(1, Next(0, forward: true, unreadOnly: false));
    }

    [Fact]
    public void Previous_moves_one_back()
    {
        Assert.Equal(1, Next(2, forward: false, unreadOnly: false));
    }

    [Fact]
    public void Next_unread_skips_read_items()
    {
        Assert.Equal(2, Next(0, forward: true, unreadOnly: true));
    }

    [Fact]
    public void Previous_unread_skips_read_items()
    {
        Assert.Equal(2, Next(4, forward: false, unreadOnly: true));
    }

    [Fact]
    public void Next_at_the_end_stays_put_rather_than_wrapping()
    {
        Assert.Equal(4, Next(4, forward: true, unreadOnly: false));
    }

    [Fact]
    public void Previous_at_the_start_stays_put_rather_than_wrapping()
    {
        Assert.Equal(0, Next(0, forward: false, unreadOnly: false));
    }

    [Fact]
    public void Next_unread_with_nothing_unread_ahead_stays_put()
    {
        bool[] allReadAhead = [false, true, true];

        Assert.Equal(0, MainWindow.FindNextIndexIn(allReadAhead, 0, true, true));
    }

    [Fact]
    public void Navigation_from_no_selection_starts_at_the_first_item()
    {
        Assert.Equal(0, Next(-1, forward: true, unreadOnly: false));
    }

    [Fact]
    public void Navigation_in_an_empty_list_returns_no_selection()
    {
        Assert.Equal(-1, MainWindow.FindNextIndexIn([], -1, true, false));
    }
}
```

Not wrapping is a deliberate choice: wrapping from the last item back to the first while the user holds `J` is disorienting, and every established reader stops at the end.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter ItemActionsTests 2>&1 | tail -10
```

Expected: compilation failure.

- [ ] **Step 3: Write the actions partial**

Create `LucidReader/Views/MainWindow.Actions.cs`:

```csharp
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LucidReader.Models;

namespace LucidReader.Views;

public partial class MainWindow
{
    private ICommand? _nextItem, _previousItem, _nextUnread, _previousUnread;
    private ICommand? _toggleRead, _toggleStar, _refreshCurrent, _refreshAll;
    private ICommand? _openOriginal, _focusSearch, _findInArticle;
    private ICommand? _addFeed, _openSettings, _fetchFull, _exportArticle;

    public ICommand NextItemCommand => _nextItem ??= new RelayCommand(() => Move(true, false));
    public ICommand PreviousItemCommand => _previousItem ??= new RelayCommand(() => Move(false, false));
    public ICommand NextUnreadCommand => _nextUnread ??= new RelayCommand(() => Move(true, true));
    public ICommand PreviousUnreadCommand => _previousUnread ??= new RelayCommand(() => Move(false, true));
    public ICommand ToggleReadCommand => _toggleRead ??= new RelayCommand(async () => await MarkSelectedReadAsync());
    public ICommand ToggleStarCommand => _toggleStar ??= new RelayCommand(async () => await ToggleStarAsync());
    public ICommand RefreshCurrentFeedCommand => _refreshCurrent ??= new RelayCommand(async () => await RefreshCurrentFeedAsync());
    public ICommand RefreshAllCommand => _refreshAll ??= new RelayCommand(async () => await RefreshAllAsync());
    public ICommand OpenOriginalCommand => _openOriginal ??= new RelayCommand(OpenOriginalArticle);
    public ICommand FocusSearchCommand => _focusSearch ??= new RelayCommand(() => SearchBox.Focus());
    public ICommand FindInArticleCommand => _findInArticle ??= new RelayCommand(FocusFindInArticle);
    public ICommand AddFeedCommand => _addFeed ??= new RelayCommand(async () => await ShowAddFeedDialogAsync());
    public ICommand OpenSettingsCommand => _openSettings ??= new RelayCommand(async () => await ShowSettingsDialogAsync());
    public ICommand FetchFullArticleCommand => _fetchFull ??= new RelayCommand(async () => await FetchFullArticleAsync());
    public ICommand ExportArticleCommand => _exportArticle ??= new RelayCommand(async () => await ExportArticleAsync());

    /// <summary>
    /// Navigation rule, kept static and list-shaped so it can be tested without
    /// a window. Deliberately does NOT wrap: running off the end of the list
    /// back to the top while holding J is disorienting.
    /// </summary>
    internal static int FindNextIndexIn(IReadOnlyList<bool> readStates, int current, bool forward, bool unreadOnly)
    {
        if (readStates.Count == 0) return -1;
        if (current < 0) return forward ? 0 : readStates.Count - 1;

        var step = forward ? 1 : -1;

        for (var i = current + step; i >= 0 && i < readStates.Count; i += step)
        {
            if (!unreadOnly || !readStates[i]) return i;
        }

        return current;
    }

    internal int FindNextIndex(int current, bool forward, bool unreadOnly) =>
        FindNextIndexIn(ItemRows.Select(r => r.IsRead).ToList(), current, forward, unreadOnly);

    private void Move(bool forward, bool unreadOnly)
    {
        var current = SelectedItemRow is null ? -1 : ItemRows.IndexOf(SelectedItemRow);
        var next = FindNextIndex(current, forward, unreadOnly);

        if (next < 0 || next == current) return;

        SelectedItemRow = ItemRows[next];
        ItemList.ScrollIntoView(SelectedItemRow);
    }

    private async Task ToggleStarAsync()
    {
        if (SelectedItemRow is not { } row) return;

        var target = !row.IsStarred;
        await _services.Items.SetStarredAsync(row.Id, target);
        row.IsStarred = target;
    }

    private async Task RefreshCurrentFeedAsync()
    {
        if (SelectedFeedNode?.FeedId is not { } feedId)
        {
            await RefreshAllAsync();
            return;
        }

        StatusMessage = "Refreshing...";
        var outcome = await _services.Refresh.RefreshNowAsync(feedId);
        await AfterRefreshAsync(outcome.Success
            ? outcome.NotModified ? "No changes." : $"{outcome.NewItemCount} new articles."
            : "Refresh failed: " + outcome.Error);
    }

    private async Task RefreshAllAsync()
    {
        StatusMessage = "Refreshing every feed...";

        var queued = await _services.Scheduler.TickAsync();
        if (queued == 0)
        {
            // Nothing was due. A manual Refresh All should still fetch, so
            // queue every enabled feed directly.
            foreach (var feed in await _services.Feeds.GetAllAsync())
                if (feed.IsEnabled) _services.Refresh.TryQueue(feed.Id, isManual: true);
        }

        StatusMessage = "Refresh started.";
    }

    private async Task AfterRefreshAsync(string message)
    {
        await LoadFeedTreeAsync();
        await LoadItemsAsync();
        StatusMessage = message;
    }

    /// <summary>
    /// Ctrl+F searches inside the current article. The reading pane is a
    /// markdown control with no built-in find, so this focuses the global
    /// search box pre-scoped to the current feed rather than pretending to
    /// offer something that does not exist.
    /// </summary>
    private void FocusFindInArticle()
    {
        SearchBox.Focus();
        StatusMessage = "Searching across your articles. Clear the box to go back to the list.";
    }

    private async Task ExportArticleAsync()
    {
        if (SelectedItemRow is not { } row) return;

        var item = await _services.Items.GetAsync(row.Id);
        if (item is null) return;

        var suggested = string.Concat((item.Title ?? "article")
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export article as markdown",
            SuggestedFileName = suggested + ".md",
            DefaultExtension = "md",
            FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }]
        });

        if (file is null) return;

        var body = item.ContentMarkdown ?? item.Summary ?? string.Empty;
        var header = $"# {item.Title}\n\n" +
                     (item.Link is { } link ? $"[{link}]({link})\n\n" : string.Empty);

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(header + body);

        StatusMessage = "Exported to " + file.Name;
    }
}
```

- [ ] **Step 4: Add tagging**

`TagRepository` is built in Task 2 and composed in Task 1, but nothing reaches it yet, so tags would ship invisible. Spec 5 lists applying tags as an item action, so wire it here.

Add to `LucidReader/Views/MainWindow.Actions.cs`:

```csharp
    /// <summary>
    /// Tags on the selected article. Editing is a comma-separated list rather
    /// than a bespoke chip editor: tags are a low-traffic feature and a text
    /// box is honest about that.
    /// </summary>
    private async Task EditTagsAsync()
    {
        if (SelectedItemRow is not { } row) return;

        var current = await _services.Tags.GetForItemAsync(row.Id);

        var dialog = new InputDialog(
            "Tags",
            "Comma-separated tags for this article",
            string.Join(", ", current));
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } entered) return;

        var wanted = entered
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var tag in current.Where(t => !wanted.Contains(t, StringComparer.OrdinalIgnoreCase)))
            await _services.Tags.RemoveFromItemAsync(row.Id, tag);

        foreach (var tag in wanted.Where(t => !current.Contains(t, StringComparer.OrdinalIgnoreCase)))
            await _services.Tags.AddToItemAsync(row.Id, tag);

        StatusMessage = wanted.Count == 0 ? "Tags cleared." : "Tags: " + string.Join(", ", wanted);
    }
```

Add `public ICommand EditTagsCommand => _editTags ??= new RelayCommand(async () => await EditTagsAsync());` and a `T` key binding in `MainWindow.axaml`.

You will need a small `InputDialog(string title, string prompt, string initialValue)` with a `string? Result` property, null when cancelled. lucidVIEW has one at `MarkdownViewer/Views/InputDialog.axaml`; copy its shape rather than inventing a new one.

Add a test to `ItemActionsTests` proving the round trip: set tags on an item, read them back, then remove one and confirm only the other remains. Drive it through `_services.Tags` directly, since the dialog itself needs a window.

- [ ] **Step 5: Run the tests and commit**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter ItemActionsTests 2>&1 | tail -5
```

Expected: 10 passed.

```bash
git add LucidReader/Views LucidReader.Core.Tests/Ui/ItemActionsTests.cs
git commit -m "feat(reader): item actions, keyboard navigation, tags and markdown export"
```

---

## Task 10: Search

**Files:**
- Create: `LucidReader/Views/MainWindow.Search.cs`
- Test: `LucidReader.Core.Tests/Ui/SearchTests.cs`

**Interfaces:**
- Consumes: `SearchRepository.SearchAsync`.
- Produces, on `MainWindow`: `Task OnSearchTextChangedAsync()`, `Task RunSearchAsync(string query)`, `bool IsShowingSearchResults { get; }`.

Typing in the search box replaces the item list with results across every feed. Clearing it restores the previous selection's list. Input is debounced so a query does not run per keystroke.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Ui/SearchTests.cs`:

```csharp
using LucidReader.Core.Model;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class SearchTests : IAsyncLifetime
{
    private string _dir = string.Empty;
    private ReaderServices _services = null!;

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lucidreader-uitests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _services = await ReaderServices.StartAsync(
            Path.Combine(_dir, "reader.db"), Path.Combine(_dir, "settings.json"));

        var feed = await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        var one = await _services.Items.UpsertAsync(new FeedItem
        {
            FeedId = feed, Guid = "g1", Title = "Avalonia rendering internals",
            FirstSeenUtc = DateTimeOffset.Parse("2026-08-29T10:00:00Z")
        });
        await _services.Items.SetContentAsync(one, "Discussing the compositor in depth.", ContentSource.Feed);

        var two = await _services.Items.UpsertAsync(new FeedItem
        {
            FeedId = feed, Guid = "g2", Title = "Something unrelated",
            FirstSeenUtc = DateTimeOffset.Parse("2026-08-29T10:00:00Z")
        });
        await _services.Items.SetContentAsync(two, "Nothing to see.", ContentSource.Feed);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task Searching_matches_titles()
    {
        var results = await _services.Search.SearchAsync("Avalonia", 50);

        Assert.Single(results);
    }

    [Fact]
    public async Task Searching_matches_article_bodies()
    {
        var results = await _services.Search.SearchAsync("compositor", 50);

        Assert.Single(results);
    }

    [Fact]
    public async Task A_query_with_punctuation_returns_nothing_rather_than_throwing()
    {
        var results = await _services.Search.SearchAsync("\"unbalanced AND (", 50);

        Assert.Empty(results);
    }

    [Fact]
    public async Task A_blank_query_returns_nothing()
    {
        Assert.Empty(await _services.Search.SearchAsync("   ", 50));
    }
}
```

- [ ] **Step 2: Write the search partial**

Create `LucidReader/Views/MainWindow.Search.cs`:

```csharp
using LucidReader.Models;

namespace LucidReader.Views;

public partial class MainWindow
{
    private CancellationTokenSource? _searchCts;

    public bool IsShowingSearchResults { get; private set; }

    /// <summary>
    /// Debounced so a query does not run on every keystroke. Clearing the box
    /// restores whatever the feed tree selection was showing.
    /// </summary>
    public async Task OnSearchTextChangedAsync()
    {
        if (_searchCts is not null)
        {
            await _searchCts.CancelAsync();
            _searchCts.Dispose();
        }

        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        var query = SearchText;

        if (string.IsNullOrWhiteSpace(query))
        {
            IsShowingSearchResults = false;
            await LoadItemsAsync();
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), token);
            await RunSearchAsync(query, token);
        }
        catch (OperationCanceledException) { }
    }

    public Task RunSearchAsync(string query) => RunSearchAsync(query, CancellationToken.None);

    private async Task RunSearchAsync(string query, CancellationToken ct)
    {
        var results = await _services.Search.SearchAsync(query, 500, ct);
        ct.ThrowIfCancellationRequested();

        var feeds = (await _services.Feeds.GetAllAsync(ct))
            .ToDictionary(f => f.Id, f => f.DisplayTitle);
        var now = DateTimeOffset.UtcNow;

        ItemRows.Clear();
        foreach (var item in results)
        {
            ItemRows.Add(new ItemRow
            {
                Item = item,
                FeedName = feeds.GetValueOrDefault(item.FeedId, "Unknown feed"),
                IsRead = item.IsRead,
                IsStarred = item.IsStarred,
                RelativeDate = ItemRow.FormatRelative(item.PublishedUtc ?? item.FirstSeenUtc, now)
            });
        }

        IsShowingSearchResults = true;
        StatusMessage = results.Count == 0
            ? $"Nothing found for \"{query}\"."
            : $"{results.Count} results for \"{query}\"";
    }
}
```

Note for whoever writes the settings screen later: `SearchRepository` quotes every whitespace-separated term as an FTS5 phrase literal, which is what makes a stray quote or parenthesis safe. The side effect is that an explicit `"phrase"` query or a trailing `*` wildcard is treated as literal text. That is a known limitation recorded in Plan 1's ledger, not a bug to fix here.

- [ ] **Step 3: Run the tests and commit**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter SearchTests 2>&1 | tail -5
git add LucidReader/Views/MainWindow.Search.cs LucidReader.Core.Tests/Ui/SearchTests.cs
git commit -m "feat(reader): full-text search across every feed"
```

---

## Task 11: The global settings dialog

**Files:**
- Create: `LucidReader/Views/SettingsDialog.axaml`, `LucidReader/Views/SettingsDialog.axaml.cs`
- Create: `LucidReader/Views/MainWindow.Settings.cs`
- Test: `LucidReader.Core.Tests/Ui/SettingsDraftTests.cs`

**Interfaces:**
- Produces:
  - `sealed class SettingsDraft` in `LucidReader/Models/SettingsDraft.cs`: a PLAIN class, no Avalonia base type, holding every editable value, with `SettingsDraft(ReaderSettings current)` and `ReaderSettings Apply()`. This is where the mapping and clamping live, and it is what the unit tests exercise.
  - `SettingsDialog(ReaderSettings current, RetentionService retention)` with `SettingsDraft Draft { get; }` and `ReaderSettings? Result { get; }`, null when cancelled. The window is a thin shell over the draft.
  - On `MainWindow`: `Task ShowSettingsDialogAsync()`.

**Why the draft is a separate class.** A dialog cannot be constructed in a unit test here, and the settings mapping is the part actually worth testing. Splitting it out means the mapping is testable as a plain object and the window is verified through the harness, which is the split this whole plan uses.

Four groups, matching spec 6.1: Updates, Offline, Retention, Reading. The retention group shows the current database size and offers a manual clean-up, because the spec is explicit that a retention setting whose effect is invisible is one nobody trusts.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Ui/SettingsDraftTests.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class SettingsDraftTests
{
    [Fact]
    public void Applying_returns_the_edited_settings()
    {
        var draft = new SettingsDraft(ReaderSettings.Defaults)
        {
            DefaultRefreshIntervalMinutes = 120,
            AutoDownloadArticles = false,
            Theme = "Dark"
        };

        var result = draft.Apply();

        Assert.Equal(120, result.DefaultRefreshIntervalMinutes);
        Assert.False(result.AutoDownloadArticles);
        Assert.Equal("Dark", result.Theme);
    }

    [Fact]
    public void Every_other_setting_survives_editing_one_of_them()
    {
        var original = ReaderSettings.Defaults with { MaxArticlesPerFeed = 123, FontSize = 19 };
        var draft = new SettingsDraft(original) { DefaultRefreshIntervalMinutes = 45 };

        var result = draft.Apply();

        Assert.Equal(123, result.MaxArticlesPerFeed);
        Assert.Equal(19, result.FontSize);
    }

    [Fact]
    public void A_refresh_interval_below_the_floor_is_clamped()
    {
        var draft = new SettingsDraft(ReaderSettings.Defaults) { DefaultRefreshIntervalMinutes = 1 };

        Assert.Equal(
            (int)ReaderSettings.MinimumRefreshInterval.TotalMinutes,
            draft.Apply().DefaultRefreshIntervalMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_nonsense_concurrency_value_is_clamped_to_at_least_one(int value)
    {
        var draft = new SettingsDraft(ReaderSettings.Defaults) { MaxConcurrentFetches = value };

        Assert.True(draft.Apply().MaxConcurrentFetches >= 1);
    }

    [Fact]
    public void A_draft_left_untouched_reproduces_the_original_exactly()
    {
        var original = ReaderSettings.Defaults with { FontSize = 17, Theme = "GitHub" };

        Assert.Equal(original, new SettingsDraft(original).Apply());
    }

    [Fact]
    public void Human_readable_sizes_are_formatted_sensibly()
    {
        Assert.Equal("0 bytes", SettingsDraft.FormatBytes(0));
        Assert.Equal("512 bytes", SettingsDraft.FormatBytes(512));
        Assert.Equal("1.0 KB", SettingsDraft.FormatBytes(1024));
        Assert.Equal("1.5 MB", SettingsDraft.FormatBytes(1024 * 1024 * 3 / 2));
        Assert.Equal("2.0 GB", SettingsDraft.FormatBytes(1024L * 1024 * 1024 * 2));
    }
}

The round-trip test matters: it proves the draft carries every setting the dialog does not expose, so opening and closing settings cannot quietly reset something.

**Then verify the actual dialog through the harness**, not through a test:

```
dotnet run --project LucidReader/LucidReader.csproj -- --ux-repl
click SettingsButton
list
describe settings-dialog
get #DefaultRefreshIntervalBox.Text
```

Report what `list` and `describe` showed.
```

The clamp tests matter because the settings floor exists in `ReaderSettings.MinimumRefreshInterval` but nothing stops a dialog writing a smaller number straight into the file, where it would then be silently clamped per feed and confuse anyone reading the settings back.

- [ ] **Step 2: Write the dialog**

Create `LucidReader/Views/SettingsDialog.axaml` with a `TabControl` of four tabs, each a simple two-column grid of label and editor, and OK and Cancel buttons at the bottom. Name the controls after the settings they edit so the UI scripts can target them: `DefaultRefreshIntervalBox`, `RefreshOnStartupCheck`, `MaxConcurrentFetchesBox`, `AutoDownloadCheck`, `FetchFullTextCheck`, `CacheImagesCheck`, `KeepReadDaysBox`, `KeepUnreadForeverCheck`, `MaxArticlesPerFeedBox`, `NeverDeleteStarredCheck`, `DatabaseSizeText`, `CleanUpNowButton`, `ThemeCombo`, `FontSizeBox`, `ColumnWidthBox`, `MarkReadDwellBox`, `OpenLinksExternallyCheck`, `OkButton`, `CancelButton`.

Create `LucidReader/Views/SettingsDialog.axaml.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LucidReader.Core.Maintenance;
using LucidReader.Core.Model;

namespace LucidReader.Views;

public partial class SettingsDialog : Window, INotifyPropertyChanged
{
    private readonly ReaderSettings _original;
    private readonly RetentionService? _retention;
    private string _databaseSize = "Calculating...";

    public SettingsDialog(ReaderSettings current, RetentionService retention)
    {
        _original = current;
        _retention = retention;

        InitializeComponent();
        DataContext = this;

        DefaultRefreshIntervalMinutes = current.DefaultRefreshIntervalMinutes;
        RefreshOnStartup = current.RefreshOnStartup;
        PauseWhenOffline = current.PauseWhenOffline;
        MaxConcurrentFetches = current.MaxConcurrentFetches;
        AutoDownloadArticles = current.AutoDownloadArticles;
        FetchFullText = current.FetchFullText;
        CacheImages = current.CacheImages;
        MaxConcurrentDownloads = current.MaxConcurrentDownloads;
        KeepReadArticlesDays = current.KeepReadArticlesDays;
        KeepUnreadForever = current.KeepUnreadForever;
        KeepUnreadDays = current.KeepUnreadDays;
        MaxArticlesPerFeed = current.MaxArticlesPerFeed;
        NeverDeleteStarred = current.NeverDeleteStarred;
        Theme = current.Theme;
        FontSize = current.FontSize;
        ColumnWidth = current.ColumnWidth;
        MarkReadDwellMilliseconds = current.MarkReadDwellMilliseconds;
        OpenLinksExternally = current.OpenLinksExternally;

        Opened += async (_, _) => await RefreshDatabaseSizeAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public int DefaultRefreshIntervalMinutes { get; set; }
    public bool RefreshOnStartup { get; set; }
    public bool PauseWhenOffline { get; set; }
    public int MaxConcurrentFetches { get; set; }
    public bool AutoDownloadArticles { get; set; }
    public bool FetchFullText { get; set; }
    public bool CacheImages { get; set; }
    public int MaxConcurrentDownloads { get; set; }
    public int KeepReadArticlesDays { get; set; }
    public bool KeepUnreadForever { get; set; }
    public int KeepUnreadDays { get; set; }
    public int MaxArticlesPerFeed { get; set; }
    public bool NeverDeleteStarred { get; set; }
    public string Theme { get; set; } = "Auto";
    public double FontSize { get; set; }
    public double ColumnWidth { get; set; }
    public int MarkReadDwellMilliseconds { get; set; }
    public bool OpenLinksExternally { get; set; }

    public string[] ThemeOptions { get; } =
        ["Auto", "Light", "Dark", "VsCode", "GitHub", "MostlylucidDark", "MostlylucidLight", "Pride"];

    public string DatabaseSize
    {
        get => _databaseSize;
        private set { if (_databaseSize == value) return; _databaseSize = value; Raise(); }
    }

    public ReaderSettings? Result { get; private set; }

    /// <summary>
    /// Applies the edits over the settings the dialog was opened with, so any
    /// setting this dialog does not expose survives untouched.
    /// </summary>
    public void Accept()
    {
        var floor = (int)ReaderSettings.MinimumRefreshInterval.TotalMinutes;

        Result = _original with
        {
            DefaultRefreshIntervalMinutes = Math.Max(floor, DefaultRefreshIntervalMinutes),
            RefreshOnStartup = RefreshOnStartup,
            PauseWhenOffline = PauseWhenOffline,
            MaxConcurrentFetches = Math.Max(1, MaxConcurrentFetches),
            AutoDownloadArticles = AutoDownloadArticles,
            FetchFullText = FetchFullText,
            CacheImages = CacheImages,
            MaxConcurrentDownloads = Math.Max(1, MaxConcurrentDownloads),
            KeepReadArticlesDays = Math.Max(0, KeepReadArticlesDays),
            KeepUnreadForever = KeepUnreadForever,
            KeepUnreadDays = Math.Max(0, KeepUnreadDays),
            MaxArticlesPerFeed = Math.Max(0, MaxArticlesPerFeed),
            NeverDeleteStarred = NeverDeleteStarred,
            Theme = Theme,
            FontSize = Math.Clamp(FontSize, 9, 40),
            ColumnWidth = Math.Clamp(ColumnWidth, 320, 2000),
            MarkReadDwellMilliseconds = Math.Max(0, MarkReadDwellMilliseconds),
            OpenLinksExternally = OpenLinksExternally
        };

        Close();
    }

    public void Cancel()
    {
        Result = null;
        Close();
    }

    public async Task CleanUpNowAsync()
    {
        if (_retention is null) return;

        DatabaseSize = "Cleaning up...";
        var deleted = await _retention.PruneAsync();
        await RefreshDatabaseSizeAsync();
        DatabaseSize += $"  ({deleted} articles removed)";
    }

    private async Task RefreshDatabaseSizeAsync()
    {
        if (_retention is null) return;

        try { DatabaseSize = FormatBytes(await _retention.GetDatabaseSizeBytesAsync()); }
        catch (Exception ex) { DatabaseSize = "Unavailable: " + ex.Message; }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} bytes";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.0} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

The tests construct the dialog with a null `RetentionService`, which is why every use of it is null-guarded. That is deliberate: the settings mapping is worth testing without a database.

- [ ] **Step 3: Wire it into MainWindow**

Create `LucidReader/Views/MainWindow.Settings.cs`:

```csharp
namespace LucidReader.Views;

public partial class MainWindow
{
    public async Task ShowSettingsDialogAsync()
    {
        var dialog = new SettingsDialog(_services.Settings, _services.Retention);
        await dialog.ShowDialog(this);

        if (dialog.Result is not { } updated) return;

        await _services.UpdateSettingsAsync(updated);
        StatusMessage = "Settings saved.";

        // Concurrency is fixed when the coordinators are constructed, so a
        // change to either concurrency setting only takes effect next launch.
        // Say so rather than letting the user wonder why nothing changed.
        if (updated.MaxConcurrentFetches != _services.ConfiguredFetchConcurrency ||
            updated.MaxConcurrentDownloads != _services.ConfiguredDownloadConcurrency)
        {
            StatusMessage = "Settings saved. The concurrency changes take effect next time lucidREADER starts.";
        }
    }
}
```

That message is not a workaround for laziness: `EphemeralWorkCoordinator` fixes its concurrency at construction, and rebuilding the two coordinators while work is in flight would be a far larger change than this setting justifies.

- [ ] **Step 4: Run the tests and commit**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter SettingsDraftTests 2>&1 | tail -5
git add LucidReader/Views/SettingsDialog.axaml LucidReader/Views/SettingsDialog.axaml.cs LucidReader/Views/MainWindow.Settings.cs LucidReader.Core.Tests/Ui/SettingsDraftTests.cs
git commit -m "feat(reader): global settings dialog"
```

---

## Task 12: Per-feed settings and the feed context menu

**Files:**
- Create: `LucidReader/Views/FeedSettingsDialog.axaml`, `.axaml.cs`
- Create: `LucidReader/Views/MainWindow.FeedMenu.cs`
- Modify: `LucidReader/Views/MainWindow.axaml` (context menu on the feed tree)
- Test: `LucidReader.Core.Tests/Ui/FeedSettingsDraftTests.cs`

**Interfaces:**
- Produces:
  - `sealed class FeedSettingsDraft` in `LucidReader/Models/FeedSettingsDraft.cs`: a PLAIN class holding the override toggles and values, with `FeedSettingsDraft(Feed feed, ReaderSettings globals)` and `Feed Apply()`. The inherit-versus-override rule lives here and is what the tests exercise.
  - `FeedSettingsDialog(Feed feed, ReaderSettings globals, IReadOnlyList<Folder> folders)` with `FeedSettingsDraft Draft { get; }` and `Feed? Result { get; }`, a thin shell over the draft.
  - On `MainWindow`: `Task ShowFeedSettingsAsync(long feedId)`, `Task RenameFeedAsync(long feedId)`, `Task UnsubscribeAsync(long feedId)`, `Task MarkFeedReadAsync(long feedId)`.

**The inheritance rule this dialog must express.** Each of the four inheritable settings shows as "Use the global setting (current value)" until the user explicitly overrides it. Turning an override off must write null, not the global's current value, or the feed stops following future changes to the global. That distinction is the entire point of the nullable columns and it is easy to destroy from a UI.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Ui/FeedSettingsDraftTests.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Views;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class FeedSettingsDraftTests
{
    private static Feed Feed() => new() { Id = 7, FeedUrl = "https://example.com/feed.xml", Title = "Example" };

    private static FeedSettingsDraft Dialog(Feed? feed = null) =>
        new(feed ?? Feed(), ReaderSettings.Defaults);

    [Fact]
    public void A_feed_with_no_overrides_opens_with_every_override_switched_off()
    {
        var dialog = Dialog();

        Assert.False(dialog.OverrideRefreshInterval);
        Assert.False(dialog.OverrideAutoDownload);
        Assert.False(dialog.OverrideFetchFullText);
        Assert.False(dialog.OverrideRetention);
    }

    [Fact]
    public void An_override_switched_off_is_saved_as_null_not_as_the_global_value()
    {
        var dialog = Dialog(Feed() with { RefreshIntervalMinutes = 15 });
        Assert.True(dialog.OverrideRefreshInterval);

        dialog.OverrideRefreshInterval = false;
        var applied = dialog.Apply();

        Assert.Null(applied.RefreshIntervalMinutes);
    }

    [Fact]
    public void An_override_switched_on_is_saved_as_a_value()
    {
        var dialog = Dialog();
        dialog.OverrideRefreshInterval = true;
        dialog.RefreshIntervalMinutes = 15;

        var applied = dialog.Apply();

        Assert.Equal(15, applied.RefreshIntervalMinutes);
    }

    [Fact]
    public void A_false_override_is_saved_as_false_and_not_mistaken_for_unset()
    {
        var dialog = Dialog();
        dialog.OverrideAutoDownload = true;
        dialog.AutoDownload = false;

        var applied = dialog.Apply();

        Assert.False(applied.AutoDownload);
        Assert.NotNull(dialog.Result.AutoDownload);
    }

    [Fact]
    public void The_inherited_value_is_shown_so_the_user_knows_what_they_are_overriding()
    {
        var globals = ReaderSettings.Defaults with { DefaultRefreshIntervalMinutes = 45 };
        var dialog = new FeedSettingsDraft(Feed(), globals);

        Assert.Contains("45", dialog.InheritedRefreshIntervalLabel);
    }

    [Fact]
    public void A_blank_title_override_is_saved_as_null_rather_than_an_empty_string()
    {
        var dialog = Dialog(Feed() with { TitleOverride = "My name" });
        dialog.TitleOverride = "   ";

        var applied = dialog.Apply();

        Assert.Null(applied.TitleOverride);
    }

    [Fact]
    public void Fetch_bookkeeping_is_carried_through_untouched()
    {
        var feed = Feed() with { ETag = "\"abc\"", ConsecutiveFailures = 3, LastError = "boom" };
        var dialog = Dialog(feed);

        var applied = dialog.Apply();

        Assert.Equal("\"abc\"", applied.ETag);
        Assert.Equal(3, dialog.Result.ConsecutiveFailures);
        Assert.Equal("boom", dialog.Result.LastError);
    }
}
```

The last test guards a real hazard. `FeedRepository.UpdateAsync` does not write the bookkeeping columns, so they cannot be clobbered in the database, but the dialog returning a `Feed` with those fields blanked would still be wrong if anyone later passed it somewhere that does write them.

- [ ] **Step 2: Write the dialog**

Create `LucidReader/Views/FeedSettingsDialog.axaml.cs`. The pattern for each of the four is a `CheckBox` bound to `OverrideX` gating an editor bound to `X`, with a label showing the inherited value.

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LucidReader.Core.Model;

namespace LucidReader.Views;

public partial class FeedSettingsDialog : Window, INotifyPropertyChanged
{
    private readonly Feed _feed;
    private readonly ReaderSettings _globals;

    public FeedSettingsDialog(Feed feed, ReaderSettings globals, IReadOnlyList<Folder> folders)
    {
        _feed = feed;
        _globals = globals;
        Folders = folders;

        InitializeComponent();
        DataContext = this;

        DisplayTitle = feed.Title ?? feed.FeedUrl;
        TitleOverride = feed.TitleOverride ?? string.Empty;
        FeedUrl = feed.FeedUrl;
        SelectedFolderId = feed.FolderId;
        IsEnabled2 = feed.IsEnabled;

        // A non-null column means the user set an override for this feed.
        OverrideRefreshInterval = feed.RefreshIntervalMinutes is not null;
        OverrideAutoDownload = feed.AutoDownload is not null;
        OverrideFetchFullText = feed.FetchFullText is not null;
        OverrideRetention = feed.RetentionDays is not null;

        // Editors start at the inherited value so switching an override on does
        // not jump to some unrelated number.
        RefreshIntervalMinutes = feed.RefreshIntervalMinutes ?? globals.DefaultRefreshIntervalMinutes;
        AutoDownload = feed.AutoDownload ?? globals.AutoDownloadArticles;
        FetchFullText = feed.FetchFullText ?? globals.FetchFullText;
        RetentionDays = feed.RetentionDays ?? globals.KeepReadArticlesDays;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public IReadOnlyList<Folder> Folders { get; }
    public string DisplayTitle { get; set; } = string.Empty;
    public string TitleOverride { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = string.Empty;
    public long? SelectedFolderId { get; set; }

    /// <summary>Named IsEnabled2 because Window already has IsEnabled.</summary>
    public bool IsEnabled2 { get; set; }

    public bool OverrideRefreshInterval { get; set; }
    public bool OverrideAutoDownload { get; set; }
    public bool OverrideFetchFullText { get; set; }
    public bool OverrideRetention { get; set; }

    public int RefreshIntervalMinutes { get; set; }
    public bool AutoDownload { get; set; }
    public bool FetchFullText { get; set; }
    public int RetentionDays { get; set; }

    public string InheritedRefreshIntervalLabel =>
        $"Use the global setting ({_globals.DefaultRefreshIntervalMinutes} minutes)";
    public string InheritedAutoDownloadLabel =>
        $"Use the global setting ({(_globals.AutoDownloadArticles ? "on" : "off")})";
    public string InheritedFetchFullTextLabel =>
        $"Use the global setting ({(_globals.FetchFullText ? "on" : "off")})";
    public string InheritedRetentionLabel =>
        $"Use the global setting ({_globals.KeepReadArticlesDays} days)";

    public Feed? Result { get; private set; }

    /// <summary>
    /// Null means inherit. An override switched off MUST write null rather than
    /// the global's present value, or the feed silently stops following future
    /// changes to that global.
    /// </summary>
    public void Accept()
    {
        var floor = (int)ReaderSettings.MinimumRefreshInterval.TotalMinutes;

        Result = _feed with
        {
            TitleOverride = string.IsNullOrWhiteSpace(TitleOverride) ? null : TitleOverride.Trim(),
            FolderId = SelectedFolderId,
            IsEnabled = IsEnabled2,
            RefreshIntervalMinutes = OverrideRefreshInterval
                ? Math.Max(floor, RefreshIntervalMinutes)
                : null,
            AutoDownload = OverrideAutoDownload ? AutoDownload : null,
            FetchFullText = OverrideFetchFullText ? FetchFullText : null,
            RetentionDays = OverrideRetention ? Math.Max(0, RetentionDays) : null
        };

        Close();
    }

    public void Cancel()
    {
        Result = null;
        Close();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

- [ ] **Step 3: Write the context menu partial**

Create `LucidReader/Views/MainWindow.FeedMenu.cs` with `ShowFeedSettingsAsync`, `RenameFeedAsync`, `MarkFeedReadAsync` and `UnsubscribeAsync`. Two behaviours to get right:

Enabling a feed must go through `FeedRepository.SetEnabledAsync(feedId, true)` and NOT through `UpdateAsync`. Only `SetEnabledAsync(true)` clears `consecutive_failures` and `auto_paused_utc`, and without that clearing an auto-paused feed is re-paused on its first subsequent failure.

Unsubscribing deletes items and tombstones by cascade and cannot be undone, so it must confirm first, naming the feed and how many articles go with it.

```csharp
using Avalonia.Controls;

namespace LucidReader.Views;

public partial class MainWindow
{
    public async Task ShowFeedSettingsAsync(long feedId)
    {
        var feed = await _services.Feeds.GetAsync(feedId);
        if (feed is null) return;

        var folders = await _services.Folders.GetAllAsync();
        var dialog = new FeedSettingsDialog(feed, _services.Settings, folders);
        await dialog.ShowDialog(this);

        if (dialog.Result is not { } updated) return;

        // Enabling has to go through SetEnabledAsync: it is the only path that
        // clears the failure count and the auto-pause stamp. UpdateAsync would
        // set is_enabled and leave the feed one failure away from pausing again.
        if (updated.IsEnabled && !feed.IsEnabled)
            await _services.Feeds.SetEnabledAsync(feedId, true);
        else if (!updated.IsEnabled && feed.IsEnabled)
            await _services.Feeds.SetEnabledAsync(feedId, false);

        await _services.Feeds.UpdateAsync(updated);
        await LoadFeedTreeAsync();
        StatusMessage = "Feed settings saved.";
    }

    public async Task MarkFeedReadAsync(long feedId)
    {
        await _services.Items.MarkFeedReadAsync(feedId);
        await LoadFeedTreeAsync();
        await LoadItemsAsync();
    }

    public async Task UnsubscribeAsync(long feedId)
    {
        var feed = await _services.Feeds.GetAsync(feedId);
        if (feed is null) return;

        var items = await _services.Items.QueryAsync(
            new LucidReader.Core.Storage.ItemQuery(feedId, null,
                LucidReader.Core.Storage.ItemFilter.All, 10000, 0));

        var confirm = new ConfirmDialog(
            "Unsubscribe",
            $"Remove \"{feed.DisplayTitle}\" and its {items.Count} stored articles? This cannot be undone.",
            "Unsubscribe");
        await confirm.ShowDialog(this);
        if (!confirm.Confirmed) return;

        await _services.Feeds.DeleteAsync(feedId);
        await LoadFeedTreeAsync();
        await LoadItemsAsync();
        StatusMessage = $"Unsubscribed from {feed.DisplayTitle}.";
    }
}
```

You will need a small `ConfirmDialog(string title, string message, string confirmLabel)` with a `bool Confirmed` property. Create it in `LucidReader/Views/ConfirmDialog.axaml`; it is reused by Task 14.

Add a `ContextMenu` to the feed tree's `DataTemplate` in `MainWindow.axaml` with Refresh, Mark all read, Feed settings and Unsubscribe, each bound to a command that passes the row's `FeedId`.

- [ ] **Step 4: Run the tests and commit**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FeedSettingsDraftTests 2>&1 | tail -5
git add LucidReader/Views LucidReader.Core.Tests/Ui/FeedSettingsDraftTests.cs
git commit -m "feat(reader): per-feed settings and feed context menu"
```

---

## Task 13: Adding feeds and OPML import and export

**Files:**
- Create: `LucidReader/Views/AddFeedDialog.axaml`, `.axaml.cs`
- Create: `LucidReader/Views/MainWindow.Subscriptions.cs`
- Modify: `LucidReader/Views/MainWindow.axaml` (menu entries for OPML)
- Test: `LucidReader.Core.Tests/Ui/AddFeedTests.cs`

**Interfaces:**
- Consumes: `FeedAutodiscovery` (Task 3), `OpmlService` (Task 4).
- Produces:
  - `AddFeedDialog(FeedAutodiscovery discovery, IReadOnlyList<Folder> folders)` with `IReadOnlyList<DiscoveredFeed> Selected { get; }` and `long? SelectedFolderId { get; }`.
  - On `MainWindow`: `Task ShowAddFeedDialogAsync()`, `Task ImportOpmlAsync()`, `Task ExportOpmlAsync()`.

The user pastes either a feed URL or a site URL. Autodiscovery resolves it, and when a site offers several feeds the dialog asks which. A URL already subscribed is reported rather than silently duplicated.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Ui/AddFeedTests.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Core.Opml;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class AddFeedTests : IAsyncLifetime
{
    private string _dir = string.Empty;
    private ReaderServices _services = null!;

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lucidreader-uitests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _services = await ReaderServices.StartAsync(
            Path.Combine(_dir, "reader.db"), Path.Combine(_dir, "settings.json"));
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task Adding_a_feed_puts_it_in_the_tree()
    {
        await _services.Feeds.AddAsync(new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            Title = "Example"
        });

        var feeds = await _services.Feeds.GetAllAsync();

        Assert.Single(feeds);
        Assert.Equal("Example", feeds[0].DisplayTitle);
    }

    [Fact]
    public async Task Adding_a_url_that_is_already_subscribed_is_rejected_by_the_unique_index()
    {
        await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _services.Feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" }));
    }

    [Fact]
    public async Task Importing_opml_creates_folders_and_feeds()
    {
        var service = new OpmlService(_services.Folders, _services.Feeds);
        const string opml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <opml version="2.0">
              <head><title>Subscriptions</title></head>
              <body>
                <outline text="News">
                  <outline text="World" type="rss" xmlUrl="https://news.example/world.xml"/>
                </outline>
                <outline text="Loose" type="rss" xmlUrl="https://loose.example/feed.xml"/>
              </body>
            </opml>
            """;

        var result = await service.ImportAsync(opml);

        Assert.Equal(1, result.FoldersCreated);
        Assert.Equal(2, result.FeedsAdded);
        Assert.Equal(2, (await _services.Feeds.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Exporting_then_reimporting_elsewhere_reproduces_the_subscriptions()
    {
        var folder = await _services.Folders.AddAsync("Tech");
        await _services.Feeds.AddAsync(new Feed
        {
            FeedUrl = "https://a.example/feed.xml", Title = "A", FolderId = folder
        });
        await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://b.example/feed.xml", Title = "B" });

        var service = new OpmlService(_services.Folders, _services.Feeds);
        var exported = await service.ExportAsync(DateTimeOffset.Parse("2026-08-29T10:00:00Z"));

        Assert.Contains("https://a.example/feed.xml", exported);
        Assert.Contains("Tech", exported);
        Assert.Contains("https://b.example/feed.xml", exported);
    }
}
```

- [ ] **Step 2: Write the add-feed dialog**

Create `LucidReader/Views/AddFeedDialog.axaml` with a URL box, a Find button, a results list with checkboxes, a folder picker, and Add and Cancel. Name the controls `FeedUrlBox`, `FindButton`, `DiscoveredList`, `FolderCombo`, `AddButton`, `CancelButton`.

Create `LucidReader/Views/AddFeedDialog.axaml.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;

namespace LucidReader.Views;

public sealed class DiscoveredFeedChoice
{
    public required DiscoveredFeed Feed { get; init; }
    public bool IsSelected { get; set; } = true;
    public string Label => string.IsNullOrWhiteSpace(Feed.Title) ? Feed.FeedUrl : $"{Feed.Title}  ({Feed.FeedUrl})";
}

public partial class AddFeedDialog : Window, INotifyPropertyChanged
{
    private readonly FeedAutodiscovery _discovery;
    private string _url = string.Empty;
    private string _status = string.Empty;
    private bool _isSearching;

    public AddFeedDialog(FeedAutodiscovery discovery, IReadOnlyList<Folder> folders)
    {
        _discovery = discovery;
        Folders = folders;

        InitializeComponent();
        DataContext = this;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public IReadOnlyList<Folder> Folders { get; }
    public ObservableCollection<DiscoveredFeedChoice> Discovered { get; } = [];
    public long? SelectedFolderId { get; set; }

    public string Url
    {
        get => _url;
        set { if (_url == value) return; _url = value; Raise(); }
    }

    public string Status
    {
        get => _status;
        private set { if (_status == value) return; _status = value; Raise(); }
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set { if (_isSearching == value) return; _isSearching = value; Raise(); }
    }

    public IReadOnlyList<DiscoveredFeed> Selected { get; private set; } = [];

    public async Task FindAsync()
    {
        var input = Url.Trim();
        if (input.Length == 0)
        {
            Status = "Enter the address of a feed or a website.";
            return;
        }

        // Autodiscovery only follows http and https. Give a useful message
        // rather than an empty result when the user pasted something else.
        if (!input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            input = "https://" + input;
            Url = input;
        }

        IsSearching = true;
        Status = "Looking for feeds...";
        Discovered.Clear();

        try
        {
            var found = await _discovery.DiscoverAsync(input);

            foreach (var feed in found)
                Discovered.Add(new DiscoveredFeedChoice { Feed = feed });

            Status = found.Count switch
            {
                0 => "No feeds found at that address.",
                1 => "Found one feed.",
                _ => $"Found {found.Count} feeds. Choose the ones you want."
            };
        }
        finally
        {
            IsSearching = false;
        }
    }

    public void Accept()
    {
        Selected = Discovered.Where(d => d.IsSelected).Select(d => d.Feed).ToList();
        Close();
    }

    public void Cancel()
    {
        Selected = [];
        Close();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

- [ ] **Step 3: Write the subscriptions partial**

Create `LucidReader/Views/MainWindow.Subscriptions.cs`:

```csharp
using Avalonia.Platform.Storage;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Opml;

namespace LucidReader.Views;

public partial class MainWindow
{
    public async Task ShowAddFeedDialogAsync()
    {
        var folders = await _services.Folders.GetAllAsync();
        var dialog = new AddFeedDialog(new FeedAutodiscovery(_services.Http), folders);
        await dialog.ShowDialog(this);

        if (dialog.Selected.Count == 0) return;

        var added = 0;
        var skipped = 0;

        foreach (var discovered in dialog.Selected)
        {
            if (await _services.Feeds.GetByUrlAsync(discovered.FeedUrl) is not null)
            {
                skipped++;
                continue;
            }

            var id = await _services.Feeds.AddAsync(new Feed
            {
                FeedUrl = discovered.FeedUrl,
                Title = discovered.Title,
                FolderId = dialog.SelectedFolderId
            });
            added++;

            // Fetch straight away so the feed is not empty until the next tick.
            _services.Refresh.TryQueue(id, isManual: true);
        }

        await LoadFeedTreeAsync();
        StatusMessage = skipped == 0
            ? $"Added {added} feeds."
            : $"Added {added} feeds, skipped {skipped} already subscribed.";
    }

    public async Task ImportOpmlAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import subscriptions from OPML",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("OPML") { Patterns = ["*.opml", "*.xml"] }
            ]
        });

        if (files.Count == 0) return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var opml = await reader.ReadToEndAsync();

            var service = new OpmlService(_services.Folders, _services.Feeds);
            var result = await service.ImportAsync(opml);

            await LoadFeedTreeAsync();
            StatusMessage =
                $"Imported {result.FeedsAdded} feeds into {result.FoldersCreated} new folders" +
                (result.FeedsSkipped > 0 ? $", skipped {result.FeedsSkipped} already subscribed." : ".");

            // Nothing has been fetched yet, so pull everything once.
            foreach (var feed in await _services.Feeds.GetAllAsync())
                if (feed.IsEnabled && feed.LastSuccessUtc is null)
                    _services.Refresh.TryQueue(feed.Id, isManual: true);
        }
        catch (OpmlParseException ex)
        {
            StatusMessage = "That file is not a readable OPML export: " + ex.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not import: " + ex.Message;
        }
    }

    public async Task ExportOpmlAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export subscriptions as OPML",
            SuggestedFileName = "lucidreader-subscriptions.opml",
            DefaultExtension = "opml",
            FileTypeChoices = [new FilePickerFileType("OPML") { Patterns = ["*.opml"] }]
        });

        if (file is null) return;

        try
        {
            var service = new OpmlService(_services.Folders, _services.Feeds);
            var opml = await service.ExportAsync(DateTimeOffset.UtcNow);

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(opml);

            StatusMessage = "Subscriptions exported to " + file.Name;
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not export: " + ex.Message;
        }
    }
}
```

`ReaderServices` needs to expose its `HttpClient` for the autodiscovery instance. Add an `internal HttpClient Http => _http;` property rather than constructing a second client, so the reader keeps one connection pool.

- [ ] **Step 4: Run the tests and commit**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter AddFeedTests 2>&1 | tail -5
git add LucidReader/Views LucidReader/ReaderServices.cs LucidReader.Core.Tests/Ui/AddFeedTests.cs
git commit -m "feat(reader): add feeds by URL, OPML import and export"
```

---

## Task 14: Refresh health and auto-paused feeds

**Files:**
- Create: `LucidReader/Views/MainWindow.Health.cs`
- Test: `LucidReader.Core.Tests/Ui/HealthTests.cs`

**Interfaces:**
- Consumes: `RefreshScheduler.LastTickError`, `RefreshScheduler.ConsecutiveTickFailures`, `RefreshScheduler.IsRunning`, `Feed.AutoPausedUtc`, `FeedRepository.SetEnabledAsync`.
- Produces, on `MainWindow`: `Task CheckHealthAsync()`, `internal static string DescribeHealth(bool isRunning, string? lastTickError, int consecutiveFailures, int autoPausedCount)`, `Task ResumeFeedAsync(long feedId)`.

**Why this task exists.** Plan 1 added `LastTickError` and `ConsecutiveTickFailures` precisely so the app could say "background refresh is failing" rather than appearing to work while doing nothing. `IsRunning` returning true proves only that a timer exists. Nothing reads those two properties yet, and an auto-paused feed currently has no way back through the UI.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Ui/HealthTests.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Views;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class HealthTests
{
    [Fact]
    public void A_healthy_scheduler_reports_nothing()
    {
        Assert.Equal(string.Empty, MainWindow.DescribeHealth(true, null, 0, 0));
    }

    [Fact]
    public void A_stopped_scheduler_is_reported()
    {
        var text = MainWindow.DescribeHealth(false, null, 0, 0);

        Assert.Contains("not running", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_scheduler_that_is_running_but_failing_every_tick_is_reported_as_failing()
    {
        var text = MainWindow.DescribeHealth(true, "database is locked", 5, 0);

        Assert.Contains("failing", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("database is locked", text);
    }

    [Fact]
    public void One_isolated_tick_failure_is_not_shouted_about()
    {
        // A single blip is not worth alarming the user; a sustained streak is.
        Assert.Equal(string.Empty, MainWindow.DescribeHealth(true, "transient", 1, 0));
    }

    [Fact]
    public void Auto_paused_feeds_are_reported_with_a_count()
    {
        var text = MainWindow.DescribeHealth(true, null, 0, 3);

        Assert.Contains("3", text);
        Assert.Contains("paused", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void One_auto_paused_feed_reads_in_the_singular()
    {
        var text = MainWindow.DescribeHealth(true, null, 0, 1);

        Assert.Contains("1 feed", text);
        Assert.DoesNotContain("1 feeds", text);
    }

    [Fact]
    public void Both_problems_at_once_are_both_reported()
    {
        var text = MainWindow.DescribeHealth(true, "boom", 4, 2);

        Assert.Contains("failing", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("paused", text, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Write the health partial**

Create `LucidReader/Views/MainWindow.Health.cs`:

```csharp
using Avalonia.Threading;

namespace LucidReader.Views;

public partial class MainWindow
{
    private DispatcherTimer? _healthTimer;

    /// <summary>
    /// How many consecutive failing ticks before the user is told. One blip is
    /// noise; a streak means background refresh has genuinely stopped working.
    /// </summary>
    private const int TickFailureThreshold = 3;

    internal static string DescribeHealth(
        bool isRunning,
        string? lastTickError,
        int consecutiveFailures,
        int autoPausedCount)
    {
        var parts = new List<string>();

        if (!isRunning)
        {
            parts.Add("Background refresh is not running.");
        }
        else if (consecutiveFailures >= TickFailureThreshold)
        {
            // IsRunning being true says only that a timer exists. This is the
            // case Plan 1 added the counters for: the loop is alive and every
            // tick is throwing, so nothing is actually being refreshed.
            parts.Add($"Background refresh is failing ({consecutiveFailures} attempts): {lastTickError}");
        }

        if (autoPausedCount == 1)
            parts.Add("1 feed was paused after repeated failures.");
        else if (autoPausedCount > 1)
            parts.Add($"{autoPausedCount} feeds were paused after repeated failures.");

        return string.Join("  ", parts);
    }

    private void StartHealthMonitoring()
    {
        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _healthTimer.Tick += async (_, _) => await CheckHealthAsync();
        _healthTimer.Start();
    }

    public async Task CheckHealthAsync()
    {
        var pausedCount = (await _services.Feeds.GetAllAsync())
            .Count(f => f.AutoPausedUtc is not null);

        var text = DescribeHealth(
            _services.Scheduler.IsRunning,
            _services.Scheduler.LastTickError,
            _services.Scheduler.ConsecutiveTickFailures,
            pausedCount);

        // Do not stamp over a message the user's own action just produced.
        if (text.Length > 0) StatusMessage = text;
    }

    /// <summary>
    /// Puts an auto-paused feed back into rotation. Must go through
    /// SetEnabledAsync, which clears the failure count and the pause stamp:
    /// re-enabling without clearing them means the very next failure pauses
    /// the feed again, so the user gets exactly one attempt.
    /// </summary>
    public async Task ResumeFeedAsync(long feedId)
    {
        await _services.Feeds.SetEnabledAsync(feedId, true);
        _services.Refresh.TryQueue(feedId, isManual: true);
        await LoadFeedTreeAsync();
        StatusMessage = "Feed resumed.";
    }
}
```

Call `StartHealthMonitoring()` from `OnOpenedAsync`, and stop the timer in the window's `Closing` handler. Add a "Resume" entry to the feed context menu, visible only when `IsAutoPaused` is true on the node.

- [ ] **Step 3: Run the tests and commit**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter HealthTests 2>&1 | tail -5
git add LucidReader/Views LucidReader.Core.Tests/Ui/HealthTests.cs
git commit -m "feat(reader): surface refresh health and let paused feeds resume"
```

---

## Task 15: UI driving scripts

**Files:**
- Create: `ux-scripts/reader-smoke.yaml`
- Create: `ux-scripts/reader-settings.yaml`
- Create: `ux-scripts/README-reader.md`

lucidVIEW drives its UI with YAML scripts run through the Debug-only harness, and generates its user manual screenshots the same way. lucidREADER gets the same treatment. These are not a substitute for the unit tests; they catch the things unit tests cannot, such as a binding that silently fails or a control that never appears.

- [ ] **Step 1: Confirm how the harness is invoked**

lucidVIEW runs scripts like this:

```bash
dotnet run --project MarkdownViewer/MarkdownViewer.csproj -- \
    --ux-test --script ux-scripts/capture-manual.yaml \
    --output MarkdownViewer/Assets/manual/screenshots
```

Confirm the same arguments work for `LucidReader` given `UseUITesting` is wired in Task 1, and check the available action types by reading an existing script such as `ux-scripts/smoke-all-functions.yaml`. Actions seen there include `Wait`, `Screenshot`, `PressKey`, `TypeText` with a `target`, and `Click`. Use only action types you have confirmed exist rather than inventing them.

- [ ] **Step 2: Write the smoke script**

Create `ux-scripts/reader-smoke.yaml`:

```yaml
# lucidREADER smoke script.
#
# Walks the three panes, adds a feed, exercises the keyboard, and captures a
# screenshot at each step. Run:
#   dotnet run --project LucidReader/LucidReader.csproj -- \
#       --ux-test --script ux-scripts/reader-smoke.yaml --output ux-results/reader-smoke

name: reader-smoke
description: Open the reader, add a feed, navigate items and read one
default_delay: 350

actions:
  - type: Wait
    value: "1500"
    description: Startup grace period while the engine opens the database
  - type: Screenshot
    value: 01-empty-shell
    description: Three panes with no subscriptions yet

  - type: Click
    target: AddFeedButton
  - type: Wait
    value: "400"
  - type: TypeText
    target: FeedUrlBox
    value: "https://www.mostlylucid.net/rss"
  - type: Screenshot
    value: 02-add-feed-dialog
  - type: Click
    target: FindButton
  - type: Wait
    value: "2500"
    description: Autodiscovery plus the first fetch
  - type: Screenshot
    value: 03-discovered
  - type: Click
    target: AddButton
  - type: Wait
    value: "3000"
  - type: Screenshot
    value: 04-feed-added

  - type: PressKey
    value: J
  - type: Wait
    value: "600"
  - type: Screenshot
    value: 05-first-article
  - type: PressKey
    value: J
  - type: PressKey
    value: J
  - type: Wait
    value: "1200"
    description: Long enough for the mark-as-read dwell to fire
  - type: Screenshot
    value: 06-scanned-items

  - type: PressKey
    value: S
  - type: Wait
    value: "300"
  - type: Screenshot
    value: 07-starred

  - type: TypeText
    target: SearchBox
    value: "the"
  - type: Wait
    value: "800"
  - type: Screenshot
    value: 08-search-results
```

- [ ] **Step 3: Write the settings script**

Create `ux-scripts/reader-settings.yaml` driving the settings dialog and the per-feed dialog, capturing each tab. Follow the same shape, targeting the control names from Tasks 11 and 12.

- [ ] **Step 4: Run both scripts and check the screenshots**

```bash
dotnet run --project LucidReader/LucidReader.csproj -- --ux-test --script ux-scripts/reader-smoke.yaml --output ux-results/reader-smoke 2>&1 | tail -20
```

Then LOOK at the produced images. A script that runs green while every pane is empty proves nothing. Confirm the feed tree has rows, the item list has articles, and the reading pane shows text. Report what you saw, not just that the command exited zero.

If the network is unavailable in your environment, say so and note which steps could not be verified rather than reporting success.

- [ ] **Step 5: Write the README and commit**

Create `ux-scripts/README-reader.md` explaining what each script covers and how to run it, following the existing `ux-scripts/README.md` in tone.

```bash
git add ux-scripts
git commit -m "test(reader): UI driving scripts"
```

---

## Done

At this point lucidREADER is a usable application: subscribe by URL or OPML, refresh on a schedule with visible health, read articles offline, search, star, tag, export, and configure everything globally or per feed.

**Plan 3 covers:** packaging as a single-file ReadyToRun binary per platform, the macOS `.app` bundle with correctly signed native SQLite libraries and notarisation, the FTS5 startup probe in the shipped app, the FULL StyloExtract binding, and the network-availability observation that `ReaderSettings.PauseWhenOffline` implies.

## Deferred from this plan, deliberately

- **PDF export.** Recorded as a decision at the top of this plan. Markdown export only in v1.
- **Multi-level folder nesting.** The schema supports one level; OPML import flattens deeper trees onto the outermost folder.
- **Honouring a 429 `Retry-After`.** `FeedFetchResult.Failed` carries it; `BackoffPolicy` still ignores it.
- **FTS5 phrase and prefix search.** Every term is quoted as a literal, which is what makes arbitrary input safe. Revisit if users ask for it.
- **Rebuilding the coordinators when concurrency settings change.** The settings dialog tells the user the change applies at next launch.
- **`Volatile` access on `LastTickError` and `ConsecutiveTickFailures`.** They are polled every 30 seconds, not in a tight loop, so the current plain reads are adequate.
