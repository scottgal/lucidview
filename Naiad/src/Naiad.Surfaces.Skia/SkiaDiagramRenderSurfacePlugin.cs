using SkiaSharp;
using Svg.Skia;

namespace MermaidSharp.Rendering.Surfaces;

public sealed class SkiaDiagramRenderSurfacePlugin : IDiagramRenderSurfacePlugin
{
    static readonly IReadOnlyCollection<RenderSurfaceFormat> Formats =
    [
        RenderSurfaceFormat.Png,
        RenderSurfaceFormat.Pdf,
        RenderSurfaceFormat.Jpeg,
        RenderSurfaceFormat.Webp
    ];

    public string Name => "skia";
    public IReadOnlyCollection<RenderSurfaceFormat> SupportedFormats => Formats;
    public bool Supports(RenderSurfaceFormat format) => Formats.Contains(format);

    public RenderSurfaceOutput Render(RenderSurfaceContext context, RenderSurfaceRequest request)
    {
        var xml = context.SvgDocument.ToXml();
        using var svg = new SKSvg();
        svg.FromSvg(xml);
        if (svg.Picture is null)
        {
            throw new MermaidException("SVG surface conversion produced null picture.");
        }

        var scale = Math.Max(0.1f, request.Scale);
        var bounds = svg.Picture.CullRect;
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
        var background = ResolveBackground(request.Background, request.Format);

        return request.Format switch
        {
            RenderSurfaceFormat.Png => EncodeRaster(
                svg.Picture,
                width,
                height,
                scale,
                background,
                SKEncodedImageFormat.Png,
                100,
                "image/png"),
            RenderSurfaceFormat.Jpeg => EncodeRaster(
                svg.Picture,
                width,
                height,
                scale,
                background,
                SKEncodedImageFormat.Jpeg,
                Math.Clamp(request.Quality, 0, 100),
                "image/jpeg"),
            RenderSurfaceFormat.Webp => EncodeRaster(
                svg.Picture,
                width,
                height,
                scale,
                background,
                SKEncodedImageFormat.Webp,
                Math.Clamp(request.Quality, 0, 100),
                "image/webp"),
            RenderSurfaceFormat.Pdf => EncodePdf(svg.Picture, width, height, scale, background),
            _ => throw new MermaidException($"Skia surface does not support format '{request.Format}'.")
        };
    }

    static RenderSurfaceOutput EncodeRaster(
        SKPicture picture,
        int width,
        int height,
        float scale,
        SKColor background,
        SKEncodedImageFormat encodedFormat,
        int quality,
        string mimeType)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(background);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(encodedFormat, quality);
        return new RenderSurfaceOutput(data.ToArray(), null, mimeType);
    }

    static RenderSurfaceOutput EncodePdf(
        SKPicture picture,
        int width,
        int height,
        float scale,
        SKColor background)
    {
        using var ms = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(ms))
        {
            var canvas = pdf.BeginPage(width, height);
            if (background.Alpha > 0)
            {
                canvas.Clear(background);
            }

            canvas.Scale(scale);
            canvas.DrawPicture(picture);
            pdf.EndPage();
            pdf.Close();
        }

        return new RenderSurfaceOutput(ms.ToArray(), null, "application/pdf");
    }

    static SKColor ResolveBackground(string? requestedBackground, RenderSurfaceFormat format)
    {
        if (!string.IsNullOrWhiteSpace(requestedBackground) &&
            SKColor.TryParse(requestedBackground, out var parsed))
        {
            return parsed;
        }

        return format is RenderSurfaceFormat.Jpeg or RenderSurfaceFormat.Pdf
            ? SKColors.White
            : SKColors.Transparent;
    }
}
