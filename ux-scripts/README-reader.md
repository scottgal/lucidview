# mylo UI driving scripts

Same harness as lucidVIEW ([Mostlylucid.Avalonia.UITesting](../external/lucidRESUME/src/Mostlylucid.Avalonia.UITesting)),
Debug-only for the same reason: `--ux-test` is compiled out of Release.
`ux-scripts/README.md` covers the harness itself. This file covers what
mylo's scripts check, and the rules they follow.

Build first:

```bash
dotnet build LucidReader/LucidReader.csproj
```

## The scripts

| Run this | Script | What it checks | Needs network |
|---|---|---|---|
| `ux-scripts/run-reader-smoke.sh [out]` | `reader-smoke.yaml` | The whole shell: sidebar smart rows, a folder, a feed, the All/Unread/Starred segments, the reading pane and its offline badge, the mark-as-read dwell, the hover row actions, full-text search, Refresh all | no |
| `ux-scripts/run-reader-search.sh [out]` | `verify-reader-search.yaml` | Full-text search: a partly-typed word matching as it is typed, an article findable only by its summary, the matched passage shown with the query term picked out in place of the ordinary row preview, the scope toggle narrowing a search to the selected feed, the filter segments applying to a search, a sidebar click still beating a pending search, and FTS5 syntax typed into the box being searched for rather than parsed | no |
| `ux-scripts/run-reader-settings.sh [out]` | `reader-settings.yaml` | Both settings dialogs: all four global groups, save-and-reopen round trips, the retention clean-up, the per-feed override switches, and a global change showing up as the per-feed inherited label | no |
| `ux-scripts/run-reading-column.sh [out]` | `verify-reading-column.yaml` | The reading column, **measured in pixels**: equal left and right margins at two different column widths, the margin growing as the column narrows, and bigger font/line-height/code-size changing what is rendered | no |
| `ux-scripts/run-pane-layout.sh [out]` | `verify-pane-layout.yaml`, `verify-pane-layout-persist.yaml`, `verify-pane-layout-restore.yaml` | The toolbar's layout button, **measured in pixels**: each click collapses the leftmost pane still showing, the sidebar's 260px column and both splitters actually leave the window rather than going blank, the reading column re-centres in the wider pane, a fourth click restores the start exactly, and a second process comes up in the saved mode | no |
| `ux-scripts/run-reading-typography.sh [out]` | `verify-reading-typography.yaml` | The four reading settings (font size, line height, code font size, column width) round-tripping through the settings dialog: change two, save, reopen, read them back | no |
| `ux-scripts/run-reader-keyboard.sh [out]` | `verify-reader-keyboard.yaml` | The keyboard shortcuts, with real key events: S stars and unstars the selected article, and none of the ten bare gestures (J K N P M S R O T and `/`) does anything while focus is in the search box | no |
| `ux-scripts/run-refresh-health.sh [out]` | `verify-refresh-health.yaml` | The status bar reporting auto-paused feeds, and putting one back into rotation with the Resume button | no |
| `ux-scripts/run-add-feed-writes.sh [out]` | `verify-add-feed-writes.yaml` | Adding discovered feeds for real: the writes land, the sidebar reloads, the status bar says what happened | yes |
| direct | `verify-add-feed-dialog.yaml` | The add-feed dialog: empty-address message, bare-domain normalising, autodiscovery finding two feeds, Add going live. Cancels, so it writes nothing | yes |
| direct | `verify-feed-settings-dialog.yaml` | Opening per-feed settings from the toolbar against the two-feed development database. Cancels, so it writes nothing | no |

Output defaults to `/tmp/lr-<name>`; pass a directory to put it elsewhere.
`--ux-repl` and `--ux-mcp` work on mylo too, if you want to poke at the
running app by hand.

## The rule these scripts follow

**Every script must be runnable twice in a row, from any starting state, and
must leave the database as it found it.**

This is not a style preference. Two earlier scripts here were one-shot.
`verify-refresh-health.yaml` documented its database seed as a manual step, so
it passed once against hand-seeded state and failed on the next run.
`verify-add-feed-writes.yaml` had no cleanup at all, asserted "Added" (true only
the first time, since afterwards the app correctly reports duplicates), and left
two live subscriptions behind that skewed every later script's row counts. A
check that passes once and then rots is worse than no check, because it reads
as coverage.

So: if a script needs database state, it gets a runner shell script that seeds
and cleans up from an `EXIT`/`INT`/`TERM` trap under `set -euo pipefail`. Two
shapes are in use.

- **Seed and delete rows in the real profile** (`run-refresh-health.sh`,
  `run-add-feed-writes.sh`). Used where the script has to exercise the actual
  application data directory. Scoped to rows with addresses nothing real would
  collide with. Set `PRAGMA foreign_keys=ON` in any cleanup that relies on
  `ON DELETE CASCADE`: SQLite defaults it off, and the CLI is not the app.
- **A throwaway profile** (`run-reader-smoke.sh`, `run-reader-settings.sh`,
  `run-reading-column.sh`, `run-reading-typography.sh`, `run-pane-layout.sh`, via
  `reader-harness.sh`). `MYLO_DATA_DIR` points a Debug build at a
  temporary directory, which is seeded from `reader-fixture.sql` and deleted on
  the way out. `run-reader-keyboard.sh` and `run-reader-search.sh` use this shape too. This is the better shape where it fits: the script decides every
  count it asserts, needs no network, and cannot touch anything you care about.

`reader-fixture.sql` is that database: two feeds ("Harness Alpha" in a folder,
"Harness Beta" loose), five articles, one already read, one starred, one with
extracted full text, and one ("Weeknotes from the harness") with a body long
enough that the word "kingfishers" in it falls past the 180 characters the
ordinary row preview shows, which is how `verify-reader-search.yaml` tells a
search snippet apart from a normal one. Article dates are relative to now, not literal, so
retention can never age them past the 30-day cutoff and change the counts.

## Measuring, not asserting

`run-reading-column.sh` is the one script here that does not decide anything
from a property. It snips the reading pane with `Screenshot` + `target:`, then
`measure-reading-column.py` reads the PNG with PIL and reports where the
content actually landed.

That is deliberate. A property assertion says what a control was told; it does
not say what was drawn. A filter pill in this app had its padding silently
overridden by an external style and passed every property check there was. The
reading pane carries the same risk by construction: LiveMarkdown.Avalonia's
application-level stylesheet sets the font sizes that the reading settings have
to beat, and a style that loses does so silently.

What gets measured, and why it can be: the hairline `Rectangle` between the
article header and the body spans the column's whole width, so the first and
last non-background pixel on its row give both margins exactly. Article text
would not - every line ends where its last word ends.

## Harness behaviour worth knowing before you write one

Learned the hard way; none of it is obvious from a passing run.

- **`Click` only works on real Buttons.** On anything else it just calls
  `Focus()`, so it cannot select a sidebar row or fire a `PointerPressed`
  handler. Use `MouseDown` then `MouseUp` with the same target.
- **`Click` does not toggle a CheckBox or check a RadioButton either.** It
  raises `ClickEvent` directly, which bypasses `OnClick`, which is what
  actually toggles. `verify-settings-dialog.yaml` and
  `verify-settings-visual.yaml` were deleted over this: driven by `Click`,
  every one of their tab switches was a no-op, so each screenshot named for a
  tab was a picture of the Updates tab, and the assertion that a saved setting
  came back could never have passed. `reader-settings.yaml` replaces both.
- **Park the pointer before hovering a toggle.** A toggle only flips on release
  if it is under the pointer. The simulated pointer does not move between one
  dialog closing and the next opening in the same place, so hovering straight
  onto a control that sits where the pointer already is can raise no enter
  event and the release is ignored. Hover something else first. This showed up
  as an intermittent failure, not a consistent one.
- **`PressKey` cannot test keyboard shortcuts.** It bypasses
  `Window.KeyBindings`, which is where all of mylo's shortcuts live.
  Do not write a script claiming to cover them.
- **The harness cannot see a ContextMenu.** It opens in its own `PopupRoot`,
  which `LocatorEngine` (single window root) and `ScreenshotCapture` (popups in
  the window tree only) both miss. `FeedSettingsButton` and `ResumeFeedButton`
  exist on the toolbar so those two flows have a route that is not
  right-click-only.
- **Native file pickers are OS windows.** Import/Export OPML and Export article
  cannot be driven at all.
- **Quote selector values containing spaces**: `target: "text='Alpha Feed'"`.
  Unquoted is a parse error.
- **Scope text selectors.** `inside(name=SidebarSections) text='Unread'`, not
  `text='Unread'`: the same words appear on a filter segment, and a feed's name
  appears again on each of its item rows. Worse, a target is resolved separately
  for the `MouseDown` and the `MouseUp`, and selecting a row repopulates the
  item list in between, so an unscoped selector can move the release into a
  different pane.
- **`TypeText` needs a TextBox** and ignores an empty value, so it can neither
  drive a `NumericUpDown` nor clear a box. To empty the search box, click a
  sidebar row: that is what the app does anyway.
- Dialog screenshots take `window_id: "<its Title>"`; `composite: true` puts the
  dialog over the main window.
- macOS occasionally refuses a second app process a display link when one has
  just exited (`Avalonia.Native was not able to start the RenderTimer`, error
  `-6661`). `reader-harness.sh` retries around it. It is a launch-rate limit,
  not a fault in the app.

## What has no coverage here, and why

- Every keyboard shortcut, and article tagging, whose only route is the `T`
  binding. `PressKey` cannot reach `Window.KeyBindings`.
- Unsubscribe, Rename and Mark-all-read: sidebar context menu only.
- Import OPML, Export OPML, Export article: native file pickers.
- The numeric settings fields: `NumericUpDown`, which `TypeText` will not
  accept. Their values are read back but never typed into; the clamping is
  covered by `SettingsDraftTests` and `FeedSettingsDraftTests`.
- Feed autodiscovery beyond what the two add-feed scripts already do.
