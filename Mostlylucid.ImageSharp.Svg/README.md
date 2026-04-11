# Mostlylucid.ImageSharp.Svg

A small, low-allocation, **AOT-compatible** SVG rasterizer that renders to
[SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp). Pure managed
code — no SkiaSharp, no native dependencies, no reflection.

## Why

Most SVG-to-bitmap libraries on .NET wrap Skia or use reflection-heavy XML
serializers, which makes them either non-trim-safe or non-AOT-compatible.
This package fills the gap for apps that target NativeAOT or need a
self-contained, portable SVG renderer with zero unmanaged binaries.

## Status

**Phase 1.** Renders the SVG element subset emitted by:

- shields.io badges (`rect` with `rx`, `linearGradient`, `clipPath`, `text`)
- Naiad / mermaid diagrams (basic shapes, transforms, gradients — work in progress)

Out of scope: SMIL animation, advanced filters, foreignObject. The aim is
"render the SVGs that real-world apps actually feed it", not "implement the
W3C SVG spec from scratch."

## Usage

```csharp
using Mostlylucid.ImageSharp.Svg;

var svg = File.ReadAllText("badge.svg");
var result = SvgImage.LoadAsPng(svg, new SvgRenderOptions { Scale = 2f });
File.WriteAllBytes("badge.png", result.Bytes);

Console.WriteLine($"Natural size: {result.NaturalWidth}x{result.NaturalHeight}");
```

For pixel-level access:

```csharp
using var raster = SvgImage.Load(svg, new SvgRenderOptions
{
    Scale = 2f,
    Background = SixLabors.ImageSharp.Color.White,
});

raster.Image.Save("badge.png"); // ImageSharp Image<Rgba32>
```

## AOT

The csproj sets `IsAotCompatible=true`, `IsTrimmable=true`, and enables both
analyzers. The library publishes clean under `dotnet publish -p:PublishAot=true`.

## License

[Unlicense](https://unlicense.org/).
