# Mostlylucid.Naiad.Meta.Complete

Meta package for complete Naiad composition with the full built-in plugin set.

This package contains no binaries. It composes package dependencies so you get everything in one install.

## Install

```bash
dotnet add package Mostlylucid.Naiad.Meta.Complete
```

## What's Included

| Package | Description |
|---------|-------------|
| `Mostlylucid.Naiad` | Core renderer - 30+ Mermaid diagram types, SVG output |
| `Mostlylucid.Naiad.Skins.Cats` | Cat-themed flowchart skin pack |
| `Mostlylucid.Naiad.Skins.Showcase` | Showcase skin packs (prism3d, neon, sunset) |
| `Mostlylucid.Naiad.Surfaces.ImageSharp` | ImageSharp PNG/JPEG rasterization |
| `Mostlylucid.Naiad.Surfaces.Skia` | SkiaSharp PNG/JPEG rasterization |

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

## When to Use This vs Mostlylucid.Naiad.Meta.Mermaid

- **Meta.Complete** - You want everything: all skin packs, all rendering surfaces, all diagram types.
- **Meta.Mermaid** - You want a minimal Mermaid-equivalent setup (core renderer only, no extras).

## License

[Unlicense](https://unlicense.org/)
