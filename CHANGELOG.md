# Changelog

All notable changes to lucidVIEW are documented here. Format loosely based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions follow
[SemVer](https://semver.org/).

## v2.2.3 — 2026-04-11

### Changed (rip and replace, not patch)

- **Ruler architecture rewritten — alignment is now layout-driven, not math.**
  Ripped out the entire `UpdateRulerHandlesFromWidth` / `TransformToVisual` /
  `RulerCanvas` / scrollbar / gutter / scale / fallback math. The ruler bar
  is now a `Grid` *inside* the `LayoutTransformControl`, in a `StackPanel`
  above the document `Border`. The bar's `Width` is bound to the Border's
  `Width` via XAML `ElementName` binding, so they are always the same logical
  width. Handles use `HorizontalAlignment=Left/Right` with negative margins
  to extend slightly past the column edges. Avalonia's layout engine handles
  every alignment concern: scrollbar, centering, scale transform, gutter,
  padding — all gone, all replaced by one ElementName binding.
- Code-behind shrank from ~230 lines to ~85 lines. The remaining handlers
  do exactly three things: change `Border.Width` on drag, change `Border.Width`
  on click, update the readout text.
- Zoom slider, font size, fit modes — they all "just work" because the
  ruler is inside the same `LayoutTransformControl` as the document, so it
  scales with the column automatically. No `RefreshRulerForScaleChange`
  plumbing required.

### Verified

- **Release build does NOT include `Mostlylucid.Avalonia.UITesting`**. The
  Debug-only `Condition="'$(Configuration)' == 'Debug'"` on the
  `<PackageReference>` is honoured by MSBuild and `obj/Release/` contains
  no UITesting traces. The recent ~5 MB Release size growth (~73 MB → ~78 MB)
  is from `FluentAvaloniaUI` which IS a Release dependency, added in v2.1.1
  for the polished button styling.

## v2.2.2 — 2026-04-11

### Fixed

- **Ruler handles align with the actual card outline at every scale.** The
  Border had `HorizontalAlignment="Center"` which made it shrink to fit the
  content's natural width rather than honour `MaxWidth`. So the visible
  card was sitting at the *content* width (e.g. 616px), not the configured
  900px column, and the handles were correctly placed on the *configured*
  edges — which were nowhere near the visible card edges. Switched to
  `Border.Width = ContentMaxWidth` (an explicit width, not a cap) so the
  Border is forced to the column width regardless of content.
- **Width persists immediately on drag.** `OnRulerHandleDragDelta` now
  calls `_settings.Save()` after each delta. The settings file is tiny so
  the write cost is negligible. Survives unexpected app exit, not just a
  clean window close.

### Changed

- **`Mostlylucid.Avalonia.UITesting` is now consumed via NuGet (1.1.0)**
  instead of a `<ProjectReference>` to the local lucidRESUME working tree.
  Decouples the lucidVIEW Debug build from in-progress lucidRESUME edits.
  The package is still gated to Debug-only so Release builds stay tiny and
  AOT-friendly.

### Internal

- Added a `LUCIDVIEW_RULER_DEBUG=1` environment variable that prints the
  computed ruler alignment values to `Console.WriteLine` on every layout
  update. Avoids having to record large GIFs to diagnose alignment drift.
- All ruler logic now reads/writes `MarkdownContentBorder.Width` instead
  of `.MaxWidth` (drag handler, click handler, scale refresh, layout
  subscription, width readout).

## v2.2.1 — 2026-04-11

### Fixed

- **Ruler handles now sit *exactly* on the visible Border outline** at all
  zoom levels. Replaced the hand-rolled position math with
  `TransformToVisual(MarkdownContentBorder → RulerCanvas)` so the handles
  use the actual rendered geometry of the card edge. No more drift from
  scrollbar widths, content alignment, ScaleTransform, gutter padding, or
  inner Padding offsets — the ruler asks Avalonia where the Border *is*.
- **Removed the dotted ghost guides entirely**. The card border itself is
  the only vertical reference now. The user complaint was that the dotted
  guides and the visible border were two different lines that drifted out
  of alignment; now there is exactly one line.
- **Document no longer jumps to the right when zooming out**.
  `RenderedScroller` got `HorizontalContentAlignment="Center"` and
  `MarkdownLayoutTransform` got `HorizontalAlignment="Center"` so the
  centred Border stays centred regardless of how the LayoutTransform
  scales it. Previously the ScrollViewer aligned the shrunken content to
  top-left.
- **Ruler is now zoom-aware**. `GetMarkdownScale()` reads the live
  `ScaleTransform` from `MarkdownLayoutTransform` and the ruler math
  multiplies by it when positioning handles. The handles follow the
  visible text edges as the user changes font size or zoom slider, not
  the unscaled logical width.
- **Click-anywhere-on-the-ruler** snaps the column width to that point.
  Both edges move symmetrically since the column is centered. Lets the
  user set the width with one click instead of fiddling with two handle
  drags.
- **Built-in MainWindow ruler now updates whenever the Border bounds
  change** via a `BoundsProperty` subscription on `MarkdownContentBorder`.
  Window resize, font-size change, zoom slider, manual MaxWidth — all
  trigger an automatic re-position.

### Internal

- `OnRulerHandleDragDelta` now divides the drag vector by the current
  scale, so dragging in window pixels translates to the right delta on
  the underlying logical `MaxWidth`.
- Padding bumped from 40→48 to give the new card border breathing room
  around the text, with the math constant updated in lock-step.
- Two upstream errors fixed in `Mostlylucid.Avalonia.UITesting`:
  `Pointer` ambiguity in `PointerSimulator.cs` (qualified to
  `global::Avalonia.Input.Pointer`) and unimplemented MCP wheel/pinch/
  rotate/swipe/touch handlers (stubbed as “not implemented yet” instead
  of unresolved methods).

## v2.2.0 — 2026-04-10

### Added

- **README rewrite** — actually showcases lucidVIEW now. Hero screenshot,
  6-theme gallery from the user manual, copy-paste install commands per
  platform, full keyboard-shortcut table, link to the in-app user manual.
  The Naiad fork section is still there but moved below the lucidVIEW
  feature pitch.
- **Subtle document border** — `MarkdownContentBorder` now has a 1px
  `AppBorderSubtle` outline + `CornerRadius="4"` + 16px vertical margin.
  Document looks like a card now instead of text floating in space. The
  border colour is per-theme so it's barely-visible brightening on dark
  themes and barely-visible darkening on light themes.
- `ux-scripts/capture-ruler.yaml` — UI test that toggles the ruler on/off
  and captures the two states for the user manual.
- New section *11. Word-style ruler* in the in-app User Manual, with the
  ruler-off and ruler-on screenshots. Subsequent sections renumbered
  12–19.

### Fixed

- **Ruler handles now sit at the actual text edges**, not the Border edges.
  Previously the `MarkdownContentBorder` had `Padding="40,32"` so the handles
  appeared with a ~40px gap on each side of the column they were supposed to
  resize. Ruler math now subtracts the horizontal padding when placing the
  handles, the highlighted track, the width readout, and the dotted side
  guides. Padding was also bumped from 40→48 to match the new card border.
- **Ruler bar now spans the full window width**. Was being truncated by the
  18px left gutter; reordered the DockPanel so the ruler is docked Top
  *before* the gutter claims its space. The gutter still appears under the
  ruler in the document area.
- **Image scaling regression** — re-added `StretchDirection="DownOnly"`
  alongside `Stretch="Uniform"`. Large images shrink to fit the column,
  small badges stay at natural size (no more bloat).

### Changed

- **Document border** — `MarkdownContentBorder` got a 1px
  `BorderBrush="{DynamicResource AppBorderSubtle}"` outline,
  `CornerRadius="4"`, and a 16px vertical margin. Gives the document a card
  feel so the text doesn't look like it's floating in space. The
  `AppBorderSubtle` brush is per-theme so the outline is barely-visible
  brightening on dark themes and barely-visible darkening on light themes.
- Border padding bumped from `40,32` to `48,40` to give the new outline a
  little breathing room around the text.

### Internal

- `.gitignore` now ignores `.idea/` (and `**/.idea/`) and `.DS_Store`
  folders properly. The 10 stale `.idea/*.xml` files in `lucid.viewer/`
  that had been tracked by mistake are now untracked (kept on disk).

## v2.1.1 — 2026-04-10

### Added

- **FluentAvaloniaUI** adopted as the base theme. Buttons get proper hover/press
  feedback, the side panel and header look notably more polished. ContentDialog
  available for future Settings dialog upgrade.
- **FluentIcons** (Microsoft Fluent UI System Icons) replace the hand-curated
  boxicons set. 1,800+ icons available via `<ic:SymbolIcon Symbol="..."/>` —
  themed automatically, scale via `FontSize`, no inline path data.
- **Word-style ruler** above the document with two draggable margin handles
  and dotted vertical column guides. Toggle via the ruler button in the
  header. Drag a handle to live-resize the content column; the new width
  persists to `AppSettings.ContentMaxWidth`. Default off.

### Fixed

- **macOS Open With** — double-clicking a `.md` file in Finder launched
  lucidVIEW but never loaded the file. Wired up
  `IActivatableLifetime.Activated` so file paths delivered via Apple Events
  reach `LoadFile()`. Same hook handles iOS / Android / Linux MIME activation.
- **Image cropping** — `<Style Selector="Image">` was setting `Stretch=None`
  which clipped any image wider than the column. Now uses `Stretch=Uniform`
  so images scale to fit the column width without cropping, preserving
  aspect ratio.
- **`ContentMaxWidth` setting** is now actually honoured. Previously the
  XAML hard-coded `MaxWidth=1200` and ignored the persisted value.

### Changed

- `Open URL` HTTP client User-Agent bumped from `lucidVIEW/1.0` to
  `lucidVIEW/2.1`. The `Accept: text/markdown` header is preserved (priority
  q=1) so Cloudflare URL→markdown conversion, Jina Reader, and similar
  services return markdown instead of HTML.

- **Font selector** in Settings dialog stops showing the raw
  `avares://lucidVIEW/Assets/Raleway-Regular.ttf#Raleway, Segoe UI, ...`
  URI as a dropdown entry. The bundled Raleway is now listed as
  *"Raleway (bundled)"* at the top, the URI is parsed back when saving.
  Each font name in the dropdown is rendered **in its own typeface**
  (Word/Office style live preview) via `FontFamily="{Binding}"` on the
  ItemTemplate `TextBlock`s. Same fix applied to the code font dropdown.

### Internal

- New `Styles/Icons.axaml` removed — replaced wholesale by FluentIcons.
- `Border` wrapping the markdown is now named `MarkdownContentBorder` so
  the ruler can manipulate its `MaxWidth`.
- New `MainWindow` regions: *File Activation* (Open With handler) and
  *Word-style Ruler* (drag math + persistence).
- `MainWindow.axaml.cs` got an `Avalonia.Controls.ApplicationLifetimes` using.
- `SettingsDialog.ExtractDisplayName()` parses FontFamily strings (avares
  URIs, comma-separated lists, plain names) into clean dropdown labels.

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
