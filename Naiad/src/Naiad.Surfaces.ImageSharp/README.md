# Mostlylucid.Naiad.Surfaces.ImageSharp

ImageSharp rasterization surface for Naiad SVG diagrams. Cross-platform, pure managed code.

## Install

```bash
dotnet add package Mostlylucid.Naiad.Surfaces.ImageSharp
```

## Quick Start

```csharp
using Naiad;
using Naiad.Rendering;
using Naiad.Surfaces.ImageSharp;

var mermaid = """
    flowchart LR
      A[Start] --> B[End]
    """;

// Render to SVG first
var svg = NaiadRenderer.RenderToSvg(mermaid);

// Rasterize to PNG using ImageSharp
var surface = new ImageSharpDiagramRenderSurface();
var pngBytes = surface.RenderToPng(svg, width: 800, height: 600);
File.WriteAllBytes("diagram.png", pngBytes);
```

## Supported Formats

- PNG
- JPEG

## Dependencies

- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp)
- [SixLabors.ImageSharp.Drawing](https://github.com/SixLabors/ImageSharp.Drawing)
- [SixLabors.Fonts](https://github.com/SixLabors/Fonts)

## License

[Unlicense](https://unlicense.org/)
