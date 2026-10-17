using SkiaSharp;
using Svg.Skia;

namespace Mostlylucid.LucidView.Markdown.Services;

/// <summary>Shared SVG-to-PNG path for cached images and exported diagrams.</summary>
public static class SvgRasterizer
{
    public static SvgPngResult LoadAsPng(string svgContent, float scale = 1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(svgContent);
        if (!float.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));

        using var svg = new SKSvg();
        var picture = svg.FromSvg(svgContent)
            ?? throw new InvalidOperationException("SVG produced no picture.");
        var bounds = picture.CullRect;
        var width = Math.Ceiling(bounds.Width * scale);
        var height = Math.Ceiling(bounds.Height * scale);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1 ||
            width > 16384 || height > 16384 || width * height > 64_000_000)
            throw new InvalidOperationException("SVG raster dimensions are invalid or too large.");

        using var bitmap = new SKBitmap((int)width, (int)height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
            canvas.Flush();
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("SVG produced no PNG.");
        return new SvgPngResult(data.ToArray(), (int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height));
    }
}

public readonly record struct SvgPngResult(byte[] Bytes, int NaturalWidth, int NaturalHeight);
