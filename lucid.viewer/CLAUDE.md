# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

lucidVIEW is a cross-platform Avalonia UI markdown viewer (.NET 10.0) with Mermaid diagram support via an embedded Naiad fork. Single-file executable, ~50MB. License: Unlicense.

## Project layout

- `MarkdownViewer/` — **lean** active project. AOT-clean, single-file Release. The shipped exe.
- `MarkdownViewer.Tests/` — lean's XUnit tests.
- `MarkdownViewer.Full/` — **FULL** sibling exe (dogfood-only, not shipped). File-links lean source + a small `MarkdownViewer.Full/` overlay, swaps in the preview StyloExtract LLM + Playwright stack. See `docs/full-edition.md`.
- `MarkdownViewer.Full.Tests/` — FULL's XUnit tests; LLM/Playwright tests gated behind `[Trait("Category", "RequiresLlm")]` / `RequiresPlaywright`.
- `MarkdownViewer.Browser/` — WASM build (file-links a small subset).
- `Naiad/` — embedded fork of the C# Mermaid renderer.
- `lucid.viewer/` — legacy net9.0 prototype (this folder).

## Build & Run Commands

```bash
# Lean — build / run / test
dotnet build  MarkdownViewer/MarkdownViewer.csproj -c Debug
dotnet run    --project MarkdownViewer/MarkdownViewer.csproj
dotnet test   MarkdownViewer.Tests/MarkdownViewer.Tests.csproj

# Run a single test
dotnet test MarkdownViewer.Tests/MarkdownViewer.Tests.csproj --filter "FullyQualifiedName~TestMethodName"

# Lean — publish (single exe, self-contained)
dotnet publish MarkdownViewer/MarkdownViewer.csproj -c Release -r win-x64
# Also: linux-x64, osx-x64, osx-arm64

# Cross-platform publish script
pwsh ./publish.ps1 -Platform all          # lean (default)
pwsh ./publish.ps1 -Edition full -Platform osx-arm64    # FULL — Debug-only artefacts

# FULL — build / run / test / headless screenshot
dotnet build MarkdownViewer.Full/MarkdownViewer.Full.csproj -c Debug
dotnet run   --project MarkdownViewer.Full/MarkdownViewer.Full.csproj -c Debug
dotnet test  MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj \
    --filter "Category!=RequiresLlm&Category!=RequiresPlaywright"

# FULL CLI verbs (exit before Avalonia starts)
dotnet run --project MarkdownViewer.Full -- --doctor
dotnet run --project MarkdownViewer.Full -- --install-browsers
dotnet run --project MarkdownViewer.Full -- --download-model
dotnet run --project MarkdownViewer.Full -- --shot https://www.mostlylucid.net /tmp/shot.png --wait 8000
```

Note: `lucid.viewer/` is a legacy net9.0 prototype — the active project is `MarkdownViewer/`.

## Architecture

**Entry flow:** `Program.cs` → `App.axaml.cs` (theme setup, library warning filtering) → `MainWindow.axaml.cs` (main UI with drag-drop, keyboard shortcuts, file handling)

**Services layer** (`MarkdownViewer/Services/`):
- **MarkdownService** — Core processing: metadata extraction from HTML comments, image path resolution (local/remote), Mermaid block→PNG rendering via Naiad, SVG foreignObject→text conversion, theme-aware color adjustment
- **ImageCacheService** — Caches remote images to local disk
- **ThemeService** — Applies one of 6 themes (Light, Dark, VSCode, GitHub, MostlyLucidDark, MostlyLucidLight) via Avalonia resource dictionaries
- **SearchService** — Full-text search within rendered markdown
- **NavigationService** — Table of contents extraction and navigation
- **PaginationService** — Page/section handling

**Models** (`MarkdownViewer/Models/`):
- `AppSettings` — Persistent user preferences (JSON in AppData via source-generated serialization context `AppSettingsContext`)
- `AppTheme` / `CodeTheme` / `ThemeDefinition` — Theme and syntax highlight enums + color palettes
- `DocumentMetadata` — Categories and publication dates extracted from markdown

**Naiad** (`Naiad/`) — Embedded C# mermaid rendering fork supporting 30+ diagram types.

## Key Patterns

- Markdown metadata uses HTML comment syntax: `<!--category-- tag1, tag2 -->` and `<datetime class="hidden">2026-01-14T12:00</datetime>`
- Mermaid rendering has 5 preprocessing strategies to handle different input formats; failures degrade gracefully to showing the code block
- Theme switching is real-time via Avalonia `DynamicResource` bindings
- Settings persist to `%APPDATA%/MarkdownViewer/settings.json`, crash logs to `crash.log` in same folder
- Library exceptions from Markdown.Avalonia (StaticBinding etc.) are filtered in `App.axaml.cs`

## Coding Style

- C# with nullable reference types enabled
- 4-space indentation, braces on new lines
- File-scoped namespaces (`namespace X;`)
- PascalCase for types/public members, camelCase for locals/parameters
- MVVM: view logic in `Views/`, business logic in `Services/`, data in `Models/`

## Naiad Security

The Naiad fork enforces resource limits for untrusted mermaid input: max 1000 nodes, 500 edges, 50KB input, 10s timeout. CSS injection protection, icon class validation, XSS prevention via XML/HTML encoding. FontAwesome CDN disabled by default. See `Naiad/SECURITY.md` for full threat model.

## CI/CD

- `.github/workflows/ci.yml` — Multi-platform matrix build + test (Windows/Ubuntu/macOS)
- `.github/workflows/release.yml` — Manual dispatch to create GitHub releases with all platform binaries
- `.github/workflows/store-publish.yml` — Windows Store publishing

## Publishing Notes

PublishTrimmed is **false** (required by Markdown.Avalonia). PublishReadyToRun and EnableCompressionInSingleFile are enabled.

## FULL edition

`MarkdownViewer.Full/` is a sibling exe that file-links lean source and adds the preview StyloExtract LLM + Playwright stack on top. Dogfood only — **not shipped in releases**, Debug-only CI artefact.

What FULL pulls in beyond lean (currently pinned at `1.8.0-alpha.19`):

- `Mostlylucid.StyloExtract.Core` + `.Templates` — full extraction pipeline with the SQLite layout-template store
- `Mostlylucid.StyloExtract.Streaming` — alpha.19 sliding-window byte-stream fence scanner with bounded memory
- `Mostlylucid.StyloExtract.Playwright` + `Microsoft.Playwright` 1.60.0 — rendered-DOM auto-retry for SPA/empty pages
- `Mostlylucid.StyloExtract.Llm.LlamaSharp` + `LLamaSharp` 0.27.0 (CPU backend) — in-process LLM template induction; qwen3.5:4b default, lazy HF download under `AppPaths.ModelCacheDir`
- `LiveMarkdown.Avalonia 1.9.2-local-imgfix2` — local fork with the HTML `<img width=H height=W>` renderer needed for proper image dims

Two `#if FULL` join points exist in shared lean source: DI wiring in `App.axaml.cs` and the status-bar slot + F2 keybind + chunked-feed integration in `MainWindow.axaml.cs` / `MainWindow.FileOperations.cs`. Lean Release output is byte-identical with FULL undefined.

`AssemblyName` stays `lucidVIEW` in FULL — lean's `avares://lucidVIEW/...` URIs compile against AssemblyName at build time, so renaming would break the file-link model. Identity surfaces at runtime via `<Product>lucidVIEW-FULL</Product>`, an overridden window title under `#if FULL`, the first-run dialog, and the status-bar segment.

**Local NuGet feeds** (`NuGet.Config`) — both are short-lived patches we drop once upstream lands them:

- `uitest-local` (`/tmp/uitest-local-feed`) — patched `Mostlylucid.Avalonia.UITesting 1.4.3-local-fix1` (PressKey + KeyEventArgs.Source/KeyUp).
- `livemarkdown-local` (`/tmp/livemarkdown-local-feed`) — fork of `DearVa/LiveMarkdown.Avalonia` at `1.9.2-local-imgfix2` (HtmlInline/HtmlBlock `<img>` renderer).

**Canonical design record:** the FULL edition was scoped in `docs/superpowers/specs/2026-06-25-lucidview-full-design.md` and built out under `docs/superpowers/plans/2026-06-25-lucidview-full.md` (9-task plan). Read those before changing FULL behaviour — they enumerate the permitted lean-source touches.

**Full guide:** `docs/full-edition.md` covers the dogfood pipeline step-by-step, the pipeline stage indicator format, CLI verb reference, settings/state directories, and the constraints this branch lives under.
