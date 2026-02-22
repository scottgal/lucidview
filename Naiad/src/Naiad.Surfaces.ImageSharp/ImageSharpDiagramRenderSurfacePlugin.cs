using System.Globalization;
using System.Numerics;
using System.Diagnostics.CodeAnalysis;
using MermaidSharp.Rendering;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using DrawingPath = SixLabors.ImageSharp.Drawing.Path;
using Regex = System.Text.RegularExpressions.Regex;

namespace MermaidSharp.Rendering.Surfaces;

public sealed class ImageSharpDiagramRenderSurfacePlugin : IDiagramRenderSurfacePlugin
{
    static readonly IReadOnlyCollection<RenderSurfaceFormat> Formats =
    [
        RenderSurfaceFormat.Png,
        RenderSurfaceFormat.Jpeg,
        RenderSurfaceFormat.Webp
    ];

    static readonly Regex TransformRegex =
        new(@"(?<name>[A-Za-z]+)\((?<args>[^)]*)\)", System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly Regex NumberRegex =
        new(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly Regex TagRegex =
        new("<[^>]+>", System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly Regex WhitespaceRegex =
        new(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly Regex PathCommandToNumberRegex =
        new(@"(?<=[MmZzLlHhVvCcSsQqTtAa])(?=[-+0-9.])", System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly Regex NumberToPathCommandRegex =
        new(@"(?<=[0-9.])(?=[MmZzLlHhVvCcSsQqTtAa])", System.Text.RegularExpressions.RegexOptions.Compiled);

    public string Name => "imagesharp";
    public IReadOnlyCollection<RenderSurfaceFormat> SupportedFormats => Formats;
    public bool Supports(RenderSurfaceFormat format) => Formats.Contains(format);

    public RenderSurfaceOutput Render(RenderSurfaceContext context, RenderSurfaceRequest request)
    {
        var scale = Math.Max(0.1f, request.Scale);
        var width = Math.Max(1, (int)Math.Ceiling(context.SvgDocument.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(context.SvgDocument.Height * scale));

        using var image = new Image<Rgba32>(width, height);
        var background = ResolveBackground(request.Background, request.Format);
        image.Mutate(ctx => ctx.Clear(background));

        var rootTransform = Matrix3x2.CreateScale(scale);
        image.Mutate(ctx =>
        {
            foreach (var element in context.SvgDocument.Elements)
            {
                DrawElement(ctx, element, rootTransform);
            }
        });

        using var ms = new MemoryStream();
        var mimeType = request.Format switch
        {
            RenderSurfaceFormat.Png => EncodePng(image, ms),
            RenderSurfaceFormat.Jpeg => EncodeJpeg(image, ms, request.Quality),
            RenderSurfaceFormat.Webp => EncodeWebp(image, ms, request.Quality),
            _ => throw new MermaidException($"ImageSharp surface does not support format '{request.Format}'.")
        };

        return new RenderSurfaceOutput(ms.ToArray(), null, mimeType);
    }

    static string EncodePng(Image<Rgba32> image, Stream stream)
    {
        image.SaveAsPng(stream);
        return "image/png";
    }

    static string EncodeJpeg(Image<Rgba32> image, Stream stream, int quality)
    {
        image.SaveAsJpeg(stream, new JpegEncoder { Quality = Math.Clamp(quality, 0, 100) });
        return "image/jpeg";
    }

    static string EncodeWebp(Image<Rgba32> image, Stream stream, int quality)
    {
        image.SaveAsWebp(stream, new WebpEncoder { Quality = Math.Clamp(quality, 0, 100) });
        return "image/webp";
    }

    static void DrawElement(IImageProcessingContext ctx, SvgElement element, Matrix3x2 parentTransform)
    {
        var transform = Combine(parentTransform, ParseTransform(element.Transform));

        switch (element)
        {
            case SvgGroup group:
                foreach (var child in group.Children)
                {
                    DrawElement(ctx, child, transform);
                }
                break;

            case SvgRect rect:
                DrawRect(ctx, rect, transform);
                break;

            case SvgRectNoXY rectNoXY:
                DrawRectNoXY(ctx, rectNoXY, transform);
                break;

            case SvgCircle circle:
                DrawCircle(ctx, circle, transform);
                break;

            case SvgEllipse ellipse:
                DrawEllipse(ctx, ellipse, transform);
                break;

            case SvgLine line:
                DrawLine(ctx, line, transform);
                break;

            case SvgPath path:
                DrawPath(ctx, path, transform);
                break;

            case SvgPolygon polygon:
                DrawPolygon(ctx, polygon, transform);
                break;

            case SvgPolyline polyline:
                DrawPolyline(ctx, polyline, transform);
                break;

            case SvgText text:
                DrawText(ctx, text, transform);
                break;

            case SvgMultiLineText text:
                DrawMultiLineText(ctx, text, transform);
                break;

            case SvgForeignObject foreignObject:
                DrawForeignObject(ctx, foreignObject, transform);
                break;
        }
    }

    static void DrawRect(IImageProcessingContext ctx, SvgRect rect, Matrix3x2 transform)
    {
        var style = ParseStyle(rect.Style);
        var fill = FirstNotEmpty(rect.Fill, GetStyle(style, "fill"));
        var stroke = FirstNotEmpty(rect.Stroke, GetStyle(style, "stroke"));
        var strokeWidth = rect.StrokeWidth ?? ParseDouble(GetStyle(style, "stroke-width"));
        var opacity = ParseDouble(GetStyle(style, "opacity"));

        IPath path;
        if (rect.Rx > 0 || rect.Ry > 0)
        {
            var rx = (float)Math.Max(rect.Rx, 0);
            var ry = (float)Math.Max(rect.Ry, 0);
            var d = BuildRoundedRectPath((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, rx, ry);
            if (!TryParsePath(d, out var rounded))
            {
                path = new RectangularPolygon((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
            }
            else
            {
                path = rounded;
            }
        }
        else
        {
            path = new RectangularPolygon((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        }

        DrawShape(ctx, path, transform, fill, stroke, strokeWidth, fillWhenUnset: true, opacity);
    }

    static void DrawRectNoXY(IImageProcessingContext ctx, SvgRectNoXY rect, Matrix3x2 transform)
    {
        var style = ParseStyle(rect.Style);
        var fill = FirstNotEmpty(GetStyle(style, "fill"));
        var stroke = FirstNotEmpty(GetStyle(style, "stroke"));
        var strokeWidth = ParseDouble(GetStyle(style, "stroke-width"));
        var opacity = ParseDouble(GetStyle(style, "opacity"));

        var path = new RectangularPolygon(0, 0, (float)rect.Width, (float)rect.Height);
        DrawShape(ctx, path, transform, fill, stroke, strokeWidth, fillWhenUnset: true, opacity);
    }

    static void DrawCircle(IImageProcessingContext ctx, SvgCircle circle, Matrix3x2 transform)
    {
        var style = ParseStyle(circle.Style);
        var fill = FirstNotEmpty(circle.Fill, GetStyle(style, "fill"));
        var stroke = FirstNotEmpty(circle.Stroke, GetStyle(style, "stroke"));
        var strokeWidth = circle.StrokeWidth ?? ParseDouble(GetStyle(style, "stroke-width"));
        var opacity = ParseDouble(GetStyle(style, "opacity"));

        var path = new EllipsePolygon((float)circle.Cx, (float)circle.Cy, (float)circle.R);
        DrawShape(ctx, path, transform, fill, stroke, strokeWidth, fillWhenUnset: true, opacity);
    }

    static void DrawEllipse(IImageProcessingContext ctx, SvgEllipse ellipse, Matrix3x2 transform)
    {
        var style = ParseStyle(ellipse.Style);
        var fill = FirstNotEmpty(ellipse.Fill, GetStyle(style, "fill"));
        var stroke = FirstNotEmpty(ellipse.Stroke, GetStyle(style, "stroke"));
        var strokeWidth = ParseDouble(GetStyle(style, "stroke-width"));
        var opacity = ParseDouble(GetStyle(style, "opacity"));

        var path = new EllipsePolygon((float)ellipse.Cx, (float)ellipse.Cy, (float)ellipse.Rx, (float)ellipse.Ry);
        DrawShape(ctx, path, transform, fill, stroke, strokeWidth, fillWhenUnset: true, opacity);
    }

    static void DrawLine(IImageProcessingContext ctx, SvgLine line, Matrix3x2 transform)
    {
        var style = ParseStyle(line.Style);
        var stroke = FirstNotEmpty(line.Stroke, GetStyle(style, "stroke"));
        var strokeWidth = line.StrokeWidth ?? ParseDouble(GetStyle(style, "stroke-width"));
        var opacity = ParseDouble(GetStyle(style, "opacity"));

        var pathBuilder = new PathBuilder();
        pathBuilder.AddLine(
            new PointF((float)line.X1, (float)line.Y1),
            new PointF((float)line.X2, (float)line.Y2));
        var path = pathBuilder.Build();
        DrawShape(ctx, path, transform, null, stroke, strokeWidth, fillWhenUnset: false, opacity);
    }

    static void DrawPath(IImageProcessingContext ctx, SvgPath path, Matrix3x2 transform)
    {
        var style = ParseStyle(path.Style);
        var fill = FirstNotEmpty(path.Fill, GetStyle(style, "fill"));
        var stroke = FirstNotEmpty(path.Stroke, GetStyle(style, "stroke"));
        var strokeWidth = path.StrokeWidth ?? ParseDouble(GetStyle(style, "stroke-width"));
        var opacity = path.Opacity ?? ParseDouble(GetStyle(style, "opacity"));

        if (!TryParsePath(path.D, out var geometry))
        {
            return;
        }

        DrawShape(ctx, geometry, transform, fill, stroke, strokeWidth, fillWhenUnset: false, opacity);
    }

    static bool TryParsePath(string? value, [NotNullWhen(true)] out IPath? geometry)
    {
        geometry = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (TryParsePathCore(value, out geometry))
        {
            return true;
        }

        var normalized = NormalizePathData(value);
        return TryParsePathCore(normalized, out geometry);
    }

    static bool TryParsePathCore(string value, [NotNullWhen(true)] out IPath? geometry)
    {
        try
        {
            return DrawingPath.TryParseSvgPath(value, out geometry);
        }
        catch (Exception)
        {
            geometry = null;
            return false;
        }
    }

    static string NormalizePathData(string value)
    {
        var normalized = PathCommandToNumberRegex.Replace(value, " ");
        normalized = NumberToPathCommandRegex.Replace(normalized, " ");
        return normalized;
    }

    static void DrawPolygon(IImageProcessingContext ctx, SvgPolygon polygon, Matrix3x2 transform)
    {
        var style = ParseStyle(polygon.Style);
        var fill = FirstNotEmpty(polygon.Fill, GetStyle(style, "fill"));
        var stroke = FirstNotEmpty(polygon.Stroke, GetStyle(style, "stroke"));
        var strokeWidth = ParseDouble(GetStyle(style, "stroke-width"));
        var opacity = ParseDouble(GetStyle(style, "opacity"));

        if (polygon.Points.Count < 2)
        {
            return;
        }

        var points = polygon.Points
            .Select(x => new PointF((float)x.X, (float)x.Y))
            .ToArray();
        var shape = new Polygon(points);
        DrawShape(ctx, shape, transform, fill, stroke, strokeWidth, fillWhenUnset: true, opacity);
    }

    static void DrawPolyline(IImageProcessingContext ctx, SvgPolyline polyline, Matrix3x2 transform)
    {
        var style = ParseStyle(polyline.Style);
        var fill = FirstNotEmpty(polyline.Fill, GetStyle(style, "fill"));
        var stroke = FirstNotEmpty(polyline.Stroke, GetStyle(style, "stroke"));
        var strokeWidth = polyline.StrokeWidth ?? ParseDouble(GetStyle(style, "stroke-width"));
        var opacity = ParseDouble(GetStyle(style, "opacity"));

        if (polyline.Points.Count < 2)
        {
            return;
        }

        var points = polyline.Points
            .Select(x => new PointF((float)x.X, (float)x.Y))
            .ToArray();
        var pathBuilder = new PathBuilder();
        pathBuilder.AddLines(points);
        var path = pathBuilder.Build();
        DrawShape(ctx, path, transform, fill, stroke, strokeWidth, fillWhenUnset: false, opacity);
    }

    static void DrawText(IImageProcessingContext ctx, SvgText text, Matrix3x2 transform)
    {
        if (string.IsNullOrWhiteSpace(text.Content))
        {
            return;
        }

        var style = ParseStyle(text.Style);
        var fill = FirstNotEmpty(text.Fill, GetStyle(style, "fill"));
        var textAnchor = FirstNotEmpty(text.TextAnchor, GetStyle(style, "text-anchor"));
        var fontSize = FirstNotEmpty(text.FontSize, GetStyle(style, "font-size"));
        var fontFamily = FirstNotEmpty(text.FontFamily, GetStyle(style, "font-family"));
        var fontWeight = FirstNotEmpty(text.FontWeight, GetStyle(style, "font-weight"));

        var font = ResolveFont(fontFamily, fontSize, fontWeight);
        var color = ResolvePaint(fill, Color.Black, opacity: null) ?? Color.Black;
        var origin = text.OmitXY ? Vector2.Zero : new Vector2((float)text.X, (float)text.Y);
        DrawTextAt(ctx, text.Content, origin, transform, font, color, textAnchor);
    }

    static void DrawMultiLineText(IImageProcessingContext ctx, SvgMultiLineText text, Matrix3x2 transform)
    {
        if (text.Lines.Length == 0)
        {
            return;
        }

        var style = ParseStyle(text.Style);
        var fill = FirstNotEmpty(text.Fill, GetStyle(style, "fill"));
        var textAnchor = FirstNotEmpty(text.TextAnchor, GetStyle(style, "text-anchor"));
        var font = ResolveFont(null, GetStyle(style, "font-size"), GetStyle(style, "font-weight"));
        var color = ResolvePaint(fill, Color.Black, opacity: null) ?? Color.Black;
        var lineHeight = text.LineHeight > 0 ? (float)text.LineHeight : font.Size * 1.2f;

        for (var i = 0; i < text.Lines.Length; i++)
        {
            var line = text.Lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var origin = new Vector2((float)text.X, (float)(text.StartY + i * lineHeight));
            DrawTextAt(ctx, line, origin, transform, font, color, textAnchor);
        }
    }

    static void DrawForeignObject(IImageProcessingContext ctx, SvgForeignObject foreignObject, Matrix3x2 transform)
    {
        var style = ParseStyle(foreignObject.Style);
        var fill = ResolvePaint(GetStyle(style, "fill"), Color.Black, opacity: null) ?? Color.Black;
        var text = StripTags(foreignObject.HtmlContent);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var font = ResolveFont(null, "12", null);
        var origin = new Vector2((float)foreignObject.X, (float)(foreignObject.Y + font.Size));
        DrawTextAt(ctx, text, origin, transform, font, fill, "start");
    }

    static void DrawTextAt(
        IImageProcessingContext ctx,
        string text,
        Vector2 source,
        Matrix3x2 transform,
        Font font,
        Color color,
        string? textAnchor)
    {
        var options = new TextOptions(font);
        var size = TextMeasurer.MeasureSize(text, options);
        var point = TransformPoint(transform, source);

        if (string.Equals(textAnchor, "middle", StringComparison.OrdinalIgnoreCase))
        {
            point.X -= size.Width / 2;
        }
        else if (string.Equals(textAnchor, "end", StringComparison.OrdinalIgnoreCase))
        {
            point.X -= size.Width;
        }

        point.Y -= font.Size * 0.8f; // SVG text uses baseline, DrawText uses top-left.
        ctx.DrawText(text, font, color, point);
    }

    static void DrawShape(
        IImageProcessingContext ctx,
        IPath shape,
        Matrix3x2 transform,
        string? fill,
        string? stroke,
        double? strokeWidth,
        bool fillWhenUnset,
        double? opacity)
    {
        var drawPath = transform.IsIdentity ? shape : shape.Transform(transform);
        var fillColor = ResolvePaint(fill, fillWhenUnset ? Color.Black : null, opacity);
        if (fillColor.HasValue)
        {
            ctx.Fill(fillColor.Value, drawPath);
        }

        var strokeColor = ResolvePaint(stroke, null, opacity);
        if (strokeColor.HasValue)
        {
            var thickness = (float)Math.Max(0.1, (strokeWidth ?? 1d) * GetApproxScale(transform));
            ctx.Draw(strokeColor.Value, thickness, drawPath);
        }
    }

    static Matrix3x2 Combine(Matrix3x2 left, Matrix3x2 right) =>
        Matrix3x2.Multiply(left, right);

    static Matrix3x2 ParseTransform(string? transform)
    {
        if (string.IsNullOrWhiteSpace(transform))
        {
            return Matrix3x2.Identity;
        }

        var current = Matrix3x2.Identity;
        foreach (System.Text.RegularExpressions.Match match in TransformRegex.Matches(transform))
        {
            var name = match.Groups["name"].Value.ToLowerInvariant();
            var args = ParseNumbers(match.Groups["args"].Value);
            Matrix3x2 operation;
            switch (name)
            {
                case "matrix" when args.Length >= 6:
                    operation = new Matrix3x2(args[0], args[1], args[2], args[3], args[4], args[5]);
                    break;
                case "translate":
                    operation = Matrix3x2.CreateTranslation(
                        args.Length > 0 ? args[0] : 0,
                        args.Length > 1 ? args[1] : 0);
                    break;
                case "scale":
                    operation = Matrix3x2.CreateScale(
                        args.Length > 0 ? args[0] : 1,
                        args.Length > 1 ? args[1] : (args.Length > 0 ? args[0] : 1));
                    break;
                case "rotate" when args.Length >= 3:
                    operation = Matrix3x2.CreateRotation(
                        DegreesToRadians(args[0]),
                        new Vector2(args[1], args[2]));
                    break;
                case "rotate":
                    operation = Matrix3x2.CreateRotation(
                        DegreesToRadians(args.Length > 0 ? args[0] : 0));
                    break;
                case "skewx":
                    operation = new Matrix3x2(
                        1,
                        0,
                        MathF.Tan(DegreesToRadians(args.Length > 0 ? args[0] : 0)),
                        1,
                        0,
                        0);
                    break;
                case "skewy":
                    operation = new Matrix3x2(
                        1,
                        MathF.Tan(DegreesToRadians(args.Length > 0 ? args[0] : 0)),
                        0,
                        1,
                        0,
                        0);
                    break;
                default:
                    operation = Matrix3x2.Identity;
                    break;
            }

            current = Combine(current, operation);
        }

        return current;
    }

    static float[] ParseNumbers(string args) =>
    [
        .. NumberRegex.Matches(args)
            .Select(x => float.TryParse(x.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0f)
    ];

    static PointF TransformPoint(Matrix3x2 matrix, Vector2 point)
    {
        var transformed = Vector2.Transform(point, matrix);
        return new PointF(transformed.X, transformed.Y);
    }

    static float GetApproxScale(Matrix3x2 matrix)
    {
        var sx = Math.Sqrt((matrix.M11 * matrix.M11) + (matrix.M12 * matrix.M12));
        var sy = Math.Sqrt((matrix.M21 * matrix.M21) + (matrix.M22 * matrix.M22));
        var avg = (sx + sy) / 2d;
        return (float)Math.Max(0.1d, avg);
    }

    static float DegreesToRadians(float degrees) =>
        degrees * (MathF.PI / 180f);

    static Color ResolveBackground(string? value, RenderSurfaceFormat format)
    {
        if (ResolvePaint(value, null, null) is { } parsed)
        {
            return parsed;
        }

        return format == RenderSurfaceFormat.Jpeg
            ? Color.White
            : Color.Transparent;
    }

    static Color? ResolvePaint(string? value, Color? fallback, double? opacity)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ApplyOpacity(fallback, opacity);
        }

        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            return ApplyOpacity(fallback, opacity);
        }

        if (!Color.TryParse(value, out var color))
        {
            return ApplyOpacity(fallback, opacity);
        }

        return ApplyOpacity(color, opacity);
    }

    static Color? ApplyOpacity(Color? color, double? opacity)
    {
        if (color is null)
        {
            return null;
        }

        if (!opacity.HasValue)
        {
            return color.Value;
        }

        var factor = Math.Clamp(opacity.Value, 0d, 1d);
        var rgba = color.Value.ToPixel<Rgba32>();
        rgba.A = (byte)Math.Clamp((int)Math.Round(rgba.A * factor), 0, 255);
        return Color.FromPixel(rgba);
    }

    static Dictionary<string, string> ParseStyle(string? style)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(style))
        {
            return dict;
        }

        var segments = style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            var idx = segment.IndexOf(':');
            if (idx <= 0 || idx >= segment.Length - 1)
            {
                continue;
            }

            var key = segment[..idx].Trim();
            var value = segment[(idx + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0)
            {
                dict[key] = value;
            }
        }

        return dict;
    }

    static string? GetStyle(IReadOnlyDictionary<string, string> style, string key) =>
        style.TryGetValue(key, out var value) ? value : null;

    static string? FirstNotEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    static double? ParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^2];
        }

        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    static Font ResolveFont(string? fontFamily, string? fontSize, string? fontWeight)
    {
        var size = (float)Math.Max(1, ParseDouble(fontSize) ?? 12);
        var style = string.Equals(fontWeight, "bold", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fontWeight, "700", StringComparison.OrdinalIgnoreCase)
            ? FontStyle.Bold
            : FontStyle.Regular;

        foreach (var candidate in SplitFontFamilies(fontFamily))
        {
            if (SystemFonts.TryGet(candidate, out var family))
            {
                return family.CreateFont(size, style);
            }
        }

        if (SystemFonts.Families.FirstOrDefault() is { } firstFamily)
        {
            return firstFamily.CreateFont(size, style);
        }

        throw new MermaidException("ImageSharp surface requires at least one available system font.");
    }

    static IEnumerable<string> SplitFontFamilies(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            yield break;
        }

        foreach (var part in fontFamily.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = part.Trim().Trim('\'', '"');
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                yield return candidate;
            }
        }
    }

    static string StripTags(string html)
    {
        var text = TagRegex.Replace(html, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return WhitespaceRegex.Replace(text, " ").Trim();
    }

    static string BuildRoundedRectPath(float x, float y, float width, float height, float rx, float ry)
    {
        rx = Math.Min(rx, width / 2f);
        ry = Math.Min(ry, height / 2f);
        return FormattableString.Invariant(
            $"M{x + rx},{y} H{x + width - rx} A{rx},{ry} 0 0 1 {x + width},{y + ry} V{y + height - ry} A{rx},{ry} 0 0 1 {x + width - rx},{y + height} H{x + rx} A{rx},{ry} 0 0 1 {x},{y + height - ry} V{y + ry} A{rx},{ry} 0 0 1 {x + rx},{y} Z");
    }
}
