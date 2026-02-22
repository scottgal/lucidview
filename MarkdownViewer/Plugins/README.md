# Diagram Renderer Plugins

`MarkdownViewer` now uses a plugin host for native diagram replacement:

- `IDiagramRendererPlugin`
- `DiagramRendererPluginHost`
- `AvaloniaNativeDiagramRendererPlugin` (default implementation)

`MainWindow` no longer contains diagram marker replacement logic directly; it delegates to the plugin host.

## Why This Matters

- Keeps `MainWindow` focused on orchestration.
- Makes renderer replacement extensible.
- Creates a clear seam for platform-specific UI adapters.

## WPF / WinUI Reuse

`AvaloniaNativeDiagramRendererPlugin` is Avalonia-specific because it depends on:

- `Avalonia.Visual` tree traversal
- Avalonia controls (`TextBlock`, `ContextMenu`, etc.)
- Avalonia-native render controls (`FlowchartCanvas`, `DiagramCanvas`)

So it cannot be used directly in WPF/WinUI.

What can be reused:

- Naiad core rendering outputs (`FlowchartLayoutResult`, `SvgDocument`)
- Marker conventions emitted by `MarkdownService` (`FLOWCHART:` / `DIAGRAM:`)
- Plugin-host pattern (`IDiagramRendererPlugin` + host orchestration)

For WPF/WinUI, implement a platform-specific plugin that:

1. Locates marker text runs in the host visual tree.
2. Replaces markers with framework-native controls.
3. Wires export actions to the same `MarkdownService` save methods.
