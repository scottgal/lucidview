using System.IO;
using Mostlylucid.ImageSharp.Svg.Internal;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Mostlylucid.ImageSharp.Svg;

/// <summary>
/// Public entry point for the renderer. Loads SVG content and produces an
/// <see cref="Image{Rgba32}"/> via SixLabors.ImageSharp — no SkiaSharp, no
/// reflection, no native dependencies. The full pipeline (parser + renderer)
/// is AOT-clean and trim-safe.
/// </summary>
public static class SvgImage
{
    /// <summary>
    /// Render an SVG XML string to an in-memory bitmap. The caller owns the
    /// returned image and must dispose it.
    /// </summary>
    /// <param name="svgXml">UTF-8 SVG document text.</param>
    /// <param name="options">Optional render configuration.</param>
    /// <returns>The rasterized image plus the SVG's intrinsic dimensions in user units.</returns>
    public static SvgRasterResult Load(string svgXml, SvgRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(svgXml);
        var root = SvgXmlParser.Parse(svgXml);
        var renderer = new SvgRenderer(options ?? new SvgRenderOptions());
        var image = renderer.Render(root, out var natW, out var natH);
        return new SvgRasterResult(image, natW, natH);
    }

    /// <summary>
    /// Render an SVG document from a stream.
    /// </summary>
    public static SvgRasterResult Load(Stream svgStream, SvgRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(svgStream);
        var root = SvgXmlParser.Parse(svgStream);
        var renderer = new SvgRenderer(options ?? new SvgRenderOptions());
        var image = renderer.Render(root, out var natW, out var natH);
        return new SvgRasterResult(image, natW, natH);
    }

    /// <summary>
    /// Convenience helper: render an SVG straight to a PNG byte array. Useful
    /// for image-cache pipelines where the bitmap is written to disk and the
    /// caller never needs to manipulate pixels.
    /// </summary>
    public static SvgPngResult LoadAsPng(string svgXml, SvgRenderOptions? options = null)
    {
        using var result = Load(svgXml, options);
        using var ms = new MemoryStream();
        result.Image.Save(ms, new PngEncoder());
        return new SvgPngResult(ms.ToArray(), result.NaturalWidth, result.NaturalHeight);
    }
}

/// <summary>
/// Output of a successful raster: the bitmap plus the SVG's intrinsic
/// dimensions, expressed in SVG user units (typically pixels at 1×).
/// Disposing the result disposes the underlying image.
/// </summary>
public sealed class SvgRasterResult : IDisposable
{
    public Image<Rgba32> Image { get; }
    public int NaturalWidth { get; }
    public int NaturalHeight { get; }

    internal SvgRasterResult(Image<Rgba32> image, int naturalWidth, int naturalHeight)
    {
        Image = image;
        NaturalWidth = naturalWidth;
        NaturalHeight = naturalHeight;
    }

    public void Dispose() => Image.Dispose();
}

/// <summary>
/// PNG-encoded raster output (bytes + intrinsic dimensions).
/// </summary>
public readonly record struct SvgPngResult(byte[] Bytes, int NaturalWidth, int NaturalHeight);
