# mylo Design

**Date:** 2026-08-28
**Status:** Approved design, ready for implementation planning

**Name:** the product was called lucidREADER while this design and the two
implementation plans were written, and is now called mylo, always lowercase.
The prose here uses the new name. The C# namespaces, project folders and type
names still say `LucidReader`; that is internal and was left alone. The plan
documents keep the old name inside their code listings, because those are a
record of what was written at the time.

A native RSS/Atom reader built on the lucidVIEW rendering stack. Same visual
language, same markdown render control, same themes. Feeds are fetched on a
schedule, articles are converted to markdown once at fetch time and stored
locally, so reading is fully offline.

---

## 1. Goals and non-goals

### Goals

- Subscribe to RSS 2.0, RSS 1.0/RDF and Atom feeds, organised in folders.
- Background refresh with per-feed and global intervals, plus manual refresh.
- Automatic offline download of article content, converted to markdown.
- Full-text fetch when a feed publishes only a stub.
- Read/unread, starred, and user tags on items.
- Global settings and per-feed overrides.
- OPML import and export.
- Full-text search across all stored articles.
- Export and print an article, reusing lucidVIEW's PDF and print services.

### Non-goals for v1

- Syncing with hosted services (Feedly, Inoreader, Google Reader API).
- Multi-level folder nesting beyond one level.
- Podcast/enclosure playback.
- Shared or multi-user state.

---

## 2. Project structure

mylo lives in the existing `lucidview` repository as sibling projects,
reusing the shared libraries directly via `ProjectReference`.

```
Mostlylucid.LucidView.Markdown/     existing: render control, MarkdownService,
                                    ImageCacheService, Naiad diagrams
Mostlylucid.LucidView.Content/      NEW shared: IHtmlToMarkdownService,
                                    AngleSharp implementation, HtmlPreProcessor,
                                    UserAgent
Mostlylucid.LucidView.Shell/        NEW shared: ThemeService, PdfExportService,
                                    PrintService
LucidReader.Core/                   NEW: feeds, storage, sync, offline pipeline.
                                    No UI dependency.
LucidReader/                        NEW: lean Avalonia app
LucidReader.Full/                   NEW: adds StyloExtract/Playwright/LLM
                                    extraction via #if FULL
LucidReader.Core.Tests/             NEW
LucidReader.Tests/                  NEW: UI tests via Mostlylucid.Avalonia.UITesting
```

### 2.1 The extraction refactor

`HtmlToMarkdownService`, `HtmlPreProcessor`, `UserAgent`, `ThemeService`,
`PdfExportService` and `PrintService` currently live inside the `MarkdownViewer`
project. They move into the two new shared libraries, and `MarkdownViewer`
references those libraries instead.

Constraints on the move:

- The `IHtmlToMarkdownService` interface moves with the lean implementation, so
  `MarkdownViewer.Full`'s existing `#if FULL` substitution of
  `HtmlToMarkdownServiceFull` keeps working unchanged.
- `MarkdownViewer.Full` compiles `MarkdownViewer`'s sources by file-link glob.
  Files removed from `MarkdownViewer` therefore disappear from FULL too, and
  FULL must gain the same `ProjectReference` set.
- lucidVIEW's existing test suites must pass unchanged after the move. This is
  the acceptance criterion for the refactor.
- Nothing beyond what mylo needs is moved. No opportunistic refactoring.

### 2.2 Build and publish profile

mylo does **not** target AOT. It publishes:

- `SelfContained=true`
- `PublishSingleFile=true`
- `PublishReadyToRun=true`
- `PublishTrimmed=false`
- `IncludeNativeLibrariesForSelfExtract` explicitly **false** (see section 7.1)

lucidVIEW's lean build keeps its existing small, AOT-capable profile. The
shared libraries must not acquire dependencies that break it.

---

## 3. Data model and storage

SQLite via `Microsoft.Data.Sqlite`, hand-written SQL, and a small forward-only
migration runner keyed on a `schema_version` pragma. Not EF Core: the schema is
small, and EF's startup and reflection cost buys nothing here.

Database file sits at the platform application-data path beside `settings.json`
(`~/Library/Application Support/mylo/reader.db` on macOS, equivalents
elsewhere). WAL mode enabled.

### 3.1 Tables

**Folders**
`id`, `name`, `sort_order`, `parent_id` (nullable, one level of nesting only).

**Feeds**
`id`, `folder_id`, `feed_url` (unique), `site_url`, `title`, `title_override`,
`icon_path`, `is_enabled`, `last_fetched_utc`, `last_success_utc`, `etag`,
`last_modified`, `consecutive_failures`, `last_error`, `next_due_utc`, and the
nullable per-feed overrides: `refresh_interval_minutes`, `auto_download`,
`fetch_full_text`, `retention_days`.

Null override columns mean "inherit the global value". Changing a global
default therefore moves every non-overridden feed with it.

**Items**
`id`, `feed_id`, `guid`, `link`, `title`, `author`, `published_utc`,
`updated_utc`, `summary`, `content_markdown`, `content_source`
(`feed` | `extracted`), `is_read`, `is_starred`, `first_seen_utc`,
`offline_state` (`none` | `pending` | `downloaded` | `failed`),
`offline_error`.

Unique index on `(feed_id, guid)`. `guid` is the feed's `<guid>` or Atom `<id>`;
where absent or observed to be unstable, a hash of the item link is used
instead. Re-fetching updates in place rather than duplicating.

**Tags** and **ItemTags**
Many-to-many user tags on items.

**ItemsFts**
FTS5 virtual table over item title and `content_markdown`, kept in sync by
insert/update/delete triggers.

### 3.2 Content is stored as markdown

Feed HTML is converted to markdown once, at fetch time. The reading pane hands
the stored string straight to the shared render control, so offline reading
requires no conversion and no network. Images referenced by the markdown are
pulled into the existing `ImageCacheService` directory and rewritten to local
paths.

### 3.3 Conditional fetch state

`etag` and `last_modified` are stored and sent as `If-None-Match` and
`If-Modified-Since` on every fetch. A 304 short-circuits parsing, storage and
download entirely.

---

## 4. Fetch, refresh and offline download

Three UI-free components in `LucidReader.Core`, built on the
`Mostlylucid.Ephemeral` primitives (NuGet, 3.0.0).

### 4.1 FeedFetcher

One conditional HTTP GET, then parse. `System.ServiceModel.Syndication` is the
primary parser, with a lenient hand-rolled fallback covering what it handles
badly: `content:encoded`, Dublin Core dates, and malformed XML. Returns parsed
items; performs no database writes.

Partial success counts: if 18 of 20 items parse, the 18 are kept.

### 4.2 Refresh coordination

A long-lived `EphemeralWorkCoordinator<FeedRefreshRequest>` owns feed fetching.

- Bounded concurrency, default 4, configurable.
- `maxBodyDuration` of 60 seconds per feed fetch. This is required by the
  coordinator's constructor and is deliberate: a server that accepts a
  connection then stalls forever would otherwise hold its slot permanently.
- Coalescing on feed id: a manual refresh of a feed already queued or in flight
  is dropped, not duplicated.
- `PendingCount`, `ActiveCount` and `TotalFailed` drive the status bar.
- Pause/resume used when the machine goes offline.

Feeds become due via a plain one-minute timer running a single SQL query over
`next_due_utc`, feeding the coordinator. `Mostlylucid.Ephemeral.Atoms.ScheduledTasks`
is deliberately not used: the scheduling rule is one query and the atom would
add a dependency for no gain.

Backoff on failure uses `Mostlylucid.Ephemeral.Atoms.Retry`, exponential and
capped at a few hours, driven by `consecutive_failures`.

### 4.3 OfflineDownloader

A **second** coordinator, with its own lower concurrency and a
`maxBodyDuration` of 180 seconds. Two coordinators rather than one shared
queue, because feed fetch and article extraction have different duration
profiles and different failure semantics; a burst of 200 new items must not
starve feed refresh.

Per new item, when auto-download is enabled for its feed:

1. Decide whether the feed-supplied content is a stub, using content length and
   a trailing "read more" style link heuristic.
2. If it is **not** a stub, or full-text fetch is disabled, convert the
   feed-supplied HTML to markdown and set `content_source = feed`.
3. If it **is** a stub and full-text fetch is enabled, fetch the item link and
   run the page through `IHtmlToMarkdownService`, setting
   `content_source = extracted`. Lean binds the AngleSharp implementation; FULL
   binds the StyloExtract one via `#if FULL`.
4. Either way, pull referenced images into `ImageCacheService` and rewrite to
   local paths.
5. Store the markdown and set `offline_state = downloaded`.

Items whose feed has auto-download disabled keep their feed summary only, with
`offline_state = none`, and can be fetched on demand from the reading pane.

Failure sets `offline_state = failed`, records the error, and leaves the feed
summary in place. A failed extraction degrades to a readable stub, never an
empty pane.

### 4.4 SQLite writes

All writes go through `Mostlylucid.Ephemeral.Sqlite.SingleWriter`. With two
coordinators writing concurrently, SQLite's writer lock is the obvious
contention point; single-writer serialisation removes a class of `SQLITE_BUSY`
failures before they are written. Reads stay direct and concurrent under WAL.

---

## 5. User interface

Avalonia 11.3 with FluentAvaloniaUI, FluentIcons and the Raleway asset, using
the shared `ThemeService` and its seven themes. A lucidVIEW user should
recognise mylo immediately.

### 5.1 Main window

Three-column grid with draggable splitters; column widths persisted.

**Left, feed tree.** Pinned smart rows at top: All items, Unread, Starred.
Below, folders with feeds nested one level. Each row shows title and unread
count, bold when unread. A warning glyph appears when `consecutive_failures` is
above zero, with `last_error` in the tooltip. Context menu: rename, move to
folder, feed settings, refresh, unsubscribe.

**Middle, item list.** Virtualised. Title, feed name, relative date; unread in
bold; star glyph toggled inline. Newest first. Filter chips for All / Unread /
Starred. Selecting an item marks it read after a short configurable dwell, not
instantly, so arrowing through a list does not mark everything read behind you.

**Right, reading pane.** The shared markdown render control showing
`content_markdown`. Header block with title, author, date, source feed and a
link to the original. A badge shows whether the reader is seeing extracted full
text, feed content, or a failed-extraction stub; in the last case a "fetch full
article" button retries. Naiad diagram rendering and image caching come along
unchanged because it is the same control.

### 5.2 Keyboard

Follows established reader conventions rather than inventing new ones.

| Key | Action |
|---|---|
| `J` / `K` | Next / previous item |
| `N` / `P` | Next / previous unread |
| `M` | Toggle read |
| `S` | Toggle star |
| `R` | Refresh current feed |
| `Shift+R` | Refresh all feeds |
| `O` | Open original in external browser |
| `/` | Focus search |
| `Ctrl+F` | Search within current article |
| `Space` | Page reading pane, then jump to next unread |
| `F1` | User manual |

### 5.3 Search

A single search box queries the FTS5 table over title and article text across
all feeds. Results render in the middle column as a synthetic item list.

### 5.4 Export and print

The current article is exported to PDF or printed through the shared
`PdfExportService` and `PrintService`, and can be exported as markdown.

---

## 6. Settings

Two levels, presented in the `SettingsDialog` style lucidVIEW already uses.
Global values live in `settings.json` beside the database.

### 6.1 Global

**Updates**: default refresh interval, refresh on startup, pause refresh when
offline, max concurrent fetches.

**Offline**: auto-download articles, fetch full text when content looks
truncated, cache images, max image size.

**Retention**: keep read articles for N days, keep unread indefinitely or for
N days, max articles per feed, never delete starred. Plus manual "clean up now"
and "clear image cache" actions, each with a current size readout. A retention
setting whose effect is invisible is a setting nobody trusts.

**Reading**: theme, font size, column width, mark-as-read dwell delay, open
links in external browser.

### 6.2 Per-feed

Reached from the feed context menu. Every inheritable field renders as "use
global (current value)" with an explicit override toggle, showing the inherited
value inline so it is clear what is being overridden. Overrides: refresh
interval, auto-download, full-text fetch, retention.

Feed-specific fields with no global equivalent: display title override, folder,
and enable/disable.

### 6.3 Feed management

- Add feed by URL, with autodiscovery of the feed URL from a site URL.
- OPML import: folder structure preserved, duplicates detected on feed URL.
- OPML export.

---

## 7. Platform and packaging constraints

### 7.1 SQLite native library on macOS single-file

`Microsoft.Data.Sqlite` depends on `SQLitePCLRaw.bundle_e_sqlite3`, which needs
the native `e_sqlite3` library at runtime. This is a hard constraint, not a
preference:

- `IncludeNativeLibrariesForSelfExtract` must stay **false**. When true, the
  bundle extracts `e_sqlite3.dylib` to a temp directory at runtime, and the
  macOS hardened runtime refuses to load an unsigned dylib from there. The app
  dies at first database access, which is startup, and only on a notarised
  build. It passes every local test and fails for every user.
- .NET's single-file default already leaves native libraries beside the
  executable. Keep that. Inside the macOS `.app` they sit in `Contents/MacOS`,
  where `codesign` expects them.
- The macOS packaging script must sign inner binaries before signing the outer
  bundle. Wrong order is the second most common way this breaks.
- Startup runs an FTS5 probe query and fails loudly with a clear message if
  unavailable, rather than failing at the first search.
- Verification happens on a real notarised build, not `dotnet run`. Windows and
  Linux get the equivalent native-load check.

Fallback if the signed-dylib path proves unworkable:
`SQLitePCLRaw.bundle_sqlite3` against the system SQLite, removing the native
asset entirely. Not preferred, because it makes the SQLite version
platform-dependent.

---

## 8. Error handling

A reader talks to many servers it does not control, and most are broken in some
way. The rule: nothing a remote server does can take down the app or lose local
state. Transient remote problems degrade quietly and visibly; local data
problems shout.

| Failure | Behaviour |
|---|---|
| Network / HTTP error | Increment `consecutive_failures`, store message, back off. Feed stays visible with a warning glyph; existing items intact. At 20 consecutive failures the feed is auto-paused with a prompt rather than hammering a dead host. |
| Malformed feed XML | Fall back to the lenient parser. If that fails too, keep the raw response for diagnostics and mark a parse failure. Partial parses are accepted. |
| Extraction failure | `offline_state = failed`; feed summary retained; reading pane offers retry. Never an empty pane. |
| Coordinator body timeout | Logged per feed, counted as a failure. |
| Database write failure | Surfaced to the user, never swallowed. |
| Schema newer than app | Migration runner refuses to open the database rather than guessing. |

---

## 9. Testing

Tests are written first, particularly for parsing and storage, where fixtures
define the requirement.

**`LucidReader.Core.Tests`** carries the bulk, with no UI dependency:

- *Feed parsing* against a corpus of real-world feed files as fixtures,
  including malformed ones, since that is where the bugs live.
- *Storage* against a temp database file: migrations, dedupe on republished
  items, unstable guid fallback, retention pruning, FTS trigger sync.
- *Scheduling* using `TimeProvider` fakes, which the coordinator accepts:
  backoff, coalescing, concurrency limits, all without real waiting.
- *HTTP* through a stub message handler. No test touches the network.

**`LucidReader.Tests`** drives the real window through
`Mostlylucid.Avalonia.UITesting` (Debug-wired, as in lucidVIEW): three-pane
flow, keyboard navigation, settings dialogs, with screenshots. UI behaviour is
verified through this path rather than asserted.

**Extraction** is tested against saved HTML fixtures in both the lean AngleSharp
binding and the FULL StyloExtract binding, holding both implementations to the
same expectations.

**lucidVIEW regression**: the existing lucidVIEW suites must pass unchanged
after the section 2.1 refactor.

---

## 10. Build order

1. Extract `Mostlylucid.LucidView.Content` and `Mostlylucid.LucidView.Shell`;
   rewire `MarkdownViewer` and `MarkdownViewer.Full`; prove lucidVIEW's tests
   still pass.
2. `LucidReader.Core`: storage, migrations, model.
3. `LucidReader.Core`: feed parsing against the fixture corpus.
4. `LucidReader.Core`: fetch, refresh coordination, backoff.
5. `LucidReader.Core`: offline download and image caching.
6. `LucidReader`: shell, three-pane window, reading pane.
7. `LucidReader`: item actions, keyboard, search.
8. `LucidReader`: settings dialogs, global and per-feed.
9. OPML import and export.
10. Retention and cleanup.
11. Packaging: single-file R2R per platform, macOS `.app` with signed native
    libraries, notarisation check.
12. `LucidReader.Full`: StyloExtract binding.
