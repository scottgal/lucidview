# lucidVIEW UI Tests

End-to-end UI tests powered by [Mostlylucid.Avalonia.UITesting](../../lucidRESUME/src/Mostlylucid.Avalonia.UITesting).

The harness is **Debug-only** — it's not compiled into Release builds (see
`MarkdownViewer.csproj` and the `#if DEBUG` block in `Program.cs`). The
testing library lives in the local lucidRESUME repo and is referenced as a
project, not a NuGet package.

## Modes

```bash
# YAML script mode — runs to completion, exits, writes screenshots + result.json
dotnet run --project MarkdownViewer/MarkdownViewer.csproj -- \
    --ux-test --script ux-scripts/smoke-all-functions.yaml --output ux-results

# Interactive REPL — explore the UI by hand, list controls, click, screenshot
dotnet run --project MarkdownViewer/MarkdownViewer.csproj -- --ux-repl

# MCP server — lets an LLM (Claude, etc.) drive lucidVIEW over JSON-RPC stdio
dotnet run --project MarkdownViewer/MarkdownViewer.csproj -- --ux-mcp
```

## Scripts

| File | What it does |
|---|---|
| `test-document.md` | Feature-rich fixture (headings, code, mermaid, table, list, image). Loaded by every script via the `Navigate` action. |
| `smoke-all-functions.yaml` | Exercises every non-dialog function: load, side panel, all 6 themes, TOC, search, raw/preview tabs, font controls, zoom controls, fullscreen. ~20 screenshots. |
| `dialogs.yaml` | Opens every dialog-bound menu item (Settings, Render Mermaid, Open URL, Help) and screenshots. Native file pickers (Open File, Export PDF) cannot be driven by the harness — those are smoke-tested manually. |

## How `Navigate` loads the fixture

The harness's `Navigate` action calls `MainWindow.NavigateCommand` via reflection.
We added a `NavigateCommand` property on `MainWindow` that takes a file path
and calls `LoadFile(path)`. So `value: ux-scripts/test-document.md` in the YAML
just loads the markdown — no special bootstrapping needed.

`NavigateCommand` is **not** bound to any UI element; it exists purely for the
test harness.

## Adding a new test

1. Look at `MainWindow.axaml` for the `x:Name` of the control you want to drive.
   If a control is anonymous, give it a name (e.g. `MenuOpenFileBtn`).
2. Drop a new `*.yaml` file in this folder using the existing scripts as a
   template.
3. Action types are documented at
   <https://github.com/scottgal/lucidRESUME/tree/master/src/Mostlylucid.Avalonia.UITesting>:
   `Click`, `DoubleClick`, `RightClick`, `TypeText`, `PressKey`, `Hover`,
   `Scroll`, `Wait`, `Screenshot`, `Navigate`, `Assert`, `StartVideo`,
   `StopVideo`, `Svg`.

## Why the harness can't drive native file pickers

Avalonia's `StorageProvider.OpenFilePickerAsync` and `SaveFilePickerAsync`
delegate to the OS native picker (NSOpenPanel on macOS, IFileDialog on Windows,
GTK on Linux). Those windows are not Avalonia controls, so the harness's
`UITestContext` can't see or click them. For coverage of those code paths,
either:

- Test the underlying methods directly in `MarkdownViewer.Tests/`, or
- Drive lucidVIEW from the command line: `dotnet run --project MarkdownViewer -- ux-scripts/test-document.md` (lucidVIEW already loads `args[1]` as a startup file).
