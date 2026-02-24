# Mostlylucid.Naiad.Surfaces.Skia

SkiaSharp rasterization surface for Naiad SVG diagrams.

## Install

```bash
dotnet add package Mostlylucid.Naiad.Surfaces.Skia
```

## Quick Start

```csharp
using Naiad;
using Naiad.Rendering;
using Naiad.Surfaces.Skia;

var mermaid = """
    flowchart LR
      A[Start] --> B[End]
    """;

// Render to SVG first
var svg = NaiadRenderer.RenderToSvg(mermaid);

// Rasterize to PNG using SkiaSharp
var surface = new SkiaDiagramRenderSurface();
var pngBytes = surface.RenderToPng(svg, width: 800, height: 600);
File.WriteAllBytes("diagram.png", pngBytes);
```

## Supported Formats

- PNG
- JPEG

## Dependencies

- [SkiaSharp](https://github.com/mono/SkiaSharp)
- [Svg.Skia](https://github.com/nicknash/Svg.Skia)

## License

[Unlicense](https://unlicense.org/)
