# lucidVIEW-FULL — the dogfood sibling exe

A standalone guide to `MarkdownViewer.Full/`, the second Avalonia exe that
exercises the preview StyloExtract LLM + Playwright stack against
real-world web pages so the upstream library iterates with a tight
feedback loop. Lean `MarkdownViewer/` stays AOT-clean, single-file, and
behaviourally unchanged in Release.

If you just want to run it:

```bash
dotnet run --project MarkdownViewer.Full/MarkdownViewer.Full.csproj -c Debug
```

The rest of this doc explains what FULL actually does behind that command.

---

## 1. What FULL is (and isn't)

**Is:** a sibling Avalonia exe (`MarkdownViewer.Full/`) that **file-links**
lean source — every `.cs` and `.axaml` under `MarkdownViewer/` except
`Program.cs` — and adds a small `MarkdownViewer.Full/` overlay on top
(its own entry point, the FULL-only services, FULL-only views, FULL-only
settings). Two `#if FULL` join points in the linked lean source — DI
wiring in `App.axaml.cs` and a status-bar slot + F2 keybind + the
chunked-feed integration in `MainWindow.axaml.cs` /
`MainWindow.FileOperations.cs` — are runtime-neutral when the constant
is not defined.

**AssemblyName stays `lucidVIEW`** (NOT `lucidVIEW-FULL`). Lean's
`avares://lucidVIEW/...` URIs in `App.axaml`, `MainWindow.axaml`, and
`AppSettings.cs` compile against `AssemblyName` at build time — renaming
would require shadowing those three lean files with FULL-only copies,
defeating the file-link model. FULL identity surfaces at runtime via:

- `<Product>lucidVIEW-FULL</Product>` in the csproj
- `this.Title = "lucidVIEW-FULL"` in `MainWindow` ctor under `#if FULL`
- the first-run bootstrap dialog text
- the status-bar pipeline stage indicator

The two editions live in different `bin/` and `publish/` directories, so
on-disk collision is moot.

**Lean Release is byte-identical** to before the `feat/lucidview-full`
branch existed. Every lean source touch that supports FULL is permitted
only because every change is runtime-neutral in Release. The exact list
of permitted lean touches is enumerated in **Global Constraints** of
`docs/superpowers/plans/2026-06-25-lucidview-full.md`. Anything beyond
those is a defect — escalate, don't ship.

**Isn't:** shipped in releases. FULL is a Debug-only CI artefact
(eventually). The `publish.ps1 -Edition full` switch exists but the
release workflow does not consume it.

---

## 2. What FULL adds beyond lean

Everything below is FULL-only behaviour — lean does none of it.

- **Real StyloExtract pipeline.** `HtmlToMarkdownServiceFull` uses
  `ILayoutExtractor` from `Mostlylucid.StyloExtract.Core` with the SQLite
  template store (`AppPaths.LocalState/styloextract-templates.db`).
  After the first visit to a host, the structural fingerprint is stored
  and subsequent requests use the learned extractor instead of re-running
  full heuristics. Lean's `HtmlToMarkdownService` is a heuristic-only
  best-effort fallback.
- **Playwright rendered-DOM auto-retry.** When the static fetch returns
  empty / SPA-shell / sparse-blocks markup, FULL transparently retries
  via `PlaywrightHtmlFetcher` (Chromium) using `WaitUntil.Load` to avoid
  client-side router drift (e.g. BBC News auto-navigating
  `/news` → `/articles/<id>` on `NetworkIdle`).
- **In-process LLM template induction.** `LlamaSharpTextProvider`
  (CPU backend) hosts a small instruction-tuned model — default
  `qwen3.5:4b` (Q4_K_M, ~2.5 GB) — that StyloExtract calls to induce
  per-host extraction templates from a few sample pages. Model is lazily
  auto-downloaded from HuggingFace on first use into
  `AppPaths.ModelCacheDir`.
- **Streaming gateway scanner.** Alpha.19's sliding-window byte-stream
  scanner runs alongside the HttpClient byte stream. As chunks arrive
  they are fed straight into `IncrementalFenceScanner` which emits a
  running verdict (`Captured` / `Bailout` / `NoTemplate` / `Continue`)
  with bounded memory — `peak ~16 KiB` regardless of response size.
- **Per-host template persistence.** Two stores under
  `AppPaths.LocalState`:
  - `styloextract-templates.db` — SQLite layout-template store (LayoutExtractor)
  - `streaming-templates.db` — SQLite streaming-template store (gateway scanner)
  - `templates/<host>.yaml` — LLM-induced YAML, written by the LLM coordinator
  - `templates/<host>-deterministic.yaml` — heuristic-induced YAML, written by LayoutExtractor's optional template-sink dependency
- **Pipeline stage indicator** in the status bar — live dogfood signal
  showing fetcher, streaming verdict, match status, LLM induction, and
  render output. Format details in §6.
- **F2 Extraction Details panel** with NDJSON export. Click the
  status-bar line or press F2 to open the panel; it lists the last
  N extractions with template ID / version / fetcher / durations / block
  counts. NDJSON export uses LF line endings (not platform default) for
  diff-friendly storage.
- **Read/Scan mode toggle.** Header UI flips between `ExtractionProfile.RagFull`
  (article body, captions, related links — Read mode, default) and
  `ExtractionProfile.Sitemap` (title + nav + breadcrumb only — Scan mode,
  for browser-mode navigation across a site).
- **First-run bootstrap dialog.** On first launch, prompts to pre-fetch
  the GGUF model and pre-install Playwright Chromium so the first
  extraction doesn't stall on multi-GB downloads.
- **CLI verbs** that exit before Avalonia starts: `--doctor`,
  `--install-browsers`, `--download-model`, `--shot`. Reference in §4.

---

## 3. The dogfood pipeline (step-by-step)

Trace what happens on a **first visit** to <https://www.mostlylucid.net>.

1. **HTTP open with `ResponseHeadersRead`.** `DownloadWebPageAsync` opens
   `HttpClient.SendAsync(HttpCompletionOption.ResponseHeadersRead)` to get
   a stream we can read chunk-by-chunk.
2. **Warm any persisted streaming template.** Before the body arrives,
   fire-and-forget `StreamingPathSelector.WarmByHostAsync(host)` — on a
   second visit this pulls the template from SQLite into the hot cache.
   First visit: no-op.
3. **Chunked feed.** As ~16 KiB chunks arrive, each is fed into
   `IncrementalFenceScanner.Feed(chunk)`. The scanner emits a running
   verdict. Compact-on-emit drops consumed bytes immediately after each
   `TryReadTag` returns, so peak in-flight buffer stays
   O(chunk_size + longest_tag). First visit has no template → scanner is
   null and verdict stays `NoTemplate`; bytes still drain into a
   `MemoryStream` for the full extraction pipeline.
4. **Status-bar fetch segment** updates with the verdict + peak bytes —
   e.g. `fetch Http+NoTemplate · 227ms` on first visit. On a primed
   second visit: `fetch Http+Captured+peak16473B/199506B · 227ms`
   (measured on mostlylucid.net, alpha.21 smoke).
5. **Auto-induce on `NoTemplate`.** When the verdict is `NoTemplate` and
   the body looks HTML-shaped, `StreamingTemplateInducer.Induce(host, fullBytes)`
   runs against the buffered body. If it returns a template, that's
   upserted to `streaming-templates.db`. The next visit gets a real
   `Captured` / `Bailout` verdict mid-stream.
6. **Full extraction via `HtmlToMarkdownServiceFull.ConvertAsync`.**
   `HtmlPreProcessor.Apply(html)` (lifted from lean's
   `HtmlToMarkdownService` so both editions share the same pre-processing),
   then `ILayoutExtractor.ExtractAsync(pre, sourceUri, opts)` with the
   current Mode's profile. The status bar gets a Match stage update with
   the StyloExtract `MatchStatus` (e.g. `SlowPathMatch`, `FastPathHit`,
   `Novel`).
7. **Playwright retry decision.** `RenderedFetchPolicy.ShouldRetry`
   inspects the static HTML, the extracted markdown, and the block count.
   On empty / SPA-shell / sparse-blocks, it re-fetches via Playwright
   with `WaitUntil.Load` and re-runs `ExtractAsync` against the rendered
   DOM. Status bar emits a second `fetch Playwright` stage.
8. **Template YAML writes.** LayoutExtractor's optional
   `IDeterministicTemplateSink` writes `<host>-deterministic.yaml` after
   each heuristic induction. A `FileSystemWatcher` on the templates dir
   surfaces these as a status-bar `llm <host> (det)` segment. LLM-induced
   templates (when the LLM coordinator runs them) appear as
   `llm <host>` (no `(det)` suffix).
9. **Markdown returned.** Lucidview renders via the forked
   `LiveMarkdown.Avalonia 1.9.2-local-imgfix2` so raw `<img width=H height=W>`
   tags flow through with proper dims instead of being squashed to line-height.
10. **Telemetry recorded.** `ExtractionTelemetry.Record(LastExtractionInfo)`
    feeds the F2 details panel and the status-bar render segment
    (`render 17 blocks · 27K`).
11. **Debug dump.** The extracted markdown is written to
    `AppPaths.LocalState/extractions/<host>-<HHmmss>.md` so failures can be
    diffed by hand.

**Second visit** to the same host:

1. `WarmByHostAsync(host)` pulls the streaming template from SQLite into
   the hot cache before the body arrives.
2. `ScanByHost` returns `Captured` mid-stream (alpha.21 sliding window).
3. Subsequent extracts hit the cached SQLite layout template via the
   StyloExtract fast path.
4. Status bar shows
   `fetch Http+Captured+peak16473B/199506B · Nms · match FastPathHit · …` —
   ~200 KB response held under ~16 KiB peak in-flight (8.26% ratio,
   measured on mostlylucid.net).

The streaming-mode contract details (verdicts, sliding-window
mechanics) live in the upstream `stylobot-extract` repo at
`docs/streaming.md`. This guide does not repeat them.

---

## 4. CLI verbs reference

All four verbs are parsed in `MarkdownViewer.Full/Program.cs` before
Avalonia starts; they exit when done.

| Verb | Purpose | Example |
|---|---|---|
| `--doctor` | Report model path, browsers path, and "ready to extract" status. Exit code `0` if ready, `1` otherwise. | `dotnet run --project MarkdownViewer.Full -- --doctor` |
| `--install-browsers` | Pre-install Playwright Chromium so the first SPA retry doesn't stall. | `dotnet run --project MarkdownViewer.Full -- --install-browsers` |
| `--download-model [hf-id]` | Pre-fetch the GGUF model. Defaults to `LlmModelPath` from `settings.json`. Pass a HuggingFace `owner/repo/filename.gguf` to override. Verifies via `EnsureLoaded()` when `LlmEnabled=true`. | `dotnet run --project MarkdownViewer.Full -- --download-model unsloth/Qwen3.5-4B-GGUF/Qwen3.5-4B-Q4_K_M.gguf` |
| `--shot <url> <out.png> [--wait MS] [--mode Read\|Scan]` | Open without focus theft, auto-navigate to the URL, wait for image cache + re-render, capture a window screenshot to PNG, exit. Defaults: `--wait 30000`, `--mode Read`. | `dotnet run --project MarkdownViewer.Full -- --shot https://www.mostlylucid.net /tmp/lvshot.png --wait 8000` |

The `--shot` verb is the key dogfood tool — it lets you visually verify
an extraction across a corpus of test URLs without focus theft, and is
how the alpha.21 smoke comparison screenshots were captured
(`/tmp/lvshot-alpha19-v1.png` NoTemplate vs `/tmp/lvshot-alpha19-v2.png`
Captured). `--mode Scan` exercises the Sitemap profile end-to-end.

---

## 5. Settings & state directories

Resolved by `MarkdownViewer.Full/AppPaths.cs`. Per-platform
`AppPaths.LocalState`:

- macOS: `~/Library/Application Support/lucidVIEW-FULL/`
- Linux: `${XDG_STATE_HOME:-~/.local/state}/lucidview-full/`
- Windows: `%LOCALAPPDATA%\lucidVIEW-FULL\`

Override with `LUCIDVIEW_STATE_DIR=/some/path`. Model cache is overridable
separately with `LUCIDVIEW_MODEL_CACHE=/some/path`.

Contents:

| Path | Purpose |
|---|---|
| `settings.json` | `AppSettingsFull` (LLM model path, `LlmEnabled`, `LlmContextSize`, `LlmThreads`, `LlmGpuLayerCount`, `PlaywrightEnabled`, `HasRunBefore`). Plain `JsonSerializer` (FULL is permitted non-AOT). |
| `models/` | Downloaded GGUF files. HF refs `owner/repo/file.gguf` become `owner_repo_file.gguf` on disk. |
| `templates/<host>.yaml` | LLM-induced extraction template. |
| `templates/<host>-deterministic.yaml` | Heuristic-induced extraction template (alpha.11+ `IDeterministicTemplateSink`). |
| `styloextract-templates.db` | SQLite layout-template store (`StyloExtract.Core`). |
| `streaming-templates.db` | SQLite streaming-template store (`StyloExtract.Streaming`, alpha.17+). |
| `extractions/<host>-<HHmmss>.md` | Per-call debug dump of the extracted markdown — most recent for each host plus a timestamp. |
| `crash.log` | Global exception log (`UnhandledException` + `UnobservedTaskException`). |

These never collide with lean's settings folder (`MarkdownViewer/`).

---

## 6. Pipeline stage indicator format

The status bar shows up to four segments separated by ` · `, in
fixed order: **fetch · match · llm · render**. Each segment surfaces the
last emit from `ExtractionTelemetry.EmitStage(stage, started, detail, duration)`.

Real examples captured on the alpha.21 smoke (mostlylucid.net,
~200 KB response):

| Segment | Example | What it shows |
|---|---|---|
| **fetch** | `fetch Http+Captured+peak16473B/199506B · 227ms` | Fetcher (`Http` / `Playwright`) + streaming verdict (`Captured` / `Bailout` / `NoTemplate` / `Continue`) + bounded peak / total response / duration. `peak0B` when no scanner was built (first-visit NoTemplate path). |
| **match** | `match SlowPathMatch · 79ms` | StyloExtract `MatchStatus` enum value + match-stage duration. `FastPathHit` after the layout template is cached. |
| **llm** | `llm www.mostlylucid.net (det)` | Template YAML write detected by the `FileSystemWatcher` on the templates dir. `(det)` suffix when the write came from the heuristic deterministic sink; no suffix when LLM-induced; `(streaming)` when the streaming inducer wrote it; `(refit v2)` when alpha.18 streaming-template refit fires on drift. |
| **render** | `render 17 blocks · 27K` | Final block count + output markdown character count (rounded to K). |

A successful warm second-visit looks like:

```
fetch Http+Captured+peak16473B/199506B · 227ms · match FastPathHit · 79ms · llm www.mostlylucid.net (det) · render 17 blocks · 27K
```

The headline of the alpha.21 streaming work is that `peakNB` stays bounded
(O(chunk + longest tag), ~16 KiB on real input from HttpClient's
16 KiB chunks) regardless of response size — 8.26% ratio on a 200 KB body.
Pre-alpha.21 the in-flight buffer was capped at 1 MiB and grew
monotonically (would have been ~200 KB for the same scan).

Press **F2** (or click the status-bar segment) to open the full
Extraction Details panel — same data plus a per-extraction history and an
NDJSON export button.

---

## 7. Local NuGet feeds (and why)

`NuGet.Config` declares two local feeds beyond `nuget.org`. Both are
short-lived patches we drop the entry for once upstream lands them:

| Feed key | Path | Holds | Why local |
|---|---|---|---|
| `uitest-local` | `/tmp/uitest-local-feed` | `Mostlylucid.Avalonia.UITesting 1.4.3-local-fix1` | PressKey + KeyEventArgs.Source/KeyUp patch. Drop when upstream lands it. |
| `livemarkdown-local` | `/tmp/livemarkdown-local-feed` | `LiveMarkdown.Avalonia 1.9.2-local-imgfix2` | Fork of `DearVa/LiveMarkdown.Avalonia` with the HtmlInline/HtmlBlock `<img>` renderer needed for proper image dims. Consider upstream PR. |

Neither feed is in source control — they're built from sibling clones on
the maintainer's machine. If you need to build FULL fresh, pull the
sibling repos and `dotnet pack` into the matching `/tmp/` dir, or
temporarily comment out the `<add key="...">` lines and pin to the
nearest public versions (expect HTML-image regressions if you do).

---

## 8. Constraints (the rules this branch lives under)

These are pulled from
`docs/superpowers/plans/2026-06-25-lucidview-full.md` Global Constraints.
Anything that violates these is a defect, not a feature.

- **Lean Release behaviour MUST stay unchanged.** No edits to
  `MarkdownViewer.csproj` package refs. Lean source touches are permitted
  only when every change is runtime-neutral in Release — no new code
  paths exercised when `FULL` is not defined, any new XAML element starts
  `IsVisible="False"`, any new code-behind handler stubbed to no-op in
  lean. The exact list of permitted lean touches per task is enumerated
  in the plan.
- **FULL is allowed to be fat and non-AOT.** `PublishSingleFile=false`,
  `PublishReadyToRun=false`, `PublishTrimmed=false`. LlamaSharp and
  Playwright packages are explicitly `IsAotCompatible=false`.
- **`AssemblyName` stays `lucidVIEW`.** Renaming breaks the file-link
  model (see §1).
- **`RootNamespace=MarkdownViewer`** in the FULL csproj so shared
  file-linked source resolves the same namespaces.
- **Settings & state files** live under `AppPaths.LocalState`, never
  colliding with lean's settings folder.
- **CLI verbs exit without opening the UI.** Parsed in `Program.cs`
  before Avalonia starts.
- **Preview StyloExtract packages pinned at a tagged alpha** (currently
  `1.8.0-alpha.21`). When upstream cuts a new alpha, bump deliberately
  and smoke against the standard test pages (mostlylucid.net, BBC News).
- **Cut a new StyloExtract alpha if the preview API doesn't fit.** The
  sibling `stylobot-extract` repo is under our control. If a task hits a
  missing public API or a wrong shape, pop a new alpha from
  `stylobot-extract`, bump the version in FULL's csproj, and continue.
  Do NOT work around the upstream by reflecting into internals from FULL.
- **Tests for LLM/Playwright** are off CI initially. Use
  `[Trait("Category", "RequiresLlm")]` /
  `[Trait("Category", "RequiresPlaywright")]` and filter with
  `dotnet test --filter "Category!=RequiresLlm&Category!=RequiresPlaywright"`.
- **No release builds for FULL** until explicitly approved. Debug-only
  artefacts on CI.
- **UI changes verified via `Mostlylucid.Avalonia.UITesting`** when
  behaviour changes — the FULL csproj keeps the Debug-only harness reference.

---

## 9. Where to look next

- `docs/superpowers/specs/2026-06-25-lucidview-full-design.md` — original design spec.
- `docs/superpowers/plans/2026-06-25-lucidview-full.md` — implementation plan (9 tasks).
- `MarkdownViewer.Full/Services/HtmlToMarkdownServiceFull.cs` — the extraction-pipeline integration (Mode toggle, Playwright retry, telemetry emit).
- `MarkdownViewer.Full/Services/FullServices.cs` — DI wire-up + streaming-template store + LLM provider registration + template-YAML watcher.
- `MarkdownViewer.Full/Services/ExtractionTelemetry.cs` — stage enum, emit API, telemetry record contract.
- `MarkdownViewer.Full/Services/RenderedFetchPolicy.cs` — the static-vs-Playwright retry decision.
- `MarkdownViewer.Full/Services/ModelBootstrap.cs` — `--doctor` report assembly + lazy HF model download.
- `MarkdownViewer.Full/AppPaths.cs` — per-platform state directory resolution.
- `MarkdownViewer.Full/Models/AppSettings.Full.cs` — `AppSettingsFull` (LLM + Playwright + first-run state).
- `MarkdownViewer/Views/MainWindow.FileOperations.cs` — `DownloadWebPageAsync` with the `#if FULL` chunked-feed integration (`ReadBodyWithLimitAndScanAsync`).
- `MarkdownViewer.Full/Program.cs` — CLI verb parsing + `--shot` AutoShot state.
- `docs/streaming.md` in the `stylobot-extract` repo — streaming-mode reference (verdicts, sliding-window mechanics).
- `.superpowers/sdd/alpha19-streaming-report.md` — alpha.19 smoke metrics this guide originally cited (historical). The alpha.21 streaming work (Bloom early-reject + structural drift bailout) further reduces peak-buffered memory on real pages; see the lucidVIEW user-manual §20.2 for the alpha.21 headline numbers captured on mostlylucid.net.
