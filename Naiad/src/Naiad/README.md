# Mostlylucid.Naiad

Pure C# Mermaid-compatible diagram renderer. 30+ diagram types, SVG output, no JavaScript required.

## Install

```bash
dotnet add package Mostlylucid.Naiad
```

## Quick Start

```csharp
using Naiad;
using Naiad.Rendering;

var mermaid = """
    flowchart LR
      A[Start] --> B[Process]
      B --> C[End]
    """;

var svg = NaiadRenderer.RenderToSvg(mermaid);
```

## Supported Diagram Types

Naiad supports 30+ Mermaid-compatible diagram types including:

- **Flowchart** (LR, TB, RL, BT directions)
- **Sequence** diagrams
- **Class** diagrams
- **Entity Relationship** (ER)
- **State** diagrams
- **Gantt** charts
- **Pie** charts
- **Mindmap**
- **Timeline**
- **Git Graph**
- **Sankey**
- **XY Chart**
- **Block**
- **Architecture**
- **Kanban**
- **Radar**
- **Quadrant**
- **Requirement**
- **User Journey**
- **C4** diagrams
- **Packet**
- **Wireframe**
- **Treemap**
- **Geo** (choropleth maps)
- **Bubble Pack**
- **Dendrogram**
- **Voronoi**
- **Parallel Coordinates**
- And more...

## Skin Packs

Naiad supports pluggable skin packs for custom node shapes and styling. See `Mostlylucid.Naiad.Skins.Showcase` and `Mostlylucid.Naiad.Skins.Cats` for examples.

## Rendering Surfaces

For rasterization (PNG/JPEG), add a rendering surface package:

- `Mostlylucid.Naiad.Surfaces.ImageSharp` - cross-platform, pure managed
- `Mostlylucid.Naiad.Surfaces.Skia` - SkiaSharp-based

## License

[Unlicense](https://unlicense.org/)
