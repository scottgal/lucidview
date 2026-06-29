# lucidLAB - Design

**Status:** Design in review 2026-06-29. Awaiting user approval, then implementation plan.
**Lean app:** `MarkdownViewer/` (unchanged, AOT-conscious, single-file Release, byte-identical contract).
**Replaces:** `MarkdownViewer.Full/` and `MarkdownViewer.Full.Tests/` (removed in the same commit lucidLAB lands).
**New sibling:** `MarkdownViewer.Lab/` (dogfood-only, Debug-only, not shipped in releases).

## Why

lucidLAB is the testbed for the lucidRAG + lucidSupport infrastructure that will land in StyloBot. lucidVIEW-FULL today exercises the StyloExtract preview stack and nothing else. lucidLAB scales that contract to the whole lucidRAG storage / retrieval / synthesis / GraphRag surface plus the lucidSupport learn/serve pattern applied to documents instead of web pages.

The primary contract is **every infrastructure piece visible and inspectable in the UI**. lucidLAB is usable as a markdown reader with AI surfaces, but its purpose is to surface the four storage substrates, the ingestion pipeline, the hybrid retrieval path, the LFU composition over SQLite single-writer, the ONNX execution-provider selection, the Sentinel decomposer, GraphRag entity/community output, and the lucidSupport-shaped help pattern, so the StyloBot work that consumes the same packages has somewhere to be exercised end-to-end first.

Where FULL surfaced one pipeline (StyloExtract) on the status bar, lucidLAB surfaces all of it on a dedicated F2 dashboard.

## What lucidLAB Is (and Isn't)

**Is:**

- A sibling Avalonia exe under `MarkdownViewer.Lab/` that file-links lean source the same way FULL does today (every `.cs` and `.axaml` under `MarkdownViewer/` except `Program.cs`).
- A dashboard for the lucidRAG + lucidSupport stack with a reader UI bolted on.
- Fully persistent. Every substrate (evidence text, vectors, lexical index, relational metadata) is on disk. Nothing in-memory-only.
- Built the way StyloBot will be built: clean NuGet restore against real public and private feeds.

**Isn't:**

- Shipped in releases. The release workflow does not consume lucidLAB output.
- An in-memory or parasitic-cache system. The LFU is the storage primitive, composed from Ephemeral atoms, not a cache wrapping a separate store.
- A polished product. The reader UX is functional; the dashboard is where the work is.
- Multi-tenant. Single user, single machine, single set of workspaces.

## Constraints (Carried From Existing Memory and Project State)

- Lean Release behaviour stays tiny and AOT-capable. No edits to `MarkdownViewer.csproj` package refs. Lean source edits are permitted only when every change is runtime-neutral in Release: no new code path executed when `LAB` is not defined; new XAML elements start `IsVisible="False"`; new code-behind handlers no-op in lean. The three permitted lean touch points (DI in `App.axaml.cs`, status-bar slot + keybinds in `MainWindow.axaml` / `.axaml.cs`, integration in `MainWindow.FileOperations.cs`) carry forward from FULL gated on `#if LAB` instead of `#if FULL`.
- All lucidLAB dependencies resolve from real NuGet sources: nuget.org (lucidrag OSS, StyloExtract, Mostlylucid.Ephemeral) and GitHub Packages at `https://nuget.pkg.github.com/scottgal/index.json` (lucidsupport / lucidrag.commercial). No local file feeds. No new git submodules for the lucidrag side. Existing submodules (`external/LiveMarkdown.Avalonia`, `external/lucidRESUME`) stay; they are lean-and-Lab dependencies that StyloBot does not consume.
- Releases need explicit user approval. lucidLAB is preview-only. No public release of lucidLAB.
- Full `dotnet test` on the whole solution before any release-shaped action (version bump, merge to main, workflow trigger). Lab tests included. Tests gated by `RequiresLlm`, `RequiresGpu`, `RequiresPlaywright` traits run in the release-track matrix only.
- If a lucidrag OSS or lucidsupport package needs a change to support lucidLAB: fix in upstream, publish a new version, bump the lucidLAB PackageReference. No reflection into internals. No workarounds inside lucidLAB. This is the same alpha cadence FULL uses with StyloExtract today.

## Scope

### In scope

1. New project `MarkdownViewer.Lab/`, sibling Avalonia desktop exe.
2. New project `MarkdownViewer.Lab.Tests/`, mirroring the FULL.Tests structure with category traits.
3. Removal of `MarkdownViewer.Full/` and `MarkdownViewer.Full.Tests/` in the same commit lucidLAB lands.
4. Four persistent storage substrates per workspace: evidence (SQLite + Ephemeral SlidingCache + SQLite SingleWriter), vectors (DuckDB + VSS), lexical (Lucene.Net 4.8), relational metadata (SQLite).
5. Workspace model: named workspaces with attached folders (watched), library (curated), personal corpus (annotations/highlights).
6. Ingestion pipeline: StyloExtract for HTML, `Mostlylucid.Summarizers.Reader.*` for PDF/DOCX/MD/Gutenberg, single ingestion entry point producing `Segment`s with `ContentHash` (XxHash64), single-writer fan-out to all four substrates.
7. ML stack: ONNX Runtime with per-platform native packages (DirectML/CoreML/CUDA), startup execution-provider probe with `--ep` CLI override, BERT-base-NER, `all-MiniLM-L6-v2` embeddings, LLamaSharp for synthesis (carried from FULL).
8. Retrieval pipeline: Sentinel decompose (`Mostlylucid.LucidRAG.Decomposer`), hybrid dense + BM25, RRF fusion, semantic dedup, evidence hydration via the SlidingCache + SQLite single-writer composition.
9. Synthesis with citation IDs that resolve back to source segments and drive in-reader scroll-and-highlight.
10. GraphRag surface: entity extraction, co-occurrence edges, community detection (`Mostlylucid.GraphRag`); themes panel reads from this.
11. lucidSupport pattern reused for in-document help: open document plus selection forms a `PageContext`, scoped retrieval returns a `HelpResponse` with highlights and citations. Minimum upstream surface is `LucidSupport.Core` plus `LucidSupport.Inference`; lucidLAB implements its own `ILucidSupportStorage` adapter against its substrates.
12. UI: workspace tree, reader pane, salient-terms / themes / summary panels, Ask Workspace box with query-plan preview, in-doc help overlay, F2 infrastructure dashboard with NDJSON export.
13. Telemetry: per-substrate counters, ingestion trace, retrieval trace, synthesis trace, all surfaced on the dashboard.
14. CLI verbs: `--doctor`, `--download-model`, `--ingest <path|url> --workspace <name>`, `--ask <query> --workspace <name>`, `--shot` (carried from FULL).
15. NuGet sources: `NuGet.Config` at repo root declaring nuget.org and the GitHub Packages feed. Auth via `GITHUB_TOKEN` in CI, user-scope PAT locally.
16. Pre-work in upstream: extract Typesense out of `LucidSupport.Core` into a separate plugin package, publish a Typesense-free `LucidSupport.Core` plus `LucidSupport.Inference` to the GitHub Packages feed.

### Out of scope

- AI features in lean. lean stays untouched.
- Browser-shell UI (tabs, URL bar, history sidebar). lucidLAB inherits lean's reader UI with the right-pane augmentation and the F2 dashboard added.
- Multi-tenancy. Single-user only. The `personal:*` source tag and the personal-corpus filter exist for parity with lucidRAG's SaaS pattern, but only one user identity is configured.
- Web-shell `LucidSupport.Ingestion.Playwright` / `.Selenium` / `.Crawler` / `.Bdd` pipelines. lucidLAB's input is documents, not live web pages probed for support templates.
- `LucidSupport.Storage` (Postgres-bound). lucidLAB implements its own adapter against its substrates.
- WASM head (`MarkdownViewer.Browser/`). Untouched.
- Extracting a `MarkdownViewer.Core` library. Lean source is consumed by file-link only, same as FULL today.
- Release builds, code-signing, store publishing of lucidLAB. Out of scope until the user explicitly opts in (analogous to FULL).
- Cross-workspace query. A query runs against exactly one workspace at a time.

## Identity

- Directory: `MarkdownViewer.Lab/`. Tests directory: `MarkdownViewer.Lab.Tests/`.
- `AssemblyName` stays `lucidVIEW`. Same reason FULL keeps it: lean's `avares://lucidVIEW/...` URIs in `App.axaml`, `MainWindow.axaml`, and `AppSettings.cs` compile against `AssemblyName` at build time. Shadowing those files in lucidLAB would defeat the file-link model.
- `<Product>lucidLAB</Product>` in the csproj.
- `this.Title = "lucidLAB";` in `MainWindow` ctor under `#if LAB`.
- `#if LAB` constant, set via `<DefineConstants>$(DefineConstants);LAB</DefineConstants>` in the csproj.
- First-run dialog text identifies the app as lucidLAB.
- Status-bar carries a "lab" segment showing the active workspace and the chosen execution provider.
- AppData lives at `%AppData%/lucidLAB/` on Windows, `~/Library/Application Support/lucidLAB/` on macOS, `~/.local/share/lucidLAB/` on Linux. Workspaces are under `<AppData>/workspaces/<name>/`.

## Project Layout

```
lucidview/
  MarkdownViewer/                       (unchanged, lean)
  MarkdownViewer.Tests/                 (unchanged, lean tests)
  MarkdownViewer.Lab/                   (new)
    MarkdownViewer.Lab.csproj
    Program.cs                          (entry, CLI verbs, then Avalonia start)
    App.axaml.cs                        (DI wiring under #if LAB hook in linked lean App.axaml.cs)
    AppPaths.cs                         (workspaces/, models/, telemetry/)
    Services/
      Storage/
        WorkspaceStore.cs
        EvidenceStore.cs
        VectorStore.cs
        LexicalIndex.cs
        MetadataStore.cs
      Ingestion/
        WorkspaceIngestor.cs
        HtmlIngestor.cs                 (StyloExtract)
        PdfIngestor.cs
        DocxIngestor.cs
        MarkdownIngestor.cs
        GutenbergIngestor.cs
        FolderWatcher.cs
        LibraryAdder.cs
      Ml/
        OnnxEmbedder.cs
        NerExtractor.cs
        ExecutionProviderProbe.cs
        LlamaSharpSynthesizer.cs        (carried adapter pattern from FULL)
      Retrieval/
        SentinelQueryPlanner.cs         (adapter over Mostlylucid.LucidRAG.Decomposer)
        HybridRetriever.cs
        EvidenceHydrator.cs
        SemanticDedup.cs
      Support/
        HelpResponseBuilder.cs          (lucidLAB-side adapter; consumes LucidSupport.Core)
        PageContextBuilder.cs
      Telemetry/
        InfrastructureTelemetry.cs
        IngestionTrace.cs
        RetrievalTrace.cs
        SubstrateCounters.cs
      Bootstrap/
        ModelBootstrap.cs               (carried, broadened: embedder + NER + LLM models)
    Views/
      WorkspaceTreeView.axaml(.cs)
      DocumentReaderPane.axaml(.cs)
      SalientTermsPanel.axaml(.cs)
      ThemesPanel.axaml(.cs)
      SummaryPanel.axaml(.cs)
      AskWorkspaceBox.axaml(.cs)
      QueryPlanPreview.axaml(.cs)
      InDocHelpPanel.axaml(.cs)
      InfrastructureDashboard.axaml(.cs)
      FirstRunBootstrapDialog.axaml(.cs)
    Models/
      AppSettings.Lab.cs
      WorkspaceManifest.cs
  MarkdownViewer.Lab.Tests/             (new)
    MarkdownViewer.Lab.Tests.csproj
    Fixtures/
      sample.md
      sample.pdf
      sample.docx
      sample.html
      sample-gutenberg.txt
    Storage/
      EvidenceStoreTests.cs
      VectorStoreTests.cs
      LexicalIndexTests.cs
      MetadataStoreTests.cs
      WorkspaceStoreTests.cs
    Ingestion/
      WorkspaceIngestorTests.cs
      FolderWatcherTests.cs
    Retrieval/
      HybridRetrieverTests.cs
      EvidenceHydratorTests.cs
      SentinelQueryPlannerTests.cs
    Support/
      HelpResponseBuilderTests.cs
    Properties/
      ContentHashStability.cs
      RrfDeterminism.cs
      DedupIdempotency.cs
    FailureModes/
      SubstrateDownTests.cs
      WorkspaceDriftTests.cs
      EpDemotionTests.cs
      WatcherBackpressureTests.cs
      PersonalCorpusLeakGuardTests.cs
    Ui/                                  (uses Mostlylucid.Avalonia.UITesting)
      OpenWorkspaceFlowTests.cs
      AskWorkspaceFlowTests.cs
      DashboardTests.cs
```

### Shared source via file-link

Same pattern FULL uses today: `MarkdownViewer.Lab.csproj` glob-links every `.cs` and `.axaml` under `MarkdownViewer/` except `Program.cs`, `Assets/` is linked, the lean `Naiad` project is referenced via `ProjectReference`. The three permitted lean touch points use `#if LAB` joins. Lean Release output is byte-identical with `LAB` undefined.

## NuGet Sources

`NuGet.Config` at repo root, checked in:

```xml
<packageSources>
  <clear />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  <add key="github-lucidrag-commercial" value="https://nuget.pkg.github.com/scottgal/index.json" />
</packageSources>
```

Auth:

- CI: `GITHUB_TOKEN` injected via workflow env; `dotnet nuget add source` with `--username scottgal --password $GITHUB_TOKEN --store-password-in-clear-text` in the Lab CI step before `dotnet restore`.
- Local dev: PAT with `read:packages` scope, written to user-scope NuGet config (`~/.config/NuGet/NuGet.Config` on macOS/Linux, `%APPDATA%\NuGet\NuGet.Config` on Windows). Documented in `docs/lab-edition.md`. Not in the repo.

No `/tmp/*-local-feed` patches. No path-based sources. No machine-local fallbacks. The same `dotnet restore` invocation works on a fresh CI runner and on a fresh developer machine after the one-time PAT setup.

## Architecture

### The Two-Substrate Split

lucidRAG separates the **index** (what queries run against) from the **evidence corpus** (what synthesis grounds on). lucidLAB inherits the split verbatim.

The index holds vectors, BM25 fields, salience scores, freshness signals, and `ContentHash` pointers. No segment text touches the index. Every query path operates on pointers all the way through ranking and fusion.

The evidence corpus holds segment text addressable by `ContentHash`. Nothing queries it. Text is hydrated only when synthesis is about to consume it, via `EvidenceHydrator.Resolve(ContentHash)`.

Concretely: a retrieval that returns 50 ranked segment IDs only hydrates the top-N that synthesis is going to use. The dashboard tracks the ratio `hydrations / ranked_results` so a regression where someone hydrates early shows up.

### Storage Substrates

Four physical artefacts per workspace, all under `<AppData>/workspaces/<name>/`:

- `evidence.db` - SQLite. Holds segment text keyed by `ContentHash`. Wrapped by `EvidenceStore`.
- `vectors.duckdb` - DuckDB with the VSS extension. HNSW vector index over segment embeddings. Wrapped by `VectorStore`.
- `lucene/` - Lucene.Net 4.8 file-backed index. BM25, fuzzy, proximity, field-boosting. Wrapped by `LexicalIndex`.
- `index.db` - SQLite. Small relational tables (`documents`, `segments`, `entities`, `entity_links`, `salient_terms`, `personal_facts`, `workspace_attachments`, `workspace_library`). Wrapped by `MetadataStore`.

Each substrate has a single writer per workspace. Each substrate independently fails. `WorkspaceStore` coordinates open/close and drives the cross-substrate integrity sweep at open time.

### Persistence + LFU Composition

The evidence corpus is the most read-heavy substrate. `Mostlylucid.Ephemeral.SQLite.SingleWriter` (`SqliteSingleWriter`, nuget.org `2.6.4`) is itself the LFU + write-through primitive lucidLAB uses; no separate `SlidingCacheAtom` layered on top.

Relevant `SqliteSingleWriter` 2.6.4 surface lucidLAB consumes:

- `GetOrCreate(connectionString, SqliteSingleWriterOptions?)` - keyed-by-connection-string factory; same instance returned for the same connection string within the process.
- `ReadAsync<T>(string cacheKey, Func<SqliteConnection, Task<T>>, CancellationToken)` - cached reads; the single writer manages the LFU itself, keyed on `cacheKey`.
- `WriteAndInvalidateAsync(sql, IReadOnlyDictionary<string, object?> params, IEnumerable<string> cacheKeysToInvalidate, CancellationToken)` - serialised write plus cache invalidation in one call.
- `WriteAsync(sql, IReadOnlyDictionary<string, object?>, CancellationToken)` - AOT-safe parameterised write.
- `FlushWritesAsync(CancellationToken)` - drains the in-flight write coordinator.
- `GetSignals(string pattern)` - observability stream; `cache.*` and `write.*` patterns surface to the dashboard.
- `GetWriteSnapshot()` - in-flight write tracking for backpressure.
- `EnableWriteAheadLogging` option (default `true`) - WAL is on out of the box; the older `Cache=Shared` connection-string pattern is unnecessary.
- `CacheSizeLimit`, `DefaultCacheDuration`, `HotKeyExtension`, `HotAccessThreshold` options - LFU tuning.

`EvidenceStore` wraps a single `SqliteSingleWriter` instance per workspace. `GetAsync(hash)` calls `_writer.ReadAsync<string?>("evidence:" + hash.ToHex(), reader, ct)`. `PutAsync(hash, text)` calls `_writer.WriteAndInvalidateAsync(insertSql, params, [cacheKey], ct)`. `DrainAsync(deadline)` wraps `FlushWritesAsync` with a CTS for the timeout. `GetStats()` derives counters from `_writer.GetSignals(...)` and `_writer.GetWriteSnapshot()`. No `_writeBuffer`, no separate read connection, no `SlidingCacheAtom` field. The single writer is the storage primitive; everything flows through it.

The dashboard exposes: cache hit rate, cache size, in-flight write count, last-write latency, hot-key activity - all sourced from `GetSignals` and `GetWriteSnapshot` on the same `SqliteSingleWriter` instance.

`Mostlylucid.Ephemeral.Atoms.SlidingCache` and `Mostlylucid.Ephemeral.Atoms.Data.SQLite` are not used by `EvidenceStore`. They remain available for any later substrate or service that needs a standalone in-memory LFU or a generic JSON-keyed SQLite key/value adapter, but lucidLAB's evidence corpus does not need them.

Vectors and the lexical index have their own optimised storage; metadata is small enough to live on `Microsoft.Data.Sqlite` directly without the LFU front.

### Workspace Model

A workspace is a directory holding the four artefacts plus a `manifest.json`:

```json
{
  "name": "default",
  "createdUtc": "2026-06-29T12:34:56Z",
  "attachedFolders": [{ "path": "/Users/me/notes", "include": ["**/*.md"], "exclude": [".obsidian/**"] }],
  "library": [{ "id": "ch_...", "title": "...", "originalUrl": "https://...", "ingestedUtc": "..." }],
  "personalCorpus": { "enabled": true }
}
```

`WorkspaceStore` enumerates `<AppData>/workspaces/` at startup, parses each manifest, and surfaces them in the workspace tree.

First-launch creates a `default` workspace automatically and opens it. Dropping a folder onto the lucidLAB window attaches the folder to the current workspace and triggers ingestion.

## Components

Everything below lives in the lucidLAB overlay unless tagged `(consumed)`. `(consumed)` means an existing type in a lucidrag / Ephemeral / StyloExtract package referenced via NuGet, used directly with at most a thin adapter.

### Storage substrate

- `EvidenceStore`. Wraps `SqliteSingleWriter` `(consumed from Mostlylucid.Ephemeral.SQLite.SingleWriter)` directly. The single writer is itself the LFU + write-through primitive: reads via `ReadAsync<string?>(cacheKey, reader, ct)`, writes via `WriteAndInvalidateAsync(sql, params, cacheKeysToInvalidate, ct)`, drain via `FlushWritesAsync`. Public surface: `Get(ContentHash)`, `Put(ContentHash, string)`, `BatchPut`, `DrainAsync`. Idempotent on `ContentHash`.
- `VectorStore`. Wraps `Mostlylucid.Storage.Core` DuckDB+VSS adapter `(consumed)`. Public: `Upsert(segmentId, ContentHash, vector)`, `Search(queryVector, k)`, `Delete(segmentId)`.
- `LexicalIndex`. Wraps `Mostlylucid.LucidRAG.DocSummarizer.FullText.Lucene` `(consumed)`. Public: `Index(Segment)`, `Search(query, k)`, `RebuildAsync()`.
- `MetadataStore`. `Microsoft.Data.Sqlite` direct. Public: typed repositories for documents, segments, entities, salient_terms, personal_facts, workspace_attachments, workspace_library.
- `WorkspaceStore`. Manages workspace lifecycle. Public: `EnumerateAsync()`, `OpenAsync(name)`, `CreateAsync(name)`, `CloseAsync()`, `IntegritySweepAsync()`.

### Ingestion

- `HtmlIngestor`. Uses StyloExtract `(consumed)`. Same StyloExtract integration FULL ships today, lifted into the Lab overlay.
- `PdfIngestor` / `DocxIngestor` / `MarkdownIngestor` / `GutenbergIngestor`. Thin adapters over `Mostlylucid.Summarizers.Reader.Pdf` / `.Docx` / `.Markdown` / `.Gutenberg` `(consumed)`.
- `WorkspaceIngestor`. Single ingestion entry point. Dispatches reader by MIME, runs `SegmentSelector` `(consumed from Mostlylucid.DocSummarizer.Core)`, runs `OnnxEmbedder` and `NerExtractor` in batches, calls `SalientTermsService` `(consumed)`, fans out writes to the four substrates.
- `FolderWatcher`. `FileSystemWatcher`-backed, debounced, bounded channel to `WorkspaceIngestor`. Backpressure surfaces on the dashboard.
- `LibraryAdder`. Drag-drop and `Ctrl+Shift+O` URL paste paths. URL paths route through StyloExtract first.

### ML / inference

- `OnnxEmbedder`. `Microsoft.ML.OnnxRuntime` with `all-MiniLM-L6-v2`. Reads chosen EP from `ExecutionProviderProbe`.
- `NerExtractor`. ONNX BERT-base-NER (PER, ORG, LOC, MISC). 400-token chunks, 50-token overlap.
- `ExecutionProviderProbe`. Startup probe walks CUDA, DirectML, CoreML, CPU in order. Honors `--ep <name>` override. Exposes `Selected` for the status-bar segment. `--strict-ep` causes a non-zero exit when the requested EP is not available.
- `LlamaSharpSynthesizer`. Adapter over LLamaSharp `(consumed via Mostlylucid.LucidRAG.LLamaSharp / StyloExtract.Llm.LlamaSharp)`. Same model FULL uses today (qwen3.5:4b Q4_K_M default), carried forward. Used for synthesis and for `Mostlylucid.DocSummarizer.Core` per-doc summaries (no second model).

### Retrieval / synthesis

- `SentinelQueryPlanner`. Adapter over `Mostlylucid.LucidRAG.Decomposer` `(consumed)`. Surfaces the decomposed plan to `QueryPlanPreview`.
- `HybridRetriever`. Calls `VectorStore.Search`, `LexicalIndex.Search`, fuses with RRF (k=60, weights configurable) plus salience and freshness signals from `MetadataStore`. Returns ranked `(segmentId, ContentHash, fusedScore)` triples without text.
- `SemanticDedup`. `(consumed from Mostlylucid.DocSummarizer.Core)`. Cross-document cosine drop.
- `EvidenceHydrator`. Only this component resolves `ContentHash` to text. Calls `EvidenceStore.Get`. Tracks hits/misses for the dashboard.
- GraphRag surface: `Mostlylucid.GraphRag` `(consumed)`. Entity profile vectors live in `VectorStore` alongside segment vectors (different namespace). Community detection runs as a background pass per workspace, configurable cadence. `ThemesPanel` reads communities + entity profiles to render theme clusters.

### lucidSupport adapter

- `PageContextBuilder`. Builds the lucidSupport `PageContext` from the open document plus current selection plus visible viewport segments.
- `HelpResponseBuilder`. Adapter over `LucidSupport.Core` + `LucidSupport.Inference` `(consumed, after upstream Typesense extraction)`. Runs scoped retrieval (this-doc-first, workspace-second), hydrates evidence, synthesises with explicit "answer about the highlighted region" framing, returns `HelpResponse` with highlights (segment ranges in the open doc) and citations.
- Storage adapter. lucidLAB implements `ILucidSupportStorage` (or whatever the resolved interface name is post-extraction) against its own substrates. No `LucidSupport.Storage` (Postgres-bound) package consumed.

### Reader UI

- `WorkspaceTreeView`. Left pane. Attached folders, library, personal corpus. Per-item badges: indexed, pending, error.
- `DocumentReaderPane`. Center. `LiveMarkdown.Avalonia` from lean. Adds: citation-highlight overlay, scroll-to-segment on citation click.
- `SalientTermsPanel`, `ThemesPanel`, `SummaryPanel`. Stacked right-pane segments.
- `AskWorkspaceBox` plus `QueryPlanPreview`. Top of right pane. Collapsible "what Sentinel decomposed this into" preview.
- `InDocHelpPanel`. Overlay anchored to selection. Renders `HelpResponse`.
- `InfrastructureDashboard` (F2). The testbed dashboard. Per-substrate live readings: cache hit rate, write-behind queue depth, single-writer current command, Lucene index generation, DuckDB segment count, last-query latency per substrate, ingestion channel depth, last 100 retrieval traces (Sentinel plan, sub-query embeds, recall counts per substrate, RRF top-N, dedup drops, hydration hits/misses, synthesis ms, citations emitted). NDJSON export, LF line endings. This is FULL's `ExtractionDetailsPanel` grown up.
- Status-bar: lab segment (workspace name + EP), ingestion queue depth, last-query latency, LFU hit rate.

### Telemetry

- `InfrastructureTelemetry`. Circular buffers per substrate and per pipeline. Feeds the dashboard. Same NDJSON-export discipline FULL uses.
- `LlmActivityObserver`. Reused from FULL `(consumed pattern)`.
- `SubstrateCounters`. Strongly-typed counters with reset semantics for tests.

### Config

- `AppSettings.Lab.cs`. Same shape as `AppSettings.Full.cs`. Known workspaces, last-open workspace, EP override, GGUF model path, embedder model path, NER model path, RRF weights, salience weights, personal-corpus opt-in, community-detection cadence.

### CLI verbs (exit before Avalonia starts)

- `--doctor`. EP, model paths, workspace integrity, NuGet feed reachability.
- `--download-model`. Pulls the LlamaSharp GGUF, the ONNX embedder, the NER model. First-run bootstrap also calls this.
- `--install-browsers`. Only if any HTML ingestion path still wants Playwright fallback. Otherwise dropped.
- `--ingest <path|url> --workspace <name>`. Headless ingest into a workspace.
- `--ask <query> --workspace <name>`. Headless retrieve + synthesise. Prints answer plus citations.
- `--shot`. Carried from FULL.
- `--ep <name>`, `--strict-ep`. Diagnostic overrides for the execution-provider probe.

## Data Flows

### A. Ingestion (folder watch path)

1. `FolderWatcher` raises a debounced path event. Enqueues onto `WorkspaceIngestor`'s bounded channel. Channel depth visible on dashboard.
2. `WorkspaceIngestor` dispatches by extension: `.md` to `MarkdownIngestor`, `.pdf` to `PdfIngestor`, `.docx` to `DocxIngestor`, `.html` to `HtmlIngestor` (StyloExtract).
3. Reader yields normalised markdown blocks plus per-document metadata.
4. `SegmentSelector` chunks and scores salience (entity density, TF-IDF rarity, heading importance). Each emitted `Segment` carries a `ContentHash` (XxHash64 over normalised content bytes).
5. Per segment:
   - Idempotency check against `MetadataStore.segments` by `ContentHash`. Hit: skip. Miss: continue.
   - `OnnxEmbedder` produces the vector. `NerExtractor` produces entity spans. Both batched.
   - Single-writer fan-out: vector to `VectorStore`, BM25 fields to `LexicalIndex`, text to `EvidenceStore` (which routes through `SqliteSingleWriter`), row to `MetadataStore.segments` with `ContentHash` pointer, salience, freshness.
6. After all segments for the document complete: `SalientTermsService` recomputes the document's keywords (TF-IDF plus entity frequency, RRF-fused). `salient_terms` rows written. Document-level summary computed lazily on first read by `Mostlylucid.DocSummarizer.Core`.
7. Telemetry: per-document trace (reader, segments produced, embedder ms, NER ms, write-behind queue delta, total ms) appended to `InfrastructureTelemetry`. Dashboard updates live.

Library path is identical except step 1 is a drag-drop or URL paste, and URL paths route through StyloExtract before reader dispatch.

### B. Retrieval and synthesis (Ask Workspace)

1. User types into `AskWorkspaceBox`. `SentinelQueryPlanner.Plan(query)` runs (decompose, classify, detect clarification need).
2. If clarification needed: `QueryPlanPreview` renders the clarification prompt; user response routes back into step 1.
3. `QueryPlanPreview` shows the decomposed sub-queries so the operator can see whether Sentinel is doing the right thing.
4. For each sub-query:
   - Embed via `OnnxEmbedder` (cached by sub-query text hash for repeats within the session).
   - Dense recall from `VectorStore` (top-K by HNSW, k=50 default).
   - BM25 recall from `LexicalIndex` (top-K, query expansion via salient-terms substitution when the classifier indicates lexical-leaning).
   - Each returns `(segmentId, ContentHash, score)`. No text fetched.
5. `HybridRetriever` runs RRF (k=60) across dense + BM25 + segment-salience + freshness decay. Returns ranked triples.
6. `SemanticDedup` prunes near-duplicates.
7. **Evidence hydration is the only place text appears.** `EvidenceHydrator` resolves each surviving `ContentHash` via `EvidenceStore`. SlidingCache hits return immediately; misses fall through to `SqliteRepository` and warm the cache.
8. `LlamaSharpSynthesizer` runs synthesis with hydrated segments as evidence plus citation IDs. Output streams to UI.
9. Citation IDs in the response are clickable. `DocumentReaderPane` opens the source document and scrolls/highlights the cited span.
10. Per-query trace into `InfrastructureTelemetry`. NDJSON export.

### C. In-document help (lucidSupport "serve" pattern, scoped to current document plus workspace)

1. User makes a selection or right-clicks in `DocumentReaderPane`, picks "Help with this".
2. `PageContextBuilder` builds the `PageContext`: open document metadata, viewport bounds (visible segments), selection text and range.
3. `HelpResponseBuilder.Build(pageContext, optionalQuery)`:
   - Constructs a scoped retrieval query: selection text as the seed, this-document segments boosted, workspace segments as fallback.
   - Runs steps 4 through 7 of flow B above. No Sentinel decompose; `PageContext` is the scope.
   - Synthesises with the prompt scoped to the highlighted region.
4. Returns `HelpResponse` with highlights (segment ranges in the open doc) plus citations plus answer text.
5. `InDocHelpPanel` overlay renders next to the selection. Highlights paint over the reader.

### Three points the testbed contract pins

- The pointer-only flow through B.4 to B.6 is the index/evidence split made visible. The dashboard plots `hydrations / ranked_results` so a regression where text is pulled early shows up.
- The single-writer fan-out in A.5 is the only place all four substrates are written. One transaction boundary. If they drift, this is where a bug can live.
- Flow C is the intentional lucidSupport dogfood: same `PageContext` → scoped retrieval → `HelpResponse` shape the support widget runs against web pages, run against a markdown document instead.

## Error Handling

The testbed contract is "no silent fallbacks, no swallowed exceptions, every failure visible on the dashboard". lucidLAB is usable so one failing document cannot kill the app, but the bar is "visible, diagnosable, contained", not "smoothed over".

### Substrate failures

- Each of the four substrates raises typed exceptions through its wrapper and increments a per-substrate error counter on `InfrastructureTelemetry`.
- `WorkspaceStore` catches at the substrate boundary, sets the workspace into `Degraded(substrate, reason)`, paints a red badge in `WorkspaceTreeView`, and locks out the affected paths. Lexical down: BM25 disabled, dense still runs. Vector down: dense disabled, BM25 still runs. Synthesis disabled if both indexes are down.
- No silent retry. Dashboard surfaces exception text, substrate, timestamp. A manual "Retry" action on the dashboard re-opens the substrate.

### Workspace drift

- On `WorkspaceStore.Open`, an integrity sweep over a sampled N segments: verify the `ContentHash` resolves in `EvidenceStore`, the segmentId resolves in `VectorStore`, the lexical record exists in `LexicalIndex`. Drift count surfaces as one dashboard line.
- Above threshold (default 5%, configurable), workspace opens read-only with a dashboard banner. Manual "Rebuild index" action re-ingests from evidence + source files.
- No automatic rebuild. The dogfood point is to see drift, not paper over it.

### ONNX execution-provider failures

- Startup probe falls through CUDA → DirectML → CoreML → CPU on requested-EP failure. Demotion surfaces on status bar with a bold marker plus a dashboard trace recording `requested=cuda, got=cpu, reason=...`. `--strict-ep` causes a non-zero exit.
- Runtime EP failures (GPU loss, OOM) demote the embedder to CPU for the rest of the session. Status-bar alert. Telemetry event. No auto-recover; next app restart re-probes.

### Ingestion failures

- Per-document failures (reader exception, embedder timeout, NER failure, too-large input) mark the document `IngestionState.Error(stage, reason)` in `MetadataStore.documents`. Red badge in `WorkspaceTreeView`. Document is not added to index or evidence. Dashboard's "Last 100 ingests" row records the failure.
- StyloExtract escalation telemetry (the existing FULL signal) carried forward: Playwright fallback events, streaming bailouts, template induction events.
- Bounded ingestion channel: at depth ceiling, `FolderWatcher` switches from push to drop-with-trace. Backpressure visible on the dashboard. Ingestion is dogfood-bounded, not optimistic.

### Retrieval failures

- Sentinel parse failure falls through to raw-query mode, marks `QueryPlanPreview` with a "sentinel failed" tag, runs query as one sub-query.
- Dense or BM25 returning zero: `QueryPlanPreview` shows the actual counts per substrate ("dense 0, BM25 12, RRF skipped"). Empty results returned with substrate breakdown. Never "no results found" without diagnostic.
- RRF degenerate (all candidates from one substrate) flagged in the trace; results still returned with a marker so a regression where one substrate stops contributing is obvious.
- Evidence hydration miss (segment in index but not in evidence - drift case): segment dropped from synthesis input, hydration-miss counter increments, segment ID logged. Synthesis still runs with remaining segments.

### Synthesis failures

- LlamaSharp model missing: no synthesis. UI shows "synthesis offline (download model)" with a button invoking `--download-model`. Retrieval still works; citations render without an answer.
- LLM timeout or OOM: emit partial output if any, mark the trace, surface on the dashboard. No auto-retry.

### Write-behind queue

- `SqliteSingleWriter` queue depth surfaces on the dashboard. Configurable depth ceiling. At ceiling, writes block (single-writer semantics: bounded backpressure is correct). Status-bar surface for "writes blocked".
- On clean shutdown, `WorkspaceStore.Close` flushes the queue with a configurable drain deadline. If drain doesn't finish, partial state is logged. On next launch, the workspace opens read-only with the dashboard banner. The integrity sweep will determine whether rebuild is needed.

### Folder-watcher edge cases

- inotify / FSEvents limits caught; switch to polling for the affected folder; surface "polling fallback (rate=X)" on the dashboard.
- Unreadable paths recorded once per path, not spammed. Per-folder error badge in `WorkspaceTreeView`.

### Personal-corpus boundary

- A leak of `personal:*` entries into default retrieval is a contract bug. Filter enforced at `MetadataStore.segments` query level. Startup assertion verifies the filter is applied on every retrieval path. Tests verify the assertion trips when the filter is bypassed. If the assertion ever trips at runtime, the workspace refuses to serve retrieval - fail loud, this is privacy-shaped.

### Principles

- No silent fallbacks. Every fallback (EP demotion, polling fallback, sentinel bypass) has a visible surface.
- No swallowed exceptions. Every catch increments a counter and writes a trace.
- Per-substrate isolation. One substrate going down degrades; nothing crashes.
- The dashboard is the source of truth for "is this thing actually working". If it's not on the dashboard, it's not observable, which means it's not testbed-grade.

## Testing

### Project shape

`MarkdownViewer.Lab.Tests/` mirrors the existing `MarkdownViewer.Full.Tests/` layout. xUnit + FluentAssertions + `InternalsVisibleTo`. Category traits gate expensive tests:

- (default) - unit plus cheap integration. Every CI matrix slot.
- `RequiresLlm` - LlamaSharp paths.
- `RequiresGpu` - non-CPU ONNX EP.
- `RequiresPlaywright` - only if any HTML ingestion path still wants Playwright fallback.

CI default excludes the three Requires-* categories. Nightly / release-track CI includes them.

### Unit tier

Per-substrate wrapper tests:

- `EvidenceStore`. Round-trip `ContentHash` → text persists across re-open. Concurrent reads while a write is in flight return either old or new but never partial. Single-writer enforces ordering. Write-behind queue depth advances and drains. SlidingCache eviction at configured ceiling. Idempotent re-put of same `ContentHash` is a no-op.
- `VectorStore`. DuckDB+VSS round-trip. HNSW recall returns expected segments for a fixture corpus. Delete + re-insert maintains index integrity. Empty-store query returns empty (not error).
- `LexicalIndex`. Lucene round-trip. BM25 ordering for a known fixture. Fuzzy, proximity, field boosting behave. Reader generation advances on writer commit.
- `MetadataStore`. Schema migration test (forward only; failures fail loud). `personal:*` filter enforced at query level - separate test asserts no retrieval path returns a `personal:*` row without explicit opt-in, and the startup assertion is verified to trip when the filter is bypassed.
- `WorkspaceStore`. Open/close idempotent. Concurrent open of the same workspace path blocks the second opener with a typed exception (single-writer per workspace). Clean shutdown drains write-behind within the configured deadline.

### Integration tier

- Ingestion. Fixture documents in each supported format. End-to-end: reader → SegmentSelector → embed (CPU EP in CI) → NER → write to all four substrates. Assert: segment counts, `ContentHash` idempotency on re-ingest, salient terms produced, entity rows produced, all four substrates agree on segment IDs.
- Retrieval. Populated workspace with N fixture docs, run K queries, assert: dense top-K matches expected segments, BM25 top-K matches expected segments, RRF fused order deterministic for the same inputs, dedup drops the planted near-duplicate, evidence hydration returns the exact source text.
- Synthesis (`RequiresLlm`). Small fixture corpus, fixed seed where the model permits, assert: citations resolve back to source segments, no segment text in the response that wasn't in the hydrated set, citation IDs match `MetadataStore` IDs.
- In-doc help. Fixture document plus selection. Assert `HelpResponse` highlights are within the document, citations are scoped this-doc-first.

### Property tier

- `ContentHash` stable across runs for the same input bytes.
- RRF deterministic given identical inputs and weights.
- `SemanticDedup` idempotent on a deduped set.
- The `EvidenceStore` composition (`SlidingCacheAtom` over `SqliteSingleWriter`) preserves last-writer-wins for the same `ContentHash` across burst writes.
- Re-ingestion of a document with identical content is a complete no-op (no row touches, no substrate writes).

### Failure-mode tier (the dogfood contract)

- Substrate-down isolation. Open a workspace, force-close `LexicalIndex` mid-session, run a query: `QueryPlanPreview` shows BM25=skipped, dense still runs, dashboard red badge, synthesis proceeds with dense-only evidence. No exception leaks to UI.
- Workspace drift. Populate, then manually delete a row from `EvidenceStore` for a segment that exists in `MetadataStore`: integrity sweep at next open reports drift, workspace opens read-only, dashboard banner. Rebuild action restores integrity.
- EP demotion. Request CUDA on a CPU-only runner: probe falls through to CPU, status bar shows demotion, telemetry records `requested=cuda, got=cpu, reason=...`. With `--strict-ep`, app exits non-zero.
- Folder-watcher backpressure. Saturate the ingestion channel beyond capacity: `FolderWatcher` switches to drop-with-trace, dashboard counter advances, no exception.
- Personal-corpus leak guard. Construct a query path that bypasses the filter: startup assertion trips; workspace refuses to serve retrieval.
- Synthesis offline. Remove the model file: retrieval still runs, citations render, synthesis UI shows the offline state with the `--download-model` action.

### UI tier (`Mostlylucid.Avalonia.UITesting`, Debug-only)

Same UITesting project lean and FULL already use. Flows:

- Open lucidLAB → workspace picker (or auto-create `default`) → drop a fixture document → wait for ingestion badge → run Ask Workspace with a known query → assert answer panel shows citations → click citation → assert reader scrolls and highlights.
- F2 dashboard opens, shows per-substrate counters non-zero, NDJSON export writes a valid file with one record per traced operation.
- Status-bar segments populate: EP, workspace, queue depth, LFU hit rate.
- Theme switch round-trip (carried lean test).

### Determinism and flakiness budget

- All NDJSON exports compared in tests use LF endings (FULL discipline).
- Workspace tests write to temp dirs under `Path.GetTempPath()`, cleaned in `IAsyncDisposable` per-test.
- `RequiresLlm` tests use a small fixed-seed model.
- `RequiresGpu` tests assert the EP the probe picked, not a specific EP. Runner-portable.
- No `Thread.Sleep`. Only awaitable signals (channel reader, dashboard counter `WaitForAsync`).

### Test data

A small set of canonical fixture docs under `MarkdownViewer.Lab.Tests/Fixtures/`. Real-shape but minimal: one Markdown doc, one short PDF, one DOCX, one HTML page (exercises the StyloExtract surface), one Gutenberg text.

Real workspace traces from dogfood sessions can be replayed via the NDJSON export. A "replay this trace and assert each step" test mode follows from the NDJSON discipline.

### Pre-release gate

Every release-shaped action (version bump, merge to main, workflow trigger) runs the full `dotnet test` on the whole solution first. Lab tests included. `Requires-*` excluded by default; included on the release workflow.

## Build / CI

### Solution shape

Two heads: `MarkdownViewer/` (lean) and `MarkdownViewer.Lab/` (lab). `MarkdownViewer.Full/` and `MarkdownViewer.Full.Tests/` are removed in the same commit lucidLAB lands.

### CI matrix

`.github/workflows/ci.yml`:

- Lean: unchanged. Windows / Ubuntu / macOS, AOT-clean single-file Release plus tests.
- Lab: same three OSes, Debug only.
  - Setup: `dotnet nuget add source` for the GitHub Packages feed with `$GITHUB_TOKEN`.
  - `dotnet restore`, `dotnet build -c Debug`, `dotnet test MarkdownViewer.Lab.Tests --filter "Category!=RequiresLlm&Category!=RequiresGpu&Category!=RequiresPlaywright"`.
  - Test results published as workflow artefacts.

`.github/workflows/release.yml`:

- Unchanged. lucidLAB does not ship in releases.

### publish.ps1

Same script, switches:

- `-Edition Lean` (default) - produces the shipped lean exe per platform.
- `-Edition Lab` - produces Debug-only Lab artefacts per platform. Not for release.
- `-Edition All` - both.

No `--self-contained` flag tricks for Lab. The point is to surface the real EP and the real native blob behaviour.

### Versioning

- Lean version unchanged by this work.
- Lab starts at `<Version>0.1.0</Version>` per the existing FULL pattern. Bumps require explicit user approval; the "Z only" memory rule applies to Lab the same way it applies to lean.
- Lab is preview-only. No public release.

### Workflow when upstream needs a fix

If `Mostlylucid.LucidRAG.*` or any lucidsupport package needs an API change for lucidLAB:

1. Fix in the upstream repo (`scottgal/lucidrag` or `scottgal/lucidrag.commercial`).
2. Publish a new package version to the appropriate feed (nuget.org for OSS, GitHub Packages for commercial).
3. Bump the `PackageReference` in `MarkdownViewer.Lab.csproj`.
4. Validate locally with `dotnet test`.
5. Commit the bump and ship.

Same cadence FULL uses with StyloExtract alphas today. No reflection into internals. No workarounds in lucidLAB.

## Pre-Work in Upstream Repos

Required before lucidLAB can build a green CI:

1. **Mostlylucid.LucidRAG.Decomposer** confirmed published on nuget.org with the public Sentinel surface (`SentinelRefiner` reachable through the package's public API). If not yet packed, pack and publish.
2. **Mostlylucid.LucidRAG.UltraResearch** confirmed published. Not on the critical path for the minimum Ask Workspace flow but listed in the resolved-decisions section as part of the full GraphRag-included testbed surface.
3. **Mostlylucid.GraphRag** confirmed published with the community-detection surface.
4. **Mostlylucid.Ephemeral.Atoms.SlidingCache**, **Mostlylucid.Ephemeral.SQLite.SingleWriter**, **Mostlylucid.Ephemeral.Atoms.Data.SQLite** confirmed published on nuget.org (the Ephemeral family is published per local inspection; package IDs verified in this spec).
5. **LucidSupport.Core Typesense extraction.** Today `src/LucidSupport.Core/LucidSupport.Core.csproj` carries `<PackageReference Include="Typesense" Version="8.1.0" />`. Pre-work in `scottgal/lucidrag.commercial`:
   - Extract the Typesense-specific code paths from `LucidSupport.Core` into a new `LucidSupport.Plugin.Typesense` project.
   - `LucidSupport.Core` becomes Typesense-free, exposing the abstraction (`ILucidSupportSearch` or whatever the resolved interface name lands as) that `LucidSupport.Plugin.Typesense` implements.
   - Publish a new minor of `LucidSupport.Core` and `LucidSupport.Inference` to the GitHub Packages feed.
6. **lucidLAB-side `ILucidSupportStorage` adapter.** Internal to lucidLAB; satisfies whatever storage shape `LucidSupport.Core` requires post-extraction, backed by lucidLAB's substrates. No `LucidSupport.Storage` package consumed.

The list of packages lucidLAB consumes from each feed lands in the implementation plan once items 1 through 5 are confirmed published.

## Resolved Decisions

Recording these here so the spec doesn't pretend they were obvious:

1. **lucidLAB replaces lucidVIEW-FULL.** Single dogfood exe. `MarkdownViewer.Full/` and tests removed in the same commit.
2. **Corpus = workspace.** A workspace owns attached folders (watched, live-ingested), a library (curated drag-drop and URL-paste through StyloExtract), and a personal corpus (annotations/highlights tagged `personal:*`, excluded from default retrieval but their entities feed the graph). Multiple named workspaces, switchable. First-launch auto-creates `default` and opens it. Dropping a folder onto the window attaches it and triggers ingestion.
3. **Two substrates per the lucidRAG split.** Index (vectors + lexical + metadata + pointers) is queried. Evidence corpus (text addressable by `ContentHash`) is only hydrated at synthesis. Pointer-only flow through retrieval and ranking.
4. **Evidence storage uses `SqliteSingleWriter` directly** (`Mostlylucid.Ephemeral.SQLite.SingleWriter 2.6.4`). The single writer is itself the LFU + write-through primitive (built-in cached reads via `ReadAsync<T>(cacheKey, …)`, atomic write+invalidate via `WriteAndInvalidateAsync`, WAL on by default). `SlidingCacheAtom` and `SqliteDataStorageAtom` are not used by `EvidenceStore` — composing them on top of the single writer would be the parasitic-cache pattern the user explicitly ruled out.
5. **Vector store is DuckDB + VSS persistent.** lucidRAG's embedded answer. Not sqlite-vec, not in-memory.
6. **Lexical index is Lucene.Net 4.8.** lucidRAG ships `Mostlylucid.LucidRAG.DocSummarizer.FullText.Lucene`; lucidLAB consumes it.
7. **ONNX GPU is the default**, via per-platform native packages (DirectML on Windows, CoreML on macOS, CUDA on Linux). Auto-detect at startup, surface chosen EP on status bar, `--ep` CLI override, `--strict-ep` for diagnostics.
8. **Same LlamaSharp model** for synthesis and for per-document summaries (`Mostlylucid.DocSummarizer.Core`). No second GGUF.
9. **Full GraphRag surface** in scope: entity extraction, co-occurrence edges, community detection. The Themes panel reads from this.
10. **Sentinel via `Mostlylucid.LucidRAG.Decomposer`** for the immediate query-decomposition need. `Mostlylucid.LucidRAG.UltraResearch` is in scope for the full testbed surface as a separate phase.
11. **lucidsupport minimum surface = `LucidSupport.Core` + `LucidSupport.Inference`**, after upstream Typesense extraction. lucidLAB implements its own storage adapter. `LucidSupport.Storage` (Postgres) not consumed.
12. **NuGet only.** nuget.org for OSS, GitHub Packages for commercial. No local file feeds. No new submodules for lucidrag-shaped dependencies. Existing submodules (LiveMarkdown.Avalonia, lucidRESUME) carry forward unchanged.
13. **Upstream-fix-then-republish** is the canonical pattern. No reflection into internals.
14. **CLI verbs** `--ingest` and `--ask` are provisional; renaming is cheap and not load-bearing here.

## Out of Scope (Restated)

- AI features in lean.
- Multi-tenant SaaS shapes beyond the `personal:*` source tag pattern.
- `LucidSupport.Ingestion.Playwright` / `.Selenium` / `.Crawler` / `.Bdd` pipelines. lucidLAB ingests documents, not live web pages.
- `LucidSupport.Storage` (Postgres-bound).
- WASM head (`MarkdownViewer.Browser/`).
- Extracting `MarkdownViewer.Core`.
- Release / signing / store publishing for lucidLAB.
- Cross-workspace queries.

## Next Step

User reviews this document. On approval, the writing-plans skill runs to produce the task-by-task implementation plan, mirroring the structure of `docs/superpowers/plans/2026-06-25-lucidview-full.md`.