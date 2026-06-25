# lucidVIEW-FULL — design

**Status:** Design approved 2026-06-25. Awaiting implementation plan.
**Lean app:** `MarkdownViewer/` (stays unchanged, AOT-conscious, single-file, small).
**New sibling:** `MarkdownViewer.Full/` (dogfood-only, allowed to be fat and non-AOT).

## Why

The preview StyloExtract stack (`Mostlylucid.StyloExtract.Llm.LlamaSharp`,
`Mostlylucid.StyloExtract.Playwright`, plus `Mostlylucid.StyloExtract.Templates`
for the SQLite template store) introduces in-process LLM template induction and
a Chromium-backed rendered-DOM fetcher. Both packages are explicitly
`IsAotCompatible=false` and pull native binaries (LLamaSharp backends,
Playwright browsers). They cannot land in the lean `MarkdownViewer` Release —
that build's contract is "tiny, single-file, AOT-capable" and must not
regress.

lucidVIEW-FULL is a **sibling binary** whose only job is to provide a realistic,
real-world workload for the preview stack so the upstream library can iterate
with a tight feedback loop. It is **not** a product, has **no** new user-facing
AI features, and ships independently from lucidVIEW.

## Constraints (from existing memory / repo state)

- Lean Release **behaviour** must stay tiny and AOT-capable. No edits to
  `MarkdownViewer.csproj` package refs. Lean source edits are permitted only
  when every change is **runtime-neutral in Release**: no new code path
  executed when `FULL` is not defined; new XAML elements start
  `IsVisible="False"`; new code-behind handlers no-op in lean.
- Lean source files compile bit-identical for FULL — share by file-link, not
  by extracted Core lib. (Refactoring lean into a Core lib is out of scope.)
- Releases need explicit approval; FULL is Debug-only artifacts on CI
  initially. Release/sign/store-publish for FULL is out of scope until the
  user explicitly opts in.
- UI fixes must still be verifiable via `Mostlylucid.Avalonia.UITesting`. The
  FULL csproj keeps the same Debug-only UI testing dependency.
- **Cut a new `stylobot-extract` alpha if the preview API doesn't fit.** The
  sibling repo is under our control. If FULL hits a missing public API or a
  wrong shape, pop a new alpha from `stylobot-extract` rather than reflecting
  into internals or working around it inside FULL.

## Scope

### In scope

1. New project `MarkdownViewer.Full/` — sibling Avalonia desktop exe.
2. New project `MarkdownViewer.Full.Tests/` — smoke tests for the FULL
   pipeline (lean tests untouched).
3. `MarkdownViewer.Full/Services/HtmlToMarkdownServiceFull.cs` — replacement
   for the lean `HtmlToMarkdownService` using the full
   `Mostlylucid.StyloExtract.Core` `ILayoutExtractor` with the SQLite template
   store.
4. `PlaywrightHtmlFetcher` integration with **automatic retry** when the
   plain-HTTP fetch returns empty / SPA-marker HTML / very few blocks.
5. `LlamaSharpTextProvider` integration as the `ILlmTextProvider` for the
   StyloExtract LLM template inducer. Lazy auto-download on first call,
   matching the stylobot pattern.
6. First-run bootstrap dialog offering to pre-download model + Playwright
   browsers; both independently skippable; missing components degrade
   gracefully (heuristic-only / HTTP-only).
7. CLI verbs for pre-bootstrapping: `--download-model [<hf-id>]`,
   `--install-browsers`, `--doctor`.
8. `LastExtractionInfo` telemetry + status-bar line + F2 `Extraction Details`
   panel + NDJSON export.
9. `publish.ps1 -Edition Lean|Full|All`.

### Out of scope

- AI features on documents (chat, summarize, auto-tag, translate, etc.).
- Browser-shell UI (tabs, URL bar, history sidebar). The reader UI is
  unchanged.
- Extracting a `MarkdownViewer.Core` library.
- Release builds / code-signing / store publishing for FULL.
- WASM head (`MarkdownViewer.Browser`) — untouched.

## Project layout

```
lucidview/
  MarkdownViewer/                       (unchanged — lean)
  MarkdownViewer.Full/                  (new)
    MarkdownViewer.Full.csproj
    Program.cs                          (CLI verbs + Avalonia start)
    App.axaml.cs                        (DI wiring; #if FULL hook in lean's App copy)
    Services/
      HtmlToMarkdownServiceFull.cs      (replaces lean equivalent)
      ModelBootstrap.cs                 (stylobot-pattern bootstrap helper)
      ExtractionTelemetry.cs            (LastExtractionInfo + circular buffer)
    Views/
      FirstRunBootstrapDialog.axaml(.cs)
      ExtractionDetailsPanel.axaml(.cs)
    Models/
      AppSettings.Full.cs               (FULL-only settings additions)
  MarkdownViewer.Full.Tests/            (new)
    MarkdownViewer.Full.Tests.csproj
    HtmlToMarkdownServiceFullTests.cs   (smoke tests, [Trait] for LLM/Playwright)
```

### Shared source via file-link

Same pattern as `MarkdownViewer.Browser/MarkdownViewer.Browser.csproj` already
uses for `DiagramCanvas.cs`. In `MarkdownViewer.Full.csproj`:

```xml
<Compile Include="..\MarkdownViewer\**\*.cs"
         Exclude="..\MarkdownViewer\Services\HtmlToMarkdownService.cs"
         Link="%(RecursiveDir)%(Filename)%(Extension)" />
<AvaloniaXaml Include="..\MarkdownViewer\**\*.axaml"
              Link="%(RecursiveDir)%(Filename)%(Extension)" />
<AvaloniaResource Include="..\MarkdownViewer\Assets\**\*"
                  Link="Assets\%(RecursiveDir)%(Filename)%(Extension)" />
<Content Include="..\MarkdownViewer\Assets\manual\**\*"
         Link="manual\%(RecursiveDir)%(Filename)%(Extension)"
         CopyToOutputDirectory="PreserveNewest" />
```

The lean `HtmlToMarkdownService.cs` is excluded from the link set because FULL
provides its own.

### #if FULL join points

Lean source touches enumerated. Every one is runtime-neutral in Release:

- `MainWindow.axaml.cs:29` — `IHtmlToMarkdownService` field initialiser
  switches between `new HtmlToMarkdownService()` (lean) and
  `FullServices.Get<IHtmlToMarkdownService>()` (FULL).
- `MainWindow.axaml` — `IsVisible="False"` slots for the Help → Diagnostics
  submenu (3 items) and the status-bar `ExtractionStatusText` TextBlock.
- `MainWindow.axaml.cs` ctor — a `#if FULL` block that flips the new
  XAML elements visible, posts the first-run dialog, subscribes the status
  bar to telemetry, and binds F2. Click handlers for the Help menu items
  exist as no-op stubs in lean so the XAML resolves; their bodies are
  `#if FULL`-guarded.
- DI wiring lives in `MarkdownViewer.Full/Services/FullServices.cs` (a static
  service locator) rather than `App.axaml.cs` — keeps lean's `App.axaml.cs`
  untouched.

Everything else lives under `MarkdownViewer.Full/`. Lean compiles and runs
with zero observable difference vs. before these changes.

## csproj differences from lean

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <ApplicationManifest>..\MarkdownViewer\app.manifest</ApplicationManifest>

  <AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>

  <!-- AssemblyName MUST stay 'lucidVIEW' — lean's App.axaml, MainWindow.axaml,
       and AppSettings.cs hardcode avares://lucidVIEW/... URIs which Avalonia
       compiles against AssemblyName. Renaming requires shadowing those three
       files in FULL, which defeats the file-link model. Identity is surfaced
       at runtime via Product, MainWindow.Title (set under #if FULL), and the
       first-run dialog / status-bar text. -->
  <AssemblyName>lucidVIEW</AssemblyName>
  <RootNamespace>MarkdownViewer</RootNamespace>  <!-- match lean for shared source -->
  <Version>0.1.0</Version>
  <Product>lucidVIEW-FULL</Product>
  <Description>Dogfood build of lucidVIEW with the preview StyloExtract LLM + Playwright stack</Description>

  <DefineConstants>$(DefineConstants);FULL</DefineConstants>

  <!-- NOT shipped as single-file: Playwright browsers + native LLM libs
       cannot live inside the bundle. -->
  <PublishSingleFile>false</PublishSingleFile>
  <SelfContained>true</SelfContained>
  <PublishReadyToRun>false</PublishReadyToRun>
  <PublishTrimmed>false</PublishTrimmed>
</PropertyGroup>
```

Added package references (on top of lean's existing set):

```xml
<PackageReference Include="Mostlylucid.StyloExtract.Core"          Version="<preview>" />
<PackageReference Include="Mostlylucid.StyloExtract.Llm.LlamaSharp" Version="<preview>" />
<PackageReference Include="Mostlylucid.StyloExtract.Playwright"     Version="<preview>" />
<PackageReference Include="Mostlylucid.StyloExtract.Templates"      Version="<preview>" />
<PackageReference Include="LLamaSharp"                              Version="<latest>" />
<PackageReference Include="LLamaSharp.Backend.Cpu"                  Version="<latest>" />
<PackageReference Include="Microsoft.Playwright"                    Version="<latest>" />
```

The `<preview>` and `<latest>` placeholders are pinned at implementation time
by reading `stylobot-extract/Directory.Packages.props` first (the sibling
repo is the source of truth for preview-package versions). They are
deliberately left unpinned in this spec because the preview train moves
faster than this document.

Lean's StyloExtract 1.7.1 refs (`Abstractions`, `Html`, `Heuristics`,
`Markdown`) are pulled in transitively via `Core` for FULL — no version
conflict expected; if one appears, FULL overrides.

## Model & browser bootstrap (stylobot pattern)

Pattern source: `stylobot/src/Mostlylucid.BotDetection.Llm.LlamaSharp/LlamaSharpProviderOptions.cs`
+ `LlamaSharpLlmProvider.cs`. Mirrored, not duplicated.

### Settings (`AppSettings.Full.cs`)

| Setting | Default | Notes |
|---|---|---|
| `LlmModelPath` | `Qwen/Qwen2.5-0.5B-Instruct-GGUF/qwen2.5-0.5b-instruct-q4_k_m.gguf` | Local `.gguf` path OR HF identifier. Matches stylobot default — ~400 MB, viable on CPU. |
| `LlmModelCacheDir` | `AppPaths.LocalState/models` | Env override: `LUCIDVIEW_MODEL_CACHE`. |
| `LlmEnabled` | `true` | Toggle in settings; OFF → heuristic-only StyloExtract. |
| `PlaywrightEnabled` | `true` | OFF → HTTP-only fetch (same as lean). |
| `PlaywrightBrowsersDir` | Playwright default (`%USERPROFILE%/AppData/Local/ms-playwright` etc.) | Env override: `PLAYWRIGHT_BROWSERS_PATH` (Playwright's own). |
| `LlmContextSize` | `512` | Matches stylobot default. |
| `LlmThreads` | `Environment.ProcessorCount` | |
| `LlmGpuLayerCount` | `-1` | Full offload on Metal/CUDA where available; CPU otherwise. |

### First-run dialog

`FirstRunBootstrapDialog` shows once (gated on `AppSettings.Full.HasRunBefore`):

> **lucidVIEW-FULL** uses an embedded language model and a headless browser to
> exercise the preview StyloExtract stack on real-world pages.
>
> - Language model: ~400 MB, downloaded from Hugging Face on demand.
> - Playwright Chromium: ~150 MB, installed via Microsoft's installer.
>
> Both are independently skippable. Without them, FULL falls back to the same
> behavior as the lean lucidVIEW.
>
> [Download both] [Defer — fetch on first use] [Skip — heuristic only]

### CLI verbs

Parsed in `Program.cs` before Avalonia starts. Exit after running; do not open
the UI.

| Verb | Behaviour |
|---|---|
| `--download-model [<hf-id>]` | Force-download the configured (or specified) model into `LlmModelCacheDir`. Prints final path. |
| `--install-browsers` | `PlaywrightInstaller.EnsureBrowsersInstalled("chromium")`. |
| `--doctor` | Print: configured model path + size + present?, browsers path + present?, expected disk footprint, "ready to extract: yes/no". Non-zero exit if not ready. |

UI surface (`Help → Diagnostics → Re-download model` / `Reinstall browsers`)
calls into the same code paths.

### Lazy auto-download

`LlamaSharpTextProvider.InitializeAsync` (stylobot-style) auto-downloads on
first inference call when `ModelPath` doesn't exist on disk. Single
`SemaphoreSlim`-serialised init + inference (matches stylobot — concurrent
calls queue). Progress events bubble to the status bar.

## Dogfood extraction pipeline

`HtmlToMarkdownServiceFull` is the FULL-side replacement for the lean
`HtmlToMarkdownService`. The signature changes (lean's `Convert` is sync;
FULL's `ConvertAsync` is async because the LLM template inducer is async):

```csharp
public sealed class HtmlToMarkdownServiceFull
{
    public Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct);
    public static bool LooksLikeHtml(string body);  // unchanged from lean
}
```

The handful of lean call sites in `MainWindow.FileOperations.cs` are already
on async paths, so the change is local. Touched via `#if FULL` where the call
site needs to know which variant it's calling.

### Fetch path

The `OpenWebPageAsync` flow (lean's `Ctrl+Shift+W`) gets, on the FULL side,
this sequence:

1. `HttpClient.GetStringAsync` (with the existing `UserAgent` set).
2. If response is empty, or `SpaDetection.LooksLikeSpa(html)` is true, or
   first-pass extraction yields fewer than N blocks (start with N=3, tune
   during dogfooding):
   - Ensure Playwright browsers installed (auto-install with status-bar
     progress if missing).
   - Retry via `PlaywrightHtmlFetcher.FetchAsync(uri, new RenderOptions())`.
3. Pass HTML + final URI to `ILayoutExtractor.ExtractAsync`.

No user toggle for the retry — it's the dogfood happy path. The fetcher
actually used is recorded in `LastExtractionInfo` so we can see how often
Playwright is firing.

### Extract path

DI:

```csharp
services.AddStyloExtract(o =>
{
    o.StorePath = Path.Combine(AppPaths.LocalState, "styloextract-templates.db");
    o.DefaultProfile = ExtractionProfile.RagFull;
});
services.AddStyloExtractLlamaSharp(o => { /* from AppSettings.Full */ });
services.AddStyloExtractLlmInducer(Path.Combine(AppPaths.LocalState, "templates"));
services.AddSingleton<IRenderedHtmlFetcher, PlaywrightHtmlFetcher>();
services.AddSingleton<ExtractionTelemetry>();
```

Lean's existing pre-process steps (`PromoteHtmxLinks`, `TagMermaidPres`)
remain — they're orthogonal to StyloExtract.

Template store at `AppPaths.LocalState/styloextract-templates.db` is the
"learning" surface: fingerprints accumulate across sessions, refits happen
automatically, `TemplateVersionDiff` events get fed into
`ExtractionTelemetry`.

### `AppPaths` resolution

A small FULL-only static helper resolving the per-platform "local state"
directory once. Mirrors the lean `MarkdownViewer` settings location pattern:

| Platform | `AppPaths.LocalState` |
|---|---|
| Windows | `%LOCALAPPDATA%\lucidVIEW-FULL\` |
| macOS   | `~/Library/Application Support/lucidVIEW-FULL/` |
| Linux   | `${XDG_STATE_HOME:-~/.local/state}/lucidview-full/` |

Created on first access if missing. Settings JSON also lives here, separate
from the lean `MarkdownViewer` settings file so the two apps never collide.

## Telemetry surface

```csharp
public sealed record LastExtractionInfo(
    DateTime When,
    Uri Source,
    MatchStatus Status,        // FastPathHit | SlowPathMatch | Novel | Refit
    Guid TemplateId,
    int TemplateVersion,
    Fetcher Fetcher,           // Http | Playwright
    TimeSpan FetchDuration,
    bool LlmInductionFired,
    TimeSpan LlmDuration,
    int BlockCount,
    int OutputCharacterCount);

public sealed class ExtractionTelemetry
{
    public LastExtractionInfo? Last { get; }
    public IReadOnlyList<LastExtractionInfo> Recent { get; }  // last 50, circular
    public event Action<LastExtractionInfo>? Recorded;
    public string ExportNdjson();
}
```

### Status-bar line (always visible in FULL)

Format:
```
✓ <Host> tmpl <v3> · <fetcher> · <fetch ms> · <blocks> blocks [· LLM <ms>]
```

Click → opens the details panel.

### `Help → Extraction Details` (F2)

Single panel showing:
- Full dump of `Last`.
- Tail of `Recent` as a `DataGrid` (When, Host, Status, Template, Fetcher,
  LLM, Blocks).
- `Export NDJSON…` button → file-save dialog, writes
  `ExtractionTelemetry.ExportNdjson()` for sharing with the StyloExtract repo.
- `Clear` button → reset the buffer.

No charts, no graphs, no live event stream — keep the panel cheap.

## Build / publish / CI

### `publish.ps1`

Add `-Edition` parameter with values `Lean` (default, current behaviour),
`Full`, or `All`. Full editions publish per-RID without single-file:

```powershell
dotnet publish MarkdownViewer.Full/MarkdownViewer.Full.csproj `
  -c Release -r $rid `
  -o publish/full/$rid
```

After publish, copy Playwright browser binaries into `publish/full/$rid/.playwright/`
via `playwright.ps1 install --with-deps chromium` (run from the published
output directory) so the produced bundle is self-contained.

### CI

`.github/workflows/ci.yml`:
- Lean matrix unchanged.
- Add FULL matrix (Windows/Ubuntu/macOS), Debug build only initially. Run
  `MarkdownViewer.Full.Tests` with `RequiresLlm=false` filter (heavy tests
  off CI).
- No release workflow changes. FULL release is not on the table until
  explicitly approved.

### Tests

`MarkdownViewer.Full.Tests`:
- XUnit, references `MarkdownViewer.Full` directly (no file-link gymnastics
  needed for tests).
- `HtmlToMarkdownServiceFullTests`:
  - `Convert_PlainHtml_ProducesMarkdown` — fixture from `tests/`, no
    Playwright, no LLM. Smoke.
  - `Convert_SpaMarker_RetriesViaPlaywright` —
    `[Trait("RequiresPlaywright", "true")]`, skipped on CI.
  - `Convert_NovelLayout_InvokesLlmInducer` — `[Trait("RequiresLlm", "true")]`,
    skipped on CI.
- `ExtractionTelemetryTests` — circular-buffer + NDJSON-export unit tests.

## Implementation order

Each step independently shippable / dogfoodable:

1. **Skeleton** — `MarkdownViewer.Full.csproj` with file-links + `Program.cs`
   + titlebar `lucidVIEW-FULL`. Builds, runs, identical behavior to lean.
   Add `MarkdownViewer.Full.Tests` with one passing smoke test.
2. **StyloExtract Core swap** — `HtmlToMarkdownServiceFull` using
   `ILayoutExtractor` + SQLite template store. **No** Playwright or LLM yet.
   Verify open-web-page still works on plain pages and that the template
   store starts accumulating.
3. **Playwright** — wire `PlaywrightHtmlFetcher` + auto-retry trigger +
   `--install-browsers` CLI verb. Verify on a known SPA.
4. **LlamaSharp** — wire `LlamaSharpTextProvider` + lazy auto-download +
   `--download-model` CLI verb. Verify the LLM induction path fires on a
   novel layout.
5. **Bootstrap UX** — `FirstRunBootstrapDialog` + `--doctor` verb +
   `Help → Diagnostics` UI surfaces.
6. **Telemetry** — `LastExtractionInfo` + status-bar line + F2 details panel
   + NDJSON export.

## Risks & open questions

- **Preview package versioning.** `stylobot-extract` is in active flux.
  Pin the FULL csproj to whatever preview NuGet version the sibling
  `stylobot-extract/Directory.Packages.props` lists at implementation time.
  If a needed API is missing, cut a new alpha rather than working around it
  (see Constraints).
- **macOS Apple Silicon LLM perf.** `LLamaSharp.Backend.Cpu` covers it but
  Metal acceleration on the 0.5B Qwen model needs validation. If perf is
  insufficient the default model can be swapped without changing the design.
- **Playwright browser path on macOS.** Default lives under
  `~/Library/Caches/ms-playwright`, which is fine for dev but may not survive
  a published bundle. The publish step needs to bake browsers into the
  published output dir — confirmed via `PLAYWRIGHT_BROWSERS_PATH` override.
- **First-pass block-count threshold.** N=3 is a guess for "did extraction
  actually find anything". Tune during dogfooding; expose as a setting if it
  turns out site-dependent.
