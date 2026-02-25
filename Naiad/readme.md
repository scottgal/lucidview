# <img src="src/icon.png" height="30px"> Naiad

[![Build status](https://img.shields.io/appveyor/build/SimonCropp/Naiad)](https://ci.appveyor.com/project/SimonCropp/Naiad)
[![NuGet Status](https://img.shields.io/nuget/v/Naiad.svg)](https://www.nuget.org/packages/Naiad/)

A .NET library for rendering [Mermaid](https://mermaid.js.org/) diagrams to SVG. No browser or JavaScript runtime required.

> **Temporary fork notice:** [Naiad](https://github.com/SimonCropp/Naiad) is owned and maintained by [Simon Cropp](https://github.com/SimonCropp). This fork (`Mostlylucid.Naiad`) is a temporary package containing experimental features and new diagram types. The intention is to contribute these changes back to the upstream Naiad project - this fork is not intended as a permanent alternative.

<p align="center">
  <img src="docs/diagrams/flowchart.svg" alt="Flowchart" width="400">
</p>


## NuGet package

https://nuget.org/packages/Naiad/


## Usage

```cs
var svg = Mermaid.Render(
    """
    flowchart LR
        A[Start] --> B[Process] --> C[End]
    """);
```

The diagram type is automatically detected from the input.


### Render Options

```cs
var svg = Mermaid.Render(
    input,
    new RenderOptions
    {
        Padding = 20,
        FontSize = 14,
        FontFamily = "Arial, sans-serif"
    });
```


## Supported Diagram Types

Naiad supports **32 diagram types** - every Mermaid diagram type plus several Naiad originals. All rendered natively in .NET, no JavaScript required.

### Structural Diagrams

| Diagram | Syntax | Preview |
|---------|--------|---------|
| [Flowchart](https://mermaid.js.org/syntax/flowchart.html) | `flowchart LR` | <img src="docs/diagrams/flowchart.svg" width="250"> |
| [Class](https://mermaid.js.org/syntax/classDiagram.html) | `classDiagram` | <img src="docs/diagrams/class.svg" width="250"> |
| [State](https://mermaid.js.org/syntax/stateDiagram.html) | `stateDiagram-v2` | <img src="docs/diagrams/state.svg" width="250"> |
| [ER](https://mermaid.js.org/syntax/entityRelationshipDiagram.html) | `erDiagram` | <img src="docs/diagrams/er.svg" width="250"> |
| [Block](https://mermaid.js.org/syntax/block.html) | `block-beta` | <img src="docs/diagrams/block.svg" width="250"> |
| [Architecture](https://mermaid.js.org/syntax/architecture.html) | `architecture-beta` | <img src="docs/diagrams/architecture.svg" width="250"> |
| [Requirement](https://mermaid.js.org/syntax/requirementDiagram.html) | `requirementDiagram` | <img src="docs/diagrams/requirement.svg" width="250"> |

### Behavioral Diagrams

| Diagram | Syntax | Preview |
|---------|--------|---------|
| [Sequence](https://mermaid.js.org/syntax/sequenceDiagram.html) | `sequenceDiagram` | <img src="docs/diagrams/sequence.svg" width="250"> |
| [User Journey](https://mermaid.js.org/syntax/userJourney.html) | `journey` | <img src="docs/diagrams/journey.svg" width="250"> |
| [Gantt](https://mermaid.js.org/syntax/gantt.html) | `gantt` | <img src="docs/diagrams/gantt.svg" width="250"> |
| [Git Graph](https://mermaid.js.org/syntax/gitgraph.html) | `gitGraph` | <img src="docs/diagrams/gitgraph.svg" width="250"> |
| [Timeline](https://mermaid.js.org/syntax/timeline.html) | `timeline` | <img src="docs/diagrams/timeline.svg" width="250"> |
| [Kanban](https://mermaid.js.org/syntax/kanban.html) | `kanban` | <img src="docs/diagrams/kanban.svg" width="250"> |
| [BPMN](https://www.omg.org/spec/BPMN/) | XML `<definitions>` | <img src="docs/diagrams/bpmn.svg" width="250"> |

### Data Visualization

| Diagram | Syntax | Preview |
|---------|--------|---------|
| [Pie](https://mermaid.js.org/syntax/pie.html) | `pie` | <img src="docs/diagrams/pie.svg" width="200"> |
| [XY Chart](https://mermaid.js.org/syntax/xyChart.html) | `xychart-beta` | <img src="docs/diagrams/xychart.svg" width="250"> |
| [Sankey](https://mermaid.js.org/syntax/sankey.html) | `sankey-beta` | <img src="docs/diagrams/sankey.svg" width="250"> |
| [Quadrant](https://mermaid.js.org/syntax/quadrantChart.html) | `quadrantChart` | <img src="docs/diagrams/quadrant.svg" width="200"> |
| [Radar](https://mermaid.js.org/syntax/radar.html) | `radar-beta` | <img src="docs/diagrams/radar.svg" width="200"> |
| [Treemap](https://mermaid.js.org/syntax/treemap.html) | `treemap-beta` | <img src="docs/diagrams/treemap.svg" width="250"> |
| [Packet](https://mermaid.js.org/syntax/packet.html) | `packet-beta` | <img src="docs/diagrams/packet.svg" width="250"> |

### C4 Model

| Diagram | Syntax | Preview |
|---------|--------|---------|
| [C4 Context](https://mermaid.js.org/syntax/c4.html) | `C4Context` | <img src="docs/diagrams/c4context.svg" width="250"> |
| C4 Container | `C4Container` | <img src="docs/diagrams/c4container.svg" width="250"> |
| C4 Component | `C4Component` | <img src="docs/diagrams/c4component.svg" width="250"> |
| C4 Deployment | `C4Deployment` | <img src="docs/diagrams/c4deployment.svg" width="250"> |

### Hierarchical & Spatial

| Diagram | Syntax | Preview |
|---------|--------|---------|
| [Mindmap](https://mermaid.js.org/syntax/mindmap.html) | `mindmap` | <img src="docs/diagrams/mindmap.svg" width="250"> |

### Naiad Originals

These diagram types are unique to Naiad and not available in Mermaid.

| Diagram | Syntax | Preview |
|---------|--------|---------|
| Dendrogram | `dendrogram` | <img src="docs/diagrams/dendrogram.svg" width="250"> |
| Bubble Pack | `bubblepack` | <img src="docs/diagrams/bubblepack.svg" width="200"> |
| Voronoi | `voronoi` | <img src="docs/diagrams/voronoi.svg" width="200"> |
| Parallel Coordinates | `parallelcoords` | <img src="docs/diagrams/parallelcoords.svg" width="250"> |
| Geo Map | `geo` | <img src="docs/diagrams/geo.svg" width="250"> |

### Skin Packs

| Diagram | Syntax | Preview |
|---------|--------|---------|
| Wireframe | `%% naiad: skinPack=wireframe` | <img src="docs/diagrams/wireframe.svg" width="250"> |

### Interoperability

| Format | Description | Preview |
|--------|-------------|---------|
| [Tulip TLP](docs/tulip/README.md) | Import/export [Tulip](https://tulip.labri.fr) graph files | <img src="docs/tulip/sample-social-network.svg" width="250"> |

<img src="docs/tulip/sample-dependencies.svg" width="350"> <img src="docs/tulip/sample-family-tree.svg" width="350">


## Rendering Surfaces

Naiad's core outputs SVG. Multiple rendering surfaces transform that SVG into other formats — or bypass SVG entirely for native rendering:

| Surface | Package | Description |
|---------|---------|-------------|
| SVG (built-in) | `Mostlylucid.Naiad` | Core renderer — all diagram types produce SVG natively |
| [SkiaSharp](src/Naiad.Surfaces.Skia/README.md) | `Mostlylucid.Naiad.Surfaces.Skia` | Rasterize SVG → PNG/JPEG via SkiaSharp (native libs) |
| [ImageSharp](src/Naiad.Surfaces.ImageSharp/README.md) | `Mostlylucid.Naiad.Surfaces.ImageSharp` | Rasterize SVG → PNG/JPEG — pure managed, no native deps |
| [Blazor](src/Naiad.Blazor/README.md) | `Mostlylucid.Naiad.Blazor` | `<NaiadDiagram>` Blazor component wrapping the WASM web component |
| [WebAssembly](src/Naiad.Wasm/README.md) | — | `net10.0-browser` target: `RenderSvg`, `DetectDiagramType` exports |
| Console | — | ANSI half-block art renderer ([tutorial](../docs/blog/2026-02-21-building-a-renderer-for-naiad.md)) |

Building a custom surface? See [Building a Custom Renderer for Naiad](../docs/blog/2026-02-21-building-a-renderer-for-naiad.md) — a step-by-step guide implementing a console renderer in ~120 lines.


## Plugin System

Naiad is extensible at multiple levels:

- **Render Surface Plugins** (`IDiagramRenderSurfacePlugin`) — Add new output formats. Register via `DiagramRenderSurfaceRegistry`. See [Building a Renderer](../docs/blog/2026-02-21-building-a-renderer-for-naiad.md).
- **Skin Packs** — Custom node/edge styling per diagram type (e.g., [Wireframe](src/Naiad.Skins.Showcase/README.md), [Cats](src/Naiad.Skins.Cats/README.md)).
- **Fluent API Plugins** (`IFluentDiagramPlugin`) — Typed C# builders for authoring diagrams programmatically. See the [Fluent Plugin Spec](../docs/plans/2026-02-21-fluent-plugin-spec.md) and [Fluent API Design](../docs/plans/2026-02-21-mermaid-fluent-api-design.md).
- **Meta Packages** — [Mermaid-only](src/Naiad.Meta.Mermaid/README.md) or [Complete](src/Naiad.Meta.Complete/README.md) diagram type bundles.

For Avalonia-specific rendering (interactive flowcharts with hover, click navigation, and context menus), see the [MarkdownViewer Plugin README](../MarkdownViewer/Plugins/README.md) and [Naiad Mermaid Rendering Guide](../docs/NAIAD_MERMAID_RENDERING.md).


## Theming

Naiad supports light and dark themes out of the box:

```cs
var darkSvg = Mermaid.Render(input, new RenderOptions
{
    Theme = MermaidTheme.Dark
});
```


## Test Renders<!-- include: renders. path: src/test-renders/renders.include.md -->

Auto-generated documentation from the test suite.

- [C4](src/test-renders/C4.md)
- [Class](src/test-renders/Class.md)
- [EntityRelationship](src/test-renders/EntityRelationship.md)
- [Flowchart](src/test-renders/Flowchart.md)
- [Gantt](src/test-renders/Gantt.md)
- [GitGraph](src/test-renders/GitGraph.md)
- [Kanban](src/test-renders/Kanban.md)
- [Mindmap](src/test-renders/Mindmap.md)
- [Pie](src/test-renders/Pie.md)
- [Quadrant](src/test-renders/Quadrant.md)
- [Requirement](src/test-renders/Requirement.md)
- [Sequence](src/test-renders/Sequence.md)
- [State](src/test-renders/State.md)
- [Timeline](src/test-renders/Timeline.md)
- [UserJourney](src/test-renders/UserJourney.md)

### Beta diagram types

- [Architecture](src/test-renders/Architecture.md)
- [Block](src/test-renders/Block.md)
- [BPMN](src/test-renders/BPMN.md)
- [Packet](src/test-renders/Packet.md)
- [Radar](src/test-renders/Radar.md)
- [Sankey](src/test-renders/Sankey.md)
- [Treemap](src/test-renders/Treemap.md)
- [Wireframe](src/test-renders/Wireframe.md)
- [XYChart](src/test-renders/XYChart.md)<!-- endInclude -->


## Icon

[Mermaid Tail](https://thenounproject.com/icon/mermaid-tail-1908145//) designed by [Olena Panasovska](https://thenounproject.com/creator/zzyzz/) from [The Noun Project](https://thenounproject.com).
