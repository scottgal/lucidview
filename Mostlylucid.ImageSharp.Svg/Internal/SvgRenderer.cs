using System.Collections.Generic;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using DrawingPath = SixLabors.ImageSharp.Drawing.Path;

namespace Mostlylucid.ImageSharp.Svg.Internal;

/// <summary>
/// Walks an <see cref="SvgNode"/> tree and emits ImageSharp draw calls. The
/// renderer covers the SVG element subset that shields.io and Naiad/mermaid
/// emit (rect, circle, ellipse, line, path, polygon, polyline, text, group)
/// with proper presentation-attribute inheritance.
/// </summary>
internal sealed class SvgRenderer
{
    private readonly Dictionary<string, SvgNode> _defs = new(StringComparer.Ordinal);
    private readonly SvgCssRules _cssRules = new();
    private readonly SvgRenderOptions _options;

    public SvgRenderer(SvgRenderOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Render an already-parsed SVG tree to a new <see cref="Image{Rgba32}"/>.
    /// Caller owns the returned image and must dispose it.
    /// </summary>
    public Image<Rgba32> Render(SvgNode root, out int naturalWidth, out int naturalHeight)
    {
        if (root.Name != "svg")
            throw new InvalidDataException($"Expected <svg> root, got <{root.Name}>.");

        // Resolve canvas dimensions: prefer width/height attributes, fall back
        // to viewBox dimensions, fall back to a sane default so we always have
        // something rasterizable.
        var (vbX, vbY, vbW, vbH) = ParseViewBox(root.Get("viewBox"));
        var widthAttr  = SvgValueParser.ParseNullableNumber(root.Get("width"));
        var heightAttr = SvgValueParser.ParseNullableNumber(root.Get("height"));

        var natW = widthAttr  ?? vbW ?? 300;
        var natH = heightAttr ?? vbH ?? 150;
        naturalWidth  = (int)Math.Max(1, Math.Round(natW));
        naturalHeight = (int)Math.Max(1, Math.Round(natH));

        var scale = Math.Max(0.1f, _options.Scale);
        var pxW = Math.Max(1, (int)Math.Ceiling(natW * scale));
        var pxH = Math.Max(1, (int)Math.Ceiling(natH * scale));

        // Build the root transform: viewBox→canvas mapping then output scale.
        var rootTransform = Matrix3x2.CreateScale(scale);
        if (vbW.HasValue && vbH.HasValue && vbW > 0 && vbH > 0)
        {
            var sx = (float)(natW / vbW.Value);
            var sy = (float)(natH / vbH.Value);
            rootTransform = Matrix3x2.Multiply(
                Matrix3x2.Multiply(
                    Matrix3x2.CreateTranslation((float)-vbX, (float)-vbY),
                    Matrix3x2.CreateScale(sx, sy)),
                rootTransform);
        }

        IndexDefs(root);

        // Use the caller-supplied Configuration when present so AOT consumers
        // can pass a minimal (PNG-only) one and let the trimmer drop the
        // unused format modules. Falls back to ImageSharp's default which
        // registers every format ImageSharp ships with.
        var image = _options.Configuration is { } cfg
            ? new Image<Rgba32>(cfg, pxW, pxH)
            : new Image<Rgba32>(pxW, pxH);
        var background = _options.Background ?? Color.Transparent;
        var rootStyle = InheritedStyle.Default;
        image.Mutate(ctx =>
        {
            ctx.Clear(background);
            DrawChildren(ctx, root, rootTransform, rootStyle);
        });
        return image;
    }

    private static (double X, double Y, double? W, double? H) ParseViewBox(string? viewBox)
    {
        if (string.IsNullOrWhiteSpace(viewBox)) return (0, 0, null, null);
        var nums = SvgValueParser.ParseNumberList(viewBox);
        if (nums.Count < 4) return (0, 0, null, null);
        return (nums[0], nums[1], nums[2], nums[3]);
    }

    private void IndexDefs(SvgNode node)
    {
        var id = node.Get("id");
        if (!string.IsNullOrEmpty(id))
            _defs[id] = node;

        // <style> blocks contain CSS rules that need to participate in the
        // attribute cascade. Parse and merge them into the rules table.
        if (node.Name == "style" && !string.IsNullOrEmpty(node.Text))
            _cssRules.Parse(node.Text);

        foreach (var child in node.Children)
            IndexDefs(child);
    }

    private void DrawChildren(IImageProcessingContext ctx, SvgNode parent, Matrix3x2 transform, InheritedStyle style)
    {
        foreach (var child in parent.Children)
            DrawElement(ctx, child, transform, style);
    }

    private void DrawElement(IImageProcessingContext ctx, SvgNode node, Matrix3x2 parentTransform, InheritedStyle parentStyle)
    {
        var transform = Matrix3x2.Multiply(
            SvgValueParser.ParseTransform(node.Get("transform")),
            parentTransform);
        var style = parentStyle.Merge(node, _cssRules);

        switch (node.Name)
        {
            case "g":
            case "svg":
                DrawChildren(ctx, node, transform, style);
                break;

            case "rect":     DrawRect(ctx, node, transform, style); break;
            case "circle":   DrawCircle(ctx, node, transform, style); break;
            case "ellipse":  DrawEllipse(ctx, node, transform, style); break;
            case "line":     DrawLine(ctx, node, transform, style); break;
            case "polygon":  DrawPolygon(ctx, node, transform, style, closed: true); break;
            case "polyline": DrawPolygon(ctx, node, transform, style, closed: false); break;
            case "path":     DrawPath(ctx, node, transform, style); break;
            case "text":     DrawText(ctx, node, transform, style); break;

            // Defs-only or metadata nodes — no direct rendering. Picked up
            // via id lookup if referenced elsewhere.
            case "defs":
            case "linearGradient":
            case "radialGradient":
            case "clipPath":
            case "marker":
            case "stop":
            case "title":
            case "desc":
            case "metadata":
            case "style":
                break;

            default:
                // Unknown element: walk children so nested content isn't lost.
                DrawChildren(ctx, node, transform, style);
                break;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Shape draw handlers
    // ────────────────────────────────────────────────────────────────────

    private void DrawRect(IImageProcessingContext ctx, SvgNode node, Matrix3x2 transform, InheritedStyle style)
    {
        var x  = (float)SvgValueParser.ParseNumber(node.Get("x"));
        var y  = (float)SvgValueParser.ParseNumber(node.Get("y"));
        var w  = (float)SvgValueParser.ParseNumber(node.Get("width"));
        var h  = (float)SvgValueParser.ParseNumber(node.Get("height"));
        var rx = (float)SvgValueParser.ParseNumber(node.Get("rx"));
        var ry = (float)SvgValueParser.ParseNumber(node.Get("ry"));
        if (w <= 0 || h <= 0) return;

        IPath shape = (rx > 0 || ry > 0)
            ? BuildRoundedRect(x, y, w, h, MathF.Max(rx, ry), MathF.Max(ry, rx))
            : new RectangularPolygon(x, y, w, h);

        FillAndStroke(ctx, shape, transform, style, defaultFill: true);
    }

    private void DrawCircle(IImageProcessingContext ctx, SvgNode node, Matrix3x2 transform, InheritedStyle style)
    {
        var cx = (float)SvgValueParser.ParseNumber(node.Get("cx"));
        var cy = (float)SvgValueParser.ParseNumber(node.Get("cy"));
        var r  = (float)SvgValueParser.ParseNumber(node.Get("r"));
        if (r <= 0) return;
        FillAndStroke(ctx, new EllipsePolygon(cx, cy, r), transform, style, defaultFill: true);
    }

    private void DrawEllipse(IImageProcessingContext ctx, SvgNode node, Matrix3x2 transform, InheritedStyle style)
    {
        var cx = (float)SvgValueParser.ParseNumber(node.Get("cx"));
        var cy = (float)SvgValueParser.ParseNumber(node.Get("cy"));
        var rx = (float)SvgValueParser.ParseNumber(node.Get("rx"));
        var ry = (float)SvgValueParser.ParseNumber(node.Get("ry"));
        if (rx <= 0 || ry <= 0) return;
        FillAndStroke(ctx, new EllipsePolygon(cx, cy, rx, ry), transform, style, defaultFill: true);
    }

    private void DrawLine(IImageProcessingContext ctx, SvgNode node, Matrix3x2 transform, InheritedStyle style)
    {
        var x1 = (float)SvgValueParser.ParseNumber(node.Get("x1"));
        var y1 = (float)SvgValueParser.ParseNumber(node.Get("y1"));
        var x2 = (float)SvgValueParser.ParseNumber(node.Get("x2"));
        var y2 = (float)SvgValueParser.ParseNumber(node.Get("y2"));
        var pb = new PathBuilder();
        pb.AddLine(new PointF(x1, y1), new PointF(x2, y2));
        FillAndStroke(ctx, pb.Build(), transform, style, defaultFill: false);
    }

    private void DrawPolygon(IImageProcessingContext ctx, SvgNode node, Matrix3x2 transform, InheritedStyle style, bool closed)
    {
        var points = SvgValueParser.ParseNumberList(node.Get("points"));
        if (points.Count < 4) return;

        var coords = new PointF[points.Count / 2];
        for (var i = 0; i + 1 < points.Count; i += 2)
            coords[i / 2] = new PointF((float)points[i], (float)points[i + 1]);

        IPath shape = closed
            ? new SixLabors.ImageSharp.Drawing.Polygon(coords)
            : BuildOpenPath(coords);

        FillAndStroke(ctx, shape, transform, style, defaultFill: closed);
    }

    private static IPath BuildOpenPath(PointF[] coords)
    {
        var pb = new PathBuilder();
        pb.AddLines(coords);
        return pb.Build();
    }

    private void DrawPath(IImageProcessingContext ctx, SvgNode node, Matrix3x2 transform, InheritedStyle style)
    {
        var d = node.Get("d");
        if (string.IsNullOrWhiteSpace(d)) return;
        if (!TryParsePath(d, out var geometry)) return;
        FillAndStroke(ctx, geometry, transform, style, defaultFill: false);
    }

    private static bool TryParsePath(string value, out IPath geometry)
    {
        geometry = default!;
        // SixLabors.ImageSharp.Drawing's parser is whitespace-strict — it
        // chokes on the compact `M0,-185A185,...` form that mermaid emits.
        // Normalize first (insert spaces around commands and between adjacent
        // numbers) so the parser can tokenize correctly.
        var normalized = NormalizePathData(value);
        try
        {
            if (DrawingPath.TryParseSvgPath(normalized, out var parsed) && parsed != null)
            {
                geometry = parsed;
                return true;
            }
        }
        catch
        {
            // Fall through.
        }
        return false;
    }

    /// <summary>
    /// Insert whitespace around path command letters and between adjacent
    /// numbers so the SixLabors path tokenizer can read each value
    /// independently. Conservative — only adds spaces, never removes content.
    /// </summary>
    private static string NormalizePathData(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 32);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            // SVG path commands.
            if ("MmLlHhVvCcSsQqTtAaZz".IndexOf(c) >= 0)
            {
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                sb.Append(c);
                sb.Append(' ');
                continue;
            }

            // A '-' or '+' that follows a digit, '.', or 'e' / 'E' starts a
            // new number — separate it from the previous one.
            if ((c == '-' || c == '+') && sb.Length > 0)
            {
                var prev = sb[^1];
                if (char.IsDigit(prev) || prev == '.')
                {
                    sb.Append(' ');
                }
                else if (prev is 'e' or 'E')
                {
                    // Exponent sign — keep glued to the previous char.
                }
            }

            // A '.' that follows a digit AFTER an existing decimal point
            // (e.g. ".5.5") starts a second number — split.
            if (c == '.' && sb.Length > 0)
            {
                // Walk back through digits to find a previous '.'.
                var j = sb.Length - 1;
                var sawDigitOnly = true;
                while (j >= 0)
                {
                    if (sb[j] == '.') { if (sawDigitOnly) sb.Append(' '); break; }
                    if (!char.IsDigit(sb[j])) { sawDigitOnly = false; break; }
                    j--;
                }
            }

            // Commas become spaces.
            if (c == ',')
            {
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                continue;
            }

            sb.Append(c);
        }
        return sb.ToString();
    }

    // ────────────────────────────────────────────────────────────────────
    // Text
    // ────────────────────────────────────────────────────────────────────

    private void DrawText(IImageProcessingContext ctx, SvgNode node, Matrix3x2 transform, InheritedStyle style)
    {
        var content = ExtractTextContent(node);
        if (string.IsNullOrWhiteSpace(content)) return;

        var x = (float)SvgValueParser.ParseNumber(node.Get("x"));
        var y = (float)SvgValueParser.ParseNumber(node.Get("y"));

        // Effective font size folds the parent transform's scale factor in,
        // so shields' <g font-size="110"><text transform="scale(.1)">…</text></g>
        // pattern collapses to a sensible 11-pt rendering instead of 110-pt.
        var inputSize = (float)Math.Max(1, style.FontSize ?? 12);
        var transformScale = GetApproxScale(transform);
        var renderSize = inputSize * transformScale;

        var font = ResolveFont(style.FontFamily, renderSize, style.FontWeight);

        // Resolve text colour with full inheritance + opacity merge.
        var color = ResolvePaintToColor(style.Fill, Color.Black, style.Opacity) ?? Color.Black;

        // Position is in user units; transform it to canvas pixels first, then
        // adjust for text-anchor.
        var origin = TransformPoint(transform, new Vector2(x, y));

        var measureOptions = new RichTextOptions(font);
        var measured = TextMeasurer.MeasureSize(content, measureOptions);

        if (string.Equals(style.TextAnchor, "middle", StringComparison.OrdinalIgnoreCase))
            origin.X -= measured.Width / 2f;
        else if (string.Equals(style.TextAnchor, "end", StringComparison.OrdinalIgnoreCase))
            origin.X -= measured.Width;

        // SVG text Y is the baseline; ImageSharp draws from the top — pull
        // up by the font ascent (~0.8 em is a close approximation).
        origin.Y -= font.Size * 0.8f;

        var drawOptions = new RichTextOptions(font) { Origin = new PointF(origin.X, origin.Y) };
        ctx.DrawText(drawOptions, content, color);
    }

    private static string ExtractTextContent(SvgNode node)
    {
        if (node.Children.Count == 0) return node.Text ?? string.Empty;

        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(node.Text)) sb.Append(node.Text);
        foreach (var c in node.Children)
            sb.Append(ExtractTextContent(c));
        return sb.ToString();
    }

    // ────────────────────────────────────────────────────────────────────
    // Paint resolution + fill/stroke
    // ────────────────────────────────────────────────────────────────────

    private void FillAndStroke(
        IImageProcessingContext ctx,
        IPath shape,
        Matrix3x2 transform,
        InheritedStyle style,
        bool defaultFill)
    {
        var path = transform.IsIdentity ? shape : shape.Transform(transform);

        var fillColor = ResolvePaintToColor(style.Fill, defaultFill ? Color.Black : (Color?)null, style.Opacity);
        if (fillColor.HasValue)
            ctx.Fill(fillColor.Value, path);

        var strokeColor = ResolvePaintToColor(style.Stroke, null, style.Opacity);
        if (strokeColor.HasValue)
        {
            var thickness = (float)Math.Max(0.1, (style.StrokeWidth ?? 1d) * GetApproxScale(transform));
            ctx.Draw(strokeColor.Value, thickness, path);
        }
    }

    private Color? ResolvePaintToColor(string? value, Color? fallback, double opacity)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback.HasValue ? SvgValueParser.ApplyOpacity(fallback.Value, opacity) : null;

        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        // url(#…) — phase 1 collapses gradients to their middle stop colour
        // so shields render with a flat fill that approximates the gradient
        // midpoint. A real linear gradient brush lands in phase 3.
        if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var id = ExtractUrlId(value);
            if (id != null && _defs.TryGetValue(id, out var defNode))
            {
                if (defNode.Name == "linearGradient" || defNode.Name == "radialGradient")
                {
                    var stops = CollectGradientStops(defNode);
                    if (stops.Count > 0)
                    {
                        var mid = stops[stops.Count / 2];
                        return SvgValueParser.ApplyOpacity(mid, opacity);
                    }
                }
            }
            return fallback.HasValue ? SvgValueParser.ApplyOpacity(fallback.Value, opacity) : null;
        }

        var parsed = SvgValueParser.ParseColor(value, opacity);
        return parsed ?? (fallback.HasValue ? SvgValueParser.ApplyOpacity(fallback.Value, opacity) : null);
    }

    private List<Color> CollectGradientStops(SvgNode gradient)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stops = new List<Color>();
        var current = gradient;
        while (current != null)
        {
            foreach (var child in current.Children)
            {
                if (child.Name != "stop") continue;
                var style = SvgValueParser.ParseStyle(child.Get("style"));
                var stopColorStr = child.Get("stop-color") ?? Lookup(style, "stop-color");
                var stopOpacity = SvgValueParser.ParseNumber(
                    child.Get("stop-opacity") ?? Lookup(style, "stop-opacity"), 1d);
                var color = SvgValueParser.ParseColor(stopColorStr, stopOpacity);
                if (color.HasValue) stops.Add(color.Value);
            }

            if (stops.Count > 0) break;

            var hrefValue = current.Get("href") ?? current.Get("xlink:href");
            if (string.IsNullOrEmpty(hrefValue) || hrefValue[0] != '#') break;
            var refId = hrefValue[1..];
            if (!visited.Add(refId)) break;
            current = _defs.TryGetValue(refId, out var next) ? next : null;
        }
        return stops;
    }

    private static string? ExtractUrlId(string value)
    {
        var open = value.IndexOf('(');
        var close = value.LastIndexOf(')');
        if (open < 0 || close < 0 || close <= open) return null;
        var inner = value.Substring(open + 1, close - open - 1).Trim().Trim('"', '\'');
        return inner.StartsWith('#') ? inner[1..] : null;
    }

    // ────────────────────────────────────────────────────────────────────
    // Fonts
    // ────────────────────────────────────────────────────────────────────

    private Font ResolveFont(string? fontFamily, float size, string? fontWeight)
    {
        var style = (fontWeight is "bold" or "700" or "800" or "900")
            ? FontStyle.Bold
            : FontStyle.Regular;

        if (!string.IsNullOrEmpty(fontFamily))
        {
            foreach (var part in fontFamily.Split(','))
            {
                var candidate = part.Trim().Trim('\'', '"');
                if (string.IsNullOrEmpty(candidate)) continue;
                if (SystemFonts.TryGet(candidate, out var family))
                    return family.CreateFont(size, style);
            }
        }

        if (SystemFonts.TryGet(_options.FallbackFontFamily, out var fallback))
            return fallback.CreateFont(size, style);

        var first = SystemFonts.Families.FirstOrDefault();
        if (first.Name != null)
            return first.CreateFont(size, style);

        throw new InvalidOperationException("No fonts available on this system.");
    }

    private static string? Lookup(IReadOnlyDictionary<string, string> style, string key)
        => style.TryGetValue(key, out var v) ? v : null;

    // ────────────────────────────────────────────────────────────────────
    // Geometry helpers
    // ────────────────────────────────────────────────────────────────────

    private static IPath BuildRoundedRect(float x, float y, float w, float h, float rx, float ry)
    {
        rx = MathF.Min(rx, w / 2f);
        ry = MathF.Min(ry, h / 2f);
        var pb = new PathBuilder();
        pb.StartFigure();
        pb.AddLine(new PointF(x + rx, y), new PointF(x + w - rx, y));
        pb.AddArc(new PointF(x + w - rx, y + ry), rx, ry, 0, 0, 90);
        pb.AddLine(new PointF(x + w, y + ry), new PointF(x + w, y + h - ry));
        pb.AddArc(new PointF(x + w - rx, y + h - ry), rx, ry, 0, 0, 90);
        pb.AddLine(new PointF(x + w - rx, y + h), new PointF(x + rx, y + h));
        pb.AddArc(new PointF(x + rx, y + h - ry), rx, ry, 0, 0, 90);
        pb.AddLine(new PointF(x, y + h - ry), new PointF(x, y + ry));
        pb.AddArc(new PointF(x + rx, y + ry), rx, ry, 0, 0, 90);
        pb.CloseFigure();
        return pb.Build();
    }

    private static PointF TransformPoint(Matrix3x2 m, Vector2 p)
    {
        var t = Vector2.Transform(p, m);
        return new PointF(t.X, t.Y);
    }

    private static float GetApproxScale(Matrix3x2 m)
    {
        var sx = Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12);
        var sy = Math.Sqrt(m.M21 * m.M21 + m.M22 * m.M22);
        return (float)Math.Max(0.1, (sx + sy) / 2.0);
    }
}
