# Naiad WebAssembly Feasibility and Naming

Date: 2026-02-20

## Recommended Naming

- Core engine: `Naiad` (keep as-is)
- Browser-WASM export assembly: `Naiad.Wasm`
- Optional WASI component-model package: `Naiad.Component`
- Avalonia browser host: `MarkdownViewer.Browser` (or productized `lucidVIEW.Browser`)
- Avalonia desktop host: `MarkdownViewer.Desktop`
- Shared UI/core: `MarkdownViewer` (no desktop/browser-specific lifetime code)

`Avalonia.Browser` conventions align with `*.Browser` project naming and `netX.Y-browser`.

## Current State (this repo)

- `Naiad` was `net10.0` only.
- `MarkdownViewer` is desktop-coupled (`Avalonia.Desktop`, desktop lifetime, process/file APIs, Playwright print path).
- Mermaid rendering core is mostly pure .NET and browser-portable.

## What Was Added in This Spike

1. `Naiad` now multi-targets:
   - `net10.0`
   - `net10.0-browser`
2. Browser-safe timeout behavior:
   - `SecurityValidator.WithTimeout()` now bypasses blocking `Task.Wait()` on browser runtime.
   - Browser relies on existing size/complexity limits for protection.
3. New project: `Naiad.Wasm`
   - Targets `net10.0-browser`
   - Exposes `[JSExport]` entry points:
     - `RenderSvg`
     - `RenderSvgDocumentJson`
     - `DetectDiagramType`
     - `Health`
4. Added to `Naiad.slnx`.
5. New project: `Naiad.Wasm.Host`
   - `Microsoft.NET.Sdk.WebAssembly`
   - Minimal HTML/JS harness calling `Naiad.Wasm` exports from browser JavaScript.
   - Includes reusable `NaiadClient` module wrapping runtime init + typed API calls.
   - Includes standards-based custom element `<naiad-diagram>` for plain HTML pages.
6. New project: `MarkdownViewer.Browser`
   - Avalonia browser spike (`Avalonia.Browser` + `net10.0-browser`)
   - References `Naiad` and renders Mermaid in-browser.
   - Reuses existing `DiagramCanvas` for native Avalonia vector rendering (no Skia raster fallback).
7. New package scaffold: `Naiad.Wasm.Npm`
   - npm package metadata, typed declarations, build script
   - `npm run build` publishes `Naiad.Wasm.Host` and syncs `dist/`
   - Styling/theming documentation for `<naiad-diagram>`

## Why This Split

- `Naiad` remains the reusable engine for desktop, server, and tests.
- `Naiad.Wasm` contains browser interop/export concerns only.
- Keeps web-facing API versioning explicit without polluting the core assembly API.

## Avalonia Browser Viewer Feasibility

Feasible, but this is not a simple retarget of current `MarkdownViewer.csproj`.

Minimum reshape:

1. Move shared UI/services into `MarkdownViewer` library (no platform lifetime logic).
2. Create `MarkdownViewer.Desktop`:
   - `Avalonia.Desktop`
   - `StartWithClassicDesktopLifetime`
3. Create `MarkdownViewer.Browser`:
   - `Microsoft.NET.Sdk.WebAssembly`
   - `TargetFramework` = `net10.0-browser`
   - `Avalonia.Browser`
   - `StartBrowserAppAsync("out")`
4. Replace/guard desktop-only features in browser host:
   - Shell launches (`Process.Start`)
   - command-line load path
   - direct filesystem cache paths
   - Playwright/print-to-PDF flow

## Risk Notes

- Browser runtime cannot forcibly cancel synchronous CPU work the same way as desktop threads.
- File caching and local-path semantics must move to browser-friendly abstractions.
- Export/print UX likely diverges between desktop and browser hosts.

## Phase Plan

### Phase 1 (done in this spike)

- Browser target for core Naiad.
- Basic wasm export assembly.

### Phase 2

- Add JS glue package/output contract for `Naiad.Wasm`.
- Create browser integration smoke test (render a simple flowchart).
- (Completed spike) `Naiad.Wasm.Host` demonstrates direct JavaScript -> `Naiad.Wasm` call path.
- (Completed spike) `NaiadClient` browser module provides a cleaner JavaScript API surface.
- (Completed spike) `<naiad-diagram>` web component works in `plain-web-component.html`.

### Phase 3

- Split viewer into shared + desktop + browser projects.
- Port viewer features with browser-specific implementations.
- (Started spike) `MarkdownViewer.Browser` confirms Avalonia browser host shape works with `Naiad`.
- (Started spike) Browser app already renders diagrams through shared `DiagramCanvas`.

### Phase 4

- Optional `Naiad.Component` for WASI component model after toolchain maturity and explicit runtime target decision.
