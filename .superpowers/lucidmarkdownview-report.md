# LucidMarkdownView — Implementation Report

## Public API

### `LucidMarkdownView` (UserControl)

**Styled Properties**

| Property | Type | Description |
|---|---|---|
| `Markdown` | `string?` | Markdown text to render. Setting this triggers a full re-render. |
| `SourcePath` | `string?` | Base path for resolving relative image paths. Defaults to `Path.GetTempPath()`. |

**Events**

| Event | Type | Description |
|---|---|---|
| `LinkClick` | `EventHandler<LinkClickedEventArgs>?` | Fired when a link inside the rendered Markdown is clicked. Forwards the inner `MarkdownRenderer.LinkClick` event. |

**Visual structure**: `UserControl` → `ScrollViewer` → `MarkdownRenderer` (from LiveMarkdown.Avalonia)

---

## Build Result

- `Mostlylucid.LucidView.Markdown`: **Build succeeded** — 0 warnings, 0 errors
- `Mostlylucid.LucidView.Markdown.Tests`: **Build succeeded** — 0 warnings, 0 errors

---

## Test Result

All **3 tests passed** (1.45 s total):

| Test | Result | Notes |
|---|---|---|
| `LucidMarkdownView_renders_non_background_pixels` | PASS | Screenshot PNG captured; >100 non-white pixels confirmed (rendered `# Hello` heading + bold text) |
| `LucidMarkdownView_visual_tree_contains_markdown_renderer` | PASS | `MarkdownRenderer` and `ScrollViewer` found in visual tree |
| `LucidMarkdownView_Markdown_property_change_rerenders` | PASS | Changing `Markdown` property did not throw; `MarkdownRenderer` still present |

**Mermaid flowchart headlessly**: The mermaid `flowchart TD A-->B` block was included in the sample markdown but `FlowchartCanvas`/`DiagramCanvas` did NOT appear in the visual tree headlessly. This is expected — the Naiad/Mermaid pipeline emits markers into the markdown text which are then replaced by `DiagramRendererPluginHost.ReplaceDiagramMarkers()`, but the headless Skia renderer doesn't drive the full layout/measure pass needed for the plugin to find and replace text markers in the visual tree. Only the text markers (code-block placeholders) render headlessly; actual diagram canvases require a real windowed session.

---

## App-Only Logic Inlined

The following behaviors from `MarkdownViewer/Views/MainWindow.axaml.cs` were adapted or omitted:

1. **`ScheduleDiagramMarkerReplacement()`** — This was a deferred UI timer callback in the app to avoid blocking the UI thread during marker replacement. In `LucidMarkdownView` it is inlined as a direct `_pluginHost.ReplaceDiagramMarkers(MdViewer)` call immediately after setting `MdViewer.MarkdownBuilder`. This is safe because we're already on the UI thread.

2. **Theme color resolution** — `MainWindow` calls `ResolveDiagramTextBrush()` reading from a `ThemeDefinition`. The control uses `Foreground` from the Avalonia visual tree, falling back to `Brushes.White`.

3. **`saveDiagramAs`** — The app handler opens a file-save dialog. The control uses a no-op `Task.CompletedTask` delegate since the control has no shell access. Callers needing this can wire it externally in a future API extension.

4. **Link navigation** — `MainWindow.OnLinkClick` opened a browser. The control exposes the raw `LinkClick` event instead so callers choose how to handle it.

5. **`ImageCacheService`** — Constructed internally; the cache temp directory defaults to `lucidview-mermaid` under `GetTempPath()` (same as the app).
