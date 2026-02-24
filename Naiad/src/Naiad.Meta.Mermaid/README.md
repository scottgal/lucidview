# Mostlylucid.Naiad.Meta.Mermaid

Meta package for Mermaid-equivalent Naiad usage - the minimal profile.

This package contains no binaries. It composes package dependencies for the core Mermaid-compatible rendering setup.

## Install

```bash
dotnet add package Mostlylucid.Naiad.Meta.Mermaid
```

## What's Included

| Package | Description |
|---------|-------------|
| `Mostlylucid.Naiad` | Core renderer - 30+ Mermaid diagram types, SVG output |

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

## When to Use This vs Mostlylucid.Naiad.Meta.Complete

- **Meta.Mermaid** - Minimal Mermaid-equivalent setup. Core renderer only, no skin packs or rasterization surfaces.
- **Meta.Complete** - Full setup with all skin packs and rendering surfaces included.

## License

[Unlicense](https://unlicense.org/)
