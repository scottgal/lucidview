using System.Security;

namespace MermaidSharp.Rendering.Surfaces;

public sealed class XamlDiagramRenderSurfacePlugin : IDiagramRenderSurfacePlugin
{
    static readonly IReadOnlyCollection<RenderSurfaceFormat> Formats =
        [RenderSurfaceFormat.Xaml];

    public string Name => "xaml";
    public IReadOnlyCollection<RenderSurfaceFormat> SupportedFormats => Formats;
    public bool Supports(RenderSurfaceFormat format) => format == RenderSurfaceFormat.Xaml;

    public RenderSurfaceOutput Render(RenderSurfaceContext context, RenderSurfaceRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
        sb.Append(" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"");
        sb.Append($" Width=\"{Fmt(context.SvgDocument.Width)}\"");
        sb.Append($" Height=\"{Fmt(context.SvgDocument.Height)}\"");
        sb.Append('>');

        foreach (var element in context.SvgDocument.Elements)
        {
            AppendElement(sb, element);
        }

        sb.Append("</Canvas>");
        return RenderSurfaceOutput.FromText(sb.ToString(), "application/xaml+xml");
    }

    static void AppendElement(StringBuilder sb, SvgElement element)
    {
        switch (element)
        {
            case SvgGroup g:
                AppendGroup(sb, g);
                break;
            case SvgRect r:
                AppendRect(sb, r);
                break;
            case SvgRectNoXY r:
                AppendRectNoXY(sb, r);
                break;
            case SvgCircle c:
                AppendCircle(sb, c);
                break;
            case SvgEllipse e:
                AppendEllipse(sb, e);
                break;
            case SvgLine l:
                AppendLine(sb, l);
                break;
            case SvgPath p:
                AppendPath(sb, p);
                break;
            case SvgPolygon p:
                AppendPolygon(sb, p);
                break;
            case SvgPolyline p:
                AppendPolyline(sb, p);
                break;
            case SvgText t:
                AppendText(sb, t);
                break;
            case SvgMultiLineText t:
                AppendMultiLineText(sb, t);
                break;
            case SvgForeignObject fo:
                AppendForeignObject(sb, fo);
                break;
            default:
                // Best-effort fallback for future element types.
                sb.Append("<!-- Unsupported SVG element: ")
                    .Append(SecurityElement.Escape(element.GetType().Name))
                    .Append(" -->");
                break;
        }
    }

    static void AppendGroup(StringBuilder sb, SvgGroup group)
    {
        sb.Append("<Canvas");
        AppendCommonAttributes(sb, group);
        sb.Append('>');
        foreach (var child in group.Children)
        {
            AppendElement(sb, child);
        }
        sb.Append("</Canvas>");
    }

    static void AppendRect(StringBuilder sb, SvgRect rect)
    {
        sb.Append("<Rectangle");
        AppendCommonAttributes(sb, rect);
        sb.Append($" Width=\"{Fmt(rect.Width)}\"");
        sb.Append($" Height=\"{Fmt(rect.Height)}\"");
        sb.Append($" Canvas.Left=\"{Fmt(rect.X)}\"");
        sb.Append($" Canvas.Top=\"{Fmt(rect.Y)}\"");
        if (rect.Rx > 0) sb.Append($" RadiusX=\"{Fmt(rect.Rx)}\"");
        if (rect.Ry > 0) sb.Append($" RadiusY=\"{Fmt(rect.Ry)}\"");
        AppendBrushAttributes(sb, rect.Fill, rect.Stroke, rect.StrokeWidth);
        sb.Append("/>");
    }

    static void AppendRectNoXY(StringBuilder sb, SvgRectNoXY rect)
    {
        sb.Append("<Rectangle");
        AppendCommonAttributes(sb, rect);
        sb.Append($" Width=\"{Fmt(rect.Width)}\"");
        sb.Append($" Height=\"{Fmt(rect.Height)}\"");
        sb.Append("/>");
    }

    static void AppendCircle(StringBuilder sb, SvgCircle circle)
    {
        var diameter = circle.R * 2d;
        sb.Append("<Ellipse");
        AppendCommonAttributes(sb, circle);
        sb.Append($" Width=\"{Fmt(diameter)}\"");
        sb.Append($" Height=\"{Fmt(diameter)}\"");
        sb.Append($" Canvas.Left=\"{Fmt(circle.Cx - circle.R)}\"");
        sb.Append($" Canvas.Top=\"{Fmt(circle.Cy - circle.R)}\"");
        AppendBrushAttributes(sb, circle.Fill, circle.Stroke, circle.StrokeWidth);
        sb.Append("/>");
    }

    static void AppendEllipse(StringBuilder sb, SvgEllipse ellipse)
    {
        sb.Append("<Ellipse");
        AppendCommonAttributes(sb, ellipse);
        sb.Append($" Width=\"{Fmt(ellipse.Rx * 2d)}\"");
        sb.Append($" Height=\"{Fmt(ellipse.Ry * 2d)}\"");
        sb.Append($" Canvas.Left=\"{Fmt(ellipse.Cx - ellipse.Rx)}\"");
        sb.Append($" Canvas.Top=\"{Fmt(ellipse.Cy - ellipse.Ry)}\"");
        AppendBrushAttributes(sb, ellipse.Fill, ellipse.Stroke, null);
        sb.Append("/>");
    }

    static void AppendLine(StringBuilder sb, SvgLine line)
    {
        sb.Append("<Line");
        AppendCommonAttributes(sb, line);
        sb.Append($" X1=\"{Fmt(line.X1)}\"");
        sb.Append($" Y1=\"{Fmt(line.Y1)}\"");
        sb.Append($" X2=\"{Fmt(line.X2)}\"");
        sb.Append($" Y2=\"{Fmt(line.Y2)}\"");
        if (!string.IsNullOrWhiteSpace(line.Stroke)) sb.Append($" Stroke=\"{EscapeAttr(line.Stroke!)}\"");
        if (line.StrokeWidth.HasValue) sb.Append($" StrokeThickness=\"{Fmt(line.StrokeWidth.Value)}\"");
        sb.Append("/>");
    }

    static void AppendPath(StringBuilder sb, SvgPath path)
    {
        sb.Append("<Path");
        AppendCommonAttributes(sb, path);
        sb.Append($" Data=\"{EscapeAttr(path.D)}\"");
        AppendBrushAttributes(sb, path.Fill, path.Stroke, path.StrokeWidth);
        if (!string.IsNullOrWhiteSpace(path.StrokeDasharray))
        {
            // SVG can use commas/spaces; WPF/Avalonia expects comma-separated.
            var dash = path.StrokeDasharray!.Replace(" ", ",", StringComparison.Ordinal);
            sb.Append($" StrokeDashArray=\"{EscapeAttr(dash)}\"");
        }

        if (path.Opacity.HasValue)
        {
            sb.Append($" Opacity=\"{Fmt(path.Opacity.Value)}\"");
        }

        sb.Append("/>");
    }

    static void AppendPolygon(StringBuilder sb, SvgPolygon polygon)
    {
        sb.Append("<Polygon");
        AppendCommonAttributes(sb, polygon);
        sb.Append($" Points=\"{EscapeAttr(ToPoints(polygon.Points))}\"");
        AppendBrushAttributes(sb, polygon.Fill, polygon.Stroke, null);
        sb.Append("/>");
    }

    static void AppendPolyline(StringBuilder sb, SvgPolyline polyline)
    {
        sb.Append("<Polyline");
        AppendCommonAttributes(sb, polyline);
        sb.Append($" Points=\"{EscapeAttr(ToPoints(polyline.Points))}\"");
        AppendBrushAttributes(sb, polyline.Fill, polyline.Stroke, polyline.StrokeWidth);
        if (!string.IsNullOrWhiteSpace(polyline.StrokeDasharray))
        {
            var dash = polyline.StrokeDasharray!.Replace(" ", ",", StringComparison.Ordinal);
            sb.Append($" StrokeDashArray=\"{EscapeAttr(dash)}\"");
        }

        sb.Append("/>");
    }

    static void AppendText(StringBuilder sb, SvgText text)
    {
        sb.Append("<TextBlock");
        AppendCommonAttributes(sb, text);
        if (!text.OmitXY)
        {
            sb.Append($" Canvas.Left=\"{Fmt(text.X)}\"");
            sb.Append($" Canvas.Top=\"{Fmt(text.Y)}\"");
        }

        if (!string.IsNullOrWhiteSpace(text.FontSize)) sb.Append($" FontSize=\"{EscapeAttr(text.FontSize!)}\"");
        if (!string.IsNullOrWhiteSpace(text.FontFamily)) sb.Append($" FontFamily=\"{EscapeAttr(text.FontFamily!)}\"");
        if (!string.IsNullOrWhiteSpace(text.FontWeight)) sb.Append($" FontWeight=\"{EscapeAttr(text.FontWeight!)}\"");
        if (!string.IsNullOrWhiteSpace(text.Fill)) sb.Append($" Foreground=\"{EscapeAttr(text.Fill!)}\"");
        if (!string.IsNullOrWhiteSpace(text.TextAnchor))
        {
            sb.Append($" TextAlignment=\"{MapTextAnchor(text.TextAnchor!)}\"");
        }

        sb.Append(" Text=\"").Append(EscapeAttr(text.Content)).Append('"');
        sb.Append("/>");
    }

    static void AppendMultiLineText(StringBuilder sb, SvgMultiLineText text)
    {
        sb.Append("<TextBlock");
        AppendCommonAttributes(sb, text);
        sb.Append($" Canvas.Left=\"{Fmt(text.X)}\"");
        sb.Append($" Canvas.Top=\"{Fmt(text.StartY)}\"");
        if (!string.IsNullOrWhiteSpace(text.Fill)) sb.Append($" Foreground=\"{EscapeAttr(text.Fill!)}\"");
        if (!string.IsNullOrWhiteSpace(text.TextAnchor))
        {
            sb.Append($" TextAlignment=\"{MapTextAnchor(text.TextAnchor!)}\"");
        }

        sb.Append(" Text=\"")
            .Append(EscapeAttr(string.Join("\n", text.Lines)))
            .Append('"');
        sb.Append("/>");
    }

    static void AppendForeignObject(StringBuilder sb, SvgForeignObject foreignObject)
    {
        sb.Append("<Border");
        AppendCommonAttributes(sb, foreignObject);
        sb.Append($" Width=\"{Fmt(foreignObject.Width)}\"");
        sb.Append($" Height=\"{Fmt(foreignObject.Height)}\"");
        sb.Append($" Canvas.Left=\"{Fmt(foreignObject.X)}\"");
        sb.Append($" Canvas.Top=\"{Fmt(foreignObject.Y)}\"");
        sb.Append('>');
        sb.Append("<TextBlock TextWrapping=\"Wrap\" Text=\"")
            .Append(EscapeAttr(StripTags(foreignObject.HtmlContent)))
            .Append("\"/>");
        sb.Append("</Border>");
    }

    static void AppendCommonAttributes(StringBuilder sb, SvgElement element)
    {
        if (!string.IsNullOrWhiteSpace(element.Id))
        {
            sb.Append($" Tag=\"id:{EscapeAttr(element.Id!)}\"");
        }

        if (!string.IsNullOrWhiteSpace(element.Class))
        {
            sb.Append($" Classes=\"{EscapeAttr(element.Class!)}\"");
        }

        if (!string.IsNullOrWhiteSpace(element.Style))
        {
            // Preserve original style string as metadata for custom plugin consumers.
            sb.Append($" ToolTip.Tip=\"style:{EscapeAttr(element.Style!)}\"");
        }
    }

    static void AppendBrushAttributes(StringBuilder sb, string? fill, string? stroke, double? strokeWidth)
    {
        var mappedFill = MapPaint(fill);
        var mappedStroke = MapPaint(stroke);
        if (!string.IsNullOrWhiteSpace(mappedFill)) sb.Append($" Fill=\"{EscapeAttr(mappedFill)}\"");
        if (!string.IsNullOrWhiteSpace(mappedStroke)) sb.Append($" Stroke=\"{EscapeAttr(mappedStroke)}\"");
        if (strokeWidth.HasValue) sb.Append($" StrokeThickness=\"{Fmt(strokeWidth.Value)}\"");
    }

    static string? MapPaint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return "Transparent";
        }

        return value;
    }

    static string MapTextAnchor(string textAnchor) =>
        textAnchor.ToLowerInvariant() switch
        {
            "start" => "Left",
            "middle" => "Center",
            "end" => "Right",
            _ => "Left"
        };

    static string ToPoints(IEnumerable<Position> points) =>
        string.Join(" ", points.Select(p => $"{Fmt(p.X)},{Fmt(p.Y)}"));

    static string EscapeAttr(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    static string StripTags(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }

    static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
