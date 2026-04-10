# Changelog

All notable changes to lucidVIEW are documented here. Format loosely based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions follow
[SemVer](https://semver.org/).

## v2.1.0 — 2026-04-10

### Added

- **Self-documenting in-app User Manual** (`F1`) — bundled at
  `manual/user-manual.md` next to the binary, with 17 screenshots automatically
  captured by the UI testing harness via `ux-scripts/capture-manual.yaml`. The
  manual covers every feature with real screenshots that always match the
  current build. Re-generate by running the capture script. `Shift+F1` still
  opens the README.
- **Pride theme** — a celebration palette using the Pride flag colors as
  borders, accents, and section dividers. Tasteful enough to read code in,
  bright enough to show off.
- **Configurable custom theme** — define your own palette in
  `settings.json` under `customTheme` and select **Custom** from the side panel.
  See `default-settings.json` for the schema and a Solarized-ish example. The
  Custom theme card only appears when a custom theme is configured.
- **UI polish** — header bar got a 2px accent-coloured bottom border, font A
  buttons are now grouped in a rounded pill with proper spacing instead of
  cramping against the macOS window controls.
- **Real Print function** (`Ctrl+P`) — sends the current document to the OS
  default printer. Generates a temp PDF via QuestPDF then hands it to
  ShellExecute (`print` verb) on Windows or `lp` (CUPS) on macOS/Linux.
- **macOS `.app` bundle** — `pwsh ./publish.ps1 -Platform osx` now produces
  `lucidVIEW.app` with proper `Info.plist`, `Resources/lucidVIEW.icns`,
  ad-hoc codesigning, and file-type associations for `.md` / `.markdown` /
  `.mdown` / `.mkd`. Double-clicking the bundle launches the GUI directly with
  no Terminal window and a Dock icon. Both `osx-x64` and `osx-arm64` are
  produced. The release CI workflow assembles the same bundle.
- **Windows Store (MSIX) prep** — `pack-msix.ps1` produces a signable
  `publish/store/lucidVIEW.msix` ready for Partner Center upload. The
  `Package.appxmanifest` now uses build-time identity tokens, the
  `MarkdownViewer/Assets/Store/` folder ships eight tile/logo PNGs, and a
  manual-dispatch `store-publish.yml` GitHub Actions workflow builds the MSIX
  on `windows-latest`. Full setup walkthrough in `docs/windows-store.md`.
- **UI testing harness (Debug only)** — Wired up
  [`Mostlylucid.Avalonia.UITesting`](https://github.com/scottgal/lucidRESUME/tree/master/src/Mostlylucid.Avalonia.UITesting)
  via `--ux-test`, `--ux-repl`, `--ux-mcp` startup flags. Three YAML scripts
  in `ux-scripts/` exercise every non-dialog function, every dialog, and the
  hand-driven REPL/MCP modes. The harness is excluded from Release builds
  via a Debug-only `<ProjectReference>` and `#if DEBUG` guards — Release
  binaries stay tiny and AOT-capable.
- `NavigateCommand` on `MainWindow` (Debug-only consumer; routes to
  `LoadFile`). Lets the YAML test scripts self-bootstrap by loading a
  fixture markdown file via the standard `Navigate` action.
- `docs/macos-bundle.md`, `docs/windows-store.md`, and `ux-scripts/README.md`.

### Changed

- **`Ctrl+P` is now real Print**, not Export PDF. Export PDF moved to
  `Ctrl+Shift+P` (also still available via the side-panel menu). The side
  panel now lists "Print (Ctrl+P)" above "Export PDF... (Ctrl+Shift+P)".
- `publish.ps1` adds `osx-arm64` and reworks the platform list — `osx`
  shorthand now builds both Intel and Apple Silicon variants.
- `release.yml` workflow assembles macOS `.app` bundles before zipping the
  artifacts so downloads launch as GUI apps on the receiving Mac.
- Bumped `MaxVersionTested` in the Store manifest from `10.0.22621.0` to
  `10.0.26100.0` (Windows 11 24H2).

### Fixed

- **Pre-existing build break** in `Naiad/QuadrantParser.cs`: `c is not '-' or ' '`
  parses as `(c is not '-') or (c is ' ')` which is always true. Corrected
  to `c is not ('-' or ' ')`. Fixes CS9336 redundant-pattern errors that
  were blocking all builds on the .NET 10 SDK.
- **Pre-existing security warning** NU1903 — `Tmds.DBus.Protocol` 0.21.2
  (transitive via Avalonia) had a high-severity vulnerability
  ([GHSA-xrw6-gwf8-vvr9](https://github.com/advisories/GHSA-xrw6-gwf8-vvr9)).
  Pinned to 0.92.0.
- **Wrong `Executable` in `Package.appxmanifest`** — was `MarkdownViewer.exe`,
  the real assembly is `lucidVIEW.exe`. The MSIX would have failed at launch.
- xUnit2013 warning in `AvaloniaNativeDiagramRendererPluginTests` — replaced
  `Assert.Equal(0, count)` with `Assert.Empty(...)`.

### Internal

- Refactored `PdfExportService` — added `ExportToTempAsync` so the new Print
  path can hand a temp PDF to the print service without duplicating the
  PDF-build logic.
- Added `MarkdownViewer/Services/PrintService.cs`.
- All MSBuild changes that reference UITesting are conditional on
  `'$(Configuration)' == 'Debug'` so the Release pipeline doesn't even see
  the testing harness.
- **Fixed `UseUITesting()` upstream bug** in
  `Mostlylucid.Avalonia.UITesting`: the extension hooked `AfterSetup` which
  fires before `App.OnFrameworkInitializationCompleted` sets `MainWindow`,
  so `UITestingStartup.AttachToApplication` would silently bail. Re-routed
  through the `IClassicDesktopStyleApplicationLifetime.Startup` event so the
  harness only attaches once `MainWindow` actually exists. Without this fix
  every `--ux-test`/`--ux-repl`/`--ux-mcp` invocation was a no-op.
- `ThemeService` learned to render a runtime `ThemeDefinition` (not just
  the static built-ins), used for both `AppTheme.Custom` and any future
  config-driven themes.
- `MainWindow.NavigateCommand` (Debug-only consumer) routes the YAML
  `Navigate` action to `LoadFile`.

## Earlier releases

Earlier releases (v0.0.x → v1.0.1) predate this changelog. See the
[GitHub Releases page](https://github.com/scottgal/lucidview/releases).
