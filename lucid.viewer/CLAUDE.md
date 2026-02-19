# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

lucidVIEW is a cross-platform Avalonia UI markdown viewer (.NET 10.0) with Mermaid diagram support via an embedded Naiad fork. Single-file executable, ~50MB. License: Unlicense.

## Build & Run Commands

```bash
# Build
dotnet build MarkdownViewer/MarkdownViewer.csproj -c Debug

# Run
dotnet run --project MarkdownViewer/MarkdownViewer.csproj

# Test (XUnit)
dotnet test MarkdownViewer.Tests/MarkdownViewer.Tests.csproj

# Run a single test
dotnet test MarkdownViewer.Tests/MarkdownViewer.Tests.csproj --filter "FullyQualifiedName~TestMethodName"

# Publish (single exe, self-contained)
dotnet publish MarkdownViewer/MarkdownViewer.csproj -c Release -r win-x64
# Also: linux-x64, osx-x64, osx-arm64

# Cross-platform publish script
pwsh ./publish.ps1 -Platform all
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
