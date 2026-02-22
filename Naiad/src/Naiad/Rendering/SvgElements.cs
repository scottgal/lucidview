using static MermaidSharp.Rendering.RenderUtils;
using System.Net;

namespace MermaidSharp.Rendering;

public abstract class SvgElement
{
    public string? Id { get; set; }
    public string? Class { get; set; }
    public string? Style { get; set; }
    public string? Transform { get; set; }

    public abstract string ToXml();

    protected string CommonAttributes()
    {
        if (Id is null && Class is null && Style is null && Transform is null)
            return "";

        var sb = new StringBuilder();
        if (Id is not null) sb.Append($" id=\"{EscapeAttribute(Id)}\"");
        if (Class is not null) sb.Append($" class=\"{EscapeAttribute(Class)}\"");
        var safeStyle = SanitizeStyleAttribute(Style);
        if (safeStyle is not null) sb.Append($" style=\"{EscapeAttribute(safeStyle)}\"");
        var safeTransform = SanitizeTransformAttribute(Transform);
        if (safeTransform is not null) sb.Append($" transform=\"{EscapeAttribute(safeTransform)}\"");
        return sb.ToString();
    }

    protected void AppendCommonAttributes(StringBuilder sb)
    {
        if (Id is not null) sb.Append($" id=\"{EscapeAttribute(Id)}\"");
        if (Class is not null) sb.Append($" class=\"{EscapeAttribute(Class)}\"");
        var safeStyle = SanitizeStyleAttribute(Style);
        if (safeStyle is not null) sb.Append($" style=\"{EscapeAttribute(safeStyle)}\"");
        var safeTransform = SanitizeTransformAttribute(Transform);
        if (safeTransform is not null) sb.Append($" transform=\"{EscapeAttribute(safeTransform)}\"");
    }

    // Only escape &, <, >, " for double-quoted XML attributes.
    // WebUtility.HtmlEncode also encodes ' → &#39; which is unnecessary
    // inside double-quoted attributes and breaks SVG font-family values.
    protected static string EscapeAttribute(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    protected static string? SanitizeStyleAttribute(string? style)
    {
        if (string.IsNullOrWhiteSpace(style))
            return null;

        var sanitized = SecurityValidator.SanitizeCss(style);
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    protected static string? SanitizeTransformAttribute(string? transform)
    {
        if (string.IsNullOrWhiteSpace(transform))
            return null;

        var trimmed = transform.Trim();
        if (trimmed.IndexOfAny(['<', '>', '"', '\'']) >= 0)
            return null;
        if (trimmed.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("expression", StringComparison.OrdinalIgnoreCase))
            return null;

        return trimmed;
    }
}

public class SvgGroup : SvgElement
{
    public List<SvgElement> Children { get; } = [];

    public override string ToXml()
    {
        var sb = new StringBuilder();
        sb.Append("<g");
        AppendCommonAttributes(sb);
        if (Children.Count == 0)
        {
            sb.Append("/>");
        }
        else
        {
            sb.Append('>');
            foreach (var child in Children)
            {
                sb.Append(child.ToXml());
            }
            sb.Append("</g>");
        }
        return sb.ToString();
    }
}

public class SvgRect : SvgElement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Rx { get; set; }
    public double Ry { get; set; }
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public double? StrokeWidth { get; set; }

    public override string ToXml()
    {
        var sb = new StringBuilder();
        sb.Append($"<rect x=\"{Fmt(X)}\" y=\"{Fmt(Y)}\" width=\"{Fmt(Width)}\" height=\"{Fmt(Height)}\"");
        if (Rx > 0) sb.Append($" rx=\"{Fmt(Rx)}\"");
        if (Ry > 0) sb.Append($" ry=\"{Fmt(Ry)}\"");
        if (Fill is not null) sb.Append($" fill=\"{EscapeAttribute(Fill)}\"");
        if (Stroke is not null) sb.Append($" stroke=\"{EscapeAttribute(Stroke)}\"");
        if (StrokeWidth.HasValue) sb.Append($" stroke-width=\"{Fmt(StrokeWidth.Value)}\"");
        AppendCommonAttributes(sb);
        sb.Append("/>");
        return sb.ToString();
    }
}

public class SvgRectNoXY : SvgElement
{
    public double Width { get; set; }
    public double Height { get; set; }

    public override string ToXml()
    {
        var sb = new StringBuilder();
        sb.Append($"<rect width=\"{Fmt(Width)}\" height=\"{Fmt(Height)}\"");
        AppendCommonAttributes(sb);
        sb.Append("/>");
        return sb.ToString();
    }
}

public class SvgCircle : SvgElement
{
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double R { get; set; }
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public double? StrokeWidth { get; set; }

    public override string ToXml()
    {
        var builder = new StringBuilder();
        builder.Append($"<circle cx=\"{Fmt(Cx)}\" cy=\"{Fmt(Cy)}\" r=\"{Fmt(R)}\"");
        if (Fill is not null) builder.Append($" fill=\"{EscapeAttribute(Fill)}\"");
        if (Stroke is not null) builder.Append($" stroke=\"{EscapeAttribute(Stroke)}\"");
        if (StrokeWidth.HasValue) builder.Append($" stroke-width=\"{Fmt(StrokeWidth.Value)}\"");
        AppendCommonAttributes(builder);
        builder.Append("/>");
        return builder.ToString();
    }
}

public class SvgEllipse : SvgElement
{
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double Rx { get; set; }
    public double Ry { get; set; }
    public string? Fill { get; set; }
    public string? Stroke { get; set; }

    public override string ToXml()
    {
        var sb = new StringBuilder();
        sb.Append($"<ellipse cx=\"{Fmt(Cx)}\" cy=\"{Fmt(Cy)}\" rx=\"{Fmt(Rx)}\" ry=\"{Fmt(Ry)}\"");
        if (Fill is not null) sb.Append($" fill=\"{EscapeAttribute(Fill)}\"");
        if (Stroke is not null) sb.Append($" stroke=\"{EscapeAttribute(Stroke)}\"");
        AppendCommonAttributes(sb);
        sb.Append("/>");
        return sb.ToString();
    }
}

public class SvgLine : SvgElement
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public string? Stroke { get; set; }
    public double? StrokeWidth { get; set; }
    public string? StrokeDasharray { get; set; }

    public override string ToXml()
    {
        var sb = new StringBuilder();
        sb.Append($"<line x1=\"{Fmt(X1)}\" y1=\"{Fmt(Y1)}\" x2=\"{Fmt(X2)}\" y2=\"{Fmt(Y2)}\"");
        if (Stroke is not null) sb.Append($" stroke=\"{EscapeAttribute(Stroke)}\"");
        if (StrokeWidth.HasValue) sb.Append($" stroke-width=\"{Fmt(StrokeWidth.Value)}\"");
        if (StrokeDasharray is not null) sb.Append($" stroke-dasharray=\"{EscapeAttribute(StrokeDasharray)}\"");
        AppendCommonAttributes(sb);
        sb.Append("/>");
        return sb.ToString();
    }
}

public class SvgPath : SvgElement
{
    public required string D { get; set; }
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public double? StrokeWidth { get; set; }
    public string? StrokeDasharray { get; set; }
    public string? MarkerStart { get; set; }
    public string? MarkerEnd { get; set; }
    public double? Opacity { get; set; }

    public override string ToXml()
    {
        var sb = new StringBuilder();
        sb.Append($"<path d=\"{EscapeAttribute(D)}\"");
        if (Fill is not null) sb.Append($" fill=\"{EscapeAttribute(Fill)}\"");
        if (Stroke is not null) sb.Append($" stroke=\"{EscapeAttribute(Stroke)}\"");
        if (StrokeWidth.HasValue) sb.Append($" stroke-width=\"{Fmt(StrokeWidth.Value)}\"");
        if (StrokeDasharray is not null) sb.Append($" stroke-dasharray=\"{EscapeAttribute(StrokeDasharray)}\"");
        if (MarkerStart is not null) sb.Append($" marker-start=\"{EscapeAttribute(MarkerStart)}\"");
        if (MarkerEnd is not null) sb.Append($" marker-end=\"{EscapeAttribute(MarkerEnd)}\"");
        if (Opacity.HasValue) sb.Append($" opacity=\"{Fmt(Opacity.Value)}\"");
        AppendCommonAttributes(sb);
        sb.Append("/>");
        return sb.ToString();
    }
}

public class SvgPolygon : SvgElement
{
    public List<Position> Points { get; } = [];
    public string? Fill { get; set; }
    public string? Stroke { get; set; }

    public override string ToXml()
    {
        var sb = new StringBuilder();
        sb.Append("<polygon points=\"");
        SvgHelpers.AppendPoints(sb, Points);
        sb.Append('"');
        if (Fill is not null) sb.Append($" fill=\"{EscapeAttribute(Fill)}\"");
        if (Stroke is not null) sb.Append($" stroke=\"{EscapeAttribute(Stroke)}\"");
        AppendCommonAttributes(sb);
        sb.Append("/>");
        return sb.ToString();
    }
}

public class SvgPolyline : SvgElement
{
    public List<Position> Points { get; } = [];
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public double? StrokeWidth { get; set; }
    public string? StrokeDasharray { get; set; }
    public string? MarkerEnd { get; set; }

    public override string ToXml()
    {
        var sb = new StringBuilder();
        sb.Append("<polyline points=\"");
        SvgHelpers.AppendPoints(sb, Points);
        sb.Append('"');
        if (Fill is not null) sb.Append($" fill=\"{EscapeAttribute(Fill)}\"");
        if (Stroke is not null) sb.Append($" stroke=\"{EscapeAttribute(Stroke)}\"");
        if (StrokeWidth.HasValue) sb.Append($" stroke-width=\"{Fmt(StrokeWidth.Value)}\"");
        if (StrokeDasharray is not null) sb.Append($" stroke-dasharray=\"{EscapeAttribute(StrokeDasharray)}\"");
        if (MarkerEnd is not null) sb.Append($" marker-end=\"{EscapeAttribute(MarkerEnd)}\"");
        AppendCommonAttributes(sb);
        sb.Append("/>");
        return sb.ToString();
    }
}

internal static class SvgHelpers
{
    internal static void AppendPoints(StringBuilder sb, List<Position> points)
    {
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append($"{Fmt(points[i].X)},{Fmt(points[i].Y)}");
        }
    }
}

public class SvgText : SvgElement
{
    public double X { get; set; }
    public double Y { get; set; }
    public bool OmitXY { get; set; } // When true, don't output x/y attributes (for transformed text)
    public required string Content { get; set; }
    public string? TextAnchor { get; set; }
    public string? DominantBaseline { get; set; }
    public string? FontSize { get; set; }
    public string? FontFamily { get; set; }
    public string? FontWeight { get; set; }
    public string? Fill { get; set; }

    public override string ToXml()
    {
        var sb = new StringBuilder();
        sb.Append("<text");

        // For transformed text (OmitXY=true), mermaid.ink uses: transform, class, style order
        if (OmitXY)
        {
            var safeTransform = SanitizeTransformAttribute(Transform);
            if (safeTransform is not null) sb.Append($" transform=\"{EscapeAttribute(safeTransform)}\"");
            if (Class is not null) sb.Append($" class=\"{EscapeAttribute(Class)}\"");
            var safeStyle = SanitizeStyleAttribute(Style);
            if (safeStyle is not null) sb.Append($" style=\"{EscapeAttribute(safeStyle)}\"");
        }
        else
        {
            sb.Append($" x=\"{Fmt(X)}\" y=\"{Fmt(Y)}\"");
            if (TextAnchor is not null) sb.Append($" text-anchor=\"{EscapeAttribute(TextAnchor)}\"");
            if (DominantBaseline is not null) sb.Append($" dominant-baseline=\"{EscapeAttribute(DominantBaseline)}\"");
            if (FontSize is not null) sb.Append($" font-size=\"{EscapeAttribute(FontSize)}\"");
            if (FontFamily is not null) sb.Append($" font-family=\"{EscapeAttribute(FontFamily.Replace('"', '\''))}\"");
            if (FontWeight is not null) sb.Append($" font-weight=\"{EscapeAttribute(FontWeight)}\"");
            if (Fill is not null) sb.Append($" fill=\"{EscapeAttribute(Fill)}\"");
            AppendCommonAttributes(sb);
        }

        if (string.IsNullOrEmpty(Content))
        {
            sb.Append("/>");
        }
        else
        {
            sb.Append($">{EscapeXml(Content)}</text>");
        }
        return sb.ToString();
    }

    static string EscapeXml(string text)
    {
        // Fast path: most text has no special characters
        if (!text.AsSpan().ContainsAny("&<>\"'"))
            return text;

        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}

public class SvgMultiLineText : SvgElement
{
    public double X { get; set; }
    public double StartY { get; set; }
    public double LineHeight { get; set; }
    public required string[] Lines { get; set; }
    public string? TextAnchor { get; set; }
    public string? Fill { get; set; }

    public override string ToXml()
    {
        var sb = new StringBuilder();
        sb.Append($"<text x=\"{Fmt(X)}\" y=\"{Fmt(StartY)}\"");
        if (TextAnchor is not null) sb.Append($" text-anchor=\"{EscapeAttribute(TextAnchor)}\"");
        if (Fill is not null) sb.Append($" fill=\"{EscapeAttribute(Fill)}\"");
        AppendCommonAttributes(sb);
        sb.Append('>');
        for (var i = 0; i < Lines.Length; i++)
        {
            var dy = i == 0 ? "0" : Fmt(LineHeight);
            sb.Append($"<tspan x=\"{Fmt(X)}\" dy=\"{dy}\">{EscapeXml(Lines[i])}</tspan>");
        }
        sb.Append("</text>");
        return sb.ToString();
    }

    static string EscapeXml(string text)
    {
        if (!text.AsSpan().ContainsAny("&<>"))
            return text;

        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}

public class SvgForeignObject : SvgElement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public required string HtmlContent { get; set; }

    public override string ToXml()
    {
        var sb = new StringBuilder();
        sb.Append($"<foreignObject x=\"{Fmt(X)}\" y=\"{Fmt(Y)}\" width=\"{Fmt(Width)}\" height=\"{Fmt(Height)}\"");
        AppendCommonAttributes(sb);
        sb.Append('>');
        sb.Append($"<div xmlns=\"http://www.w3.org/1999/xhtml\" style=\"display: table-cell; white-space: nowrap; line-height: 1.5; max-width: 200px; text-align: center; vertical-align: middle; width: {Fmt(Width)}px; height: {Fmt(Height)}px;\">");
        sb.Append($"<span class=\"nodeLabel\">{WebUtility.HtmlEncode(HtmlContent)}</span>");
        sb.Append("</div>");
        sb.Append("</foreignObject>");
        return sb.ToString();
    }
}
