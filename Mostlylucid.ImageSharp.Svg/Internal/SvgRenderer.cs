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

        // If this element has a clip-path attribute, build the clip
        // geometry from the referenced clipPath def and wrap the actual
        // draw call in ClipPathExtensions.Clip(). Lets shields render
        // their rounded corners properly.
        var clipPathAttr = node.Get("clip-path") ?? Lookup(SvgValueParser.ParseStyle(node.Get("style")), "clip-path");
        if (!string.IsNullOrEmpty(clipPathAttr))
        {
            var clipGeom = TryBuildClipPathGeometry(clipPathAttr, transform);
            if (clipGeom != null)
            {
                ctx.Clip(clipGeom, inner => DrawElementInner(inner, node, transform, style));
                return;
            }
        }

        DrawElementInner(ctx, node, transform, style);
    }

    private void DrawElementInner(IImageProcessingContext ctx, SvgNode node, Matrix3x2 transform, InheritedStyle style)
    {
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

        // Render markers (arrowheads) at the path endpoints. Mermaid edges
        // rely heavily on these — without them every flowchart edge looks
        // like a bare line.
        var markerStartId = ResolveMarkerId(node, "marker-start") ?? ResolveMarkerId(node, "marker");
        var markerEndId   = ResolveMarkerId(node, "marker-end")   ?? ResolveMarkerId(node, "marker");
        if (markerStartId == null && markerEndId == null) return;

        var (startEp, endEp) = SvgPathEndpoints.Compute(d);
        if (markerStartId != null && startEp.HasValue)
            DrawMarker(ctx, markerStartId, startEp.Value, transform, style);
        if (markerEndId != null && endEp.HasValue)
            DrawMarker(ctx, markerEndId, endEp.Value, transform, style);
    }

    /// <summary>
    /// Resolve a marker reference from inline attribute, inline style, or
    /// CSS class. Returns just the id (without the <c>url(#…)</c> wrapper).
    /// </summary>
    private string? ResolveMarkerId(SvgNode node, string attribute)
    {
        // Inline attribute first.
        var raw = node.Get(attribute);
        if (string.IsNullOrEmpty(raw))
        {
            // Then inline style="" then CSS classes.
            var style = SvgValueParser.ParseStyle(node.Get("style"));
            style.TryGetValue(attribute, out raw);
            if (string.IsNullOrEmpty(raw))
            {
                var classAttr = node.Get("class");
                if (!string.IsNullOrEmpty(classAttr) && !_cssRules.IsEmpty)
                {
                    foreach (var token in classAttr.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var rules = _cssRules.GetClassProperties(token);
                        if (rules != null && rules.TryGetValue(attribute, out var v))
                        {
                            raw = v;
                            break;
                        }
                    }
                }
            }
        }
        if (string.IsNullOrEmpty(raw)) return null;
        if (!raw.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) return null;
        return ExtractUrlId(raw);
    }

    /// <summary>
    /// Render a marker definition at a given point + tangent angle. Builds
    /// the marker-local transform (translate to point, rotate by angle,
    /// scale by markerWidth/viewBox, translate by -refX/-refY) and walks
    /// the marker's child elements through the renderer's normal element
    /// dispatch with the new transform composed onto the parent.
    /// </summary>
    private void DrawMarker(
        IImageProcessingContext ctx,
        string markerId,
        SvgPathEndpoints.Endpoint endpoint,
        Matrix3x2 parentTransform,
        InheritedStyle parentStyle)
    {
        if (!_defs.TryGetValue(markerId, out var marker) || marker.Name != "marker") return;

        // Marker viewBox + size.
        var (vbX, vbY, vbW, vbH) = ParseViewBoxLocal(marker.Get("viewBox"));
        var markerW = SvgValueParser.ParseNumber(marker.Get("markerWidth"), 3);
        var markerH = SvgValueParser.ParseNumber(marker.Get("markerHeight"), 3);
        var refX    = SvgValueParser.ParseNumber(marker.Get("refX"), 0);
        var refY    = SvgValueParser.ParseNumber(marker.Get("refY"), 0);
        var orient  = marker.Get("orient");
        var unitsAttr = marker.Get("markerUnits") ?? "strokeWidth";

        // For markerUnits="strokeWidth" (the SVG default) the marker scales
        // with the path's stroke width. For "userSpaceOnUse" the marker
        // size is in user units directly. Mermaid uses userSpaceOnUse for
        // its arrow markers, so a constant size on screen.
        double unitScale = 1d;
        if (unitsAttr.Equals("strokeWidth", StringComparison.OrdinalIgnoreCase))
        {
            unitScale = parentStyle.StrokeWidth ?? 1d;
        }

        var sx = vbW.HasValue && vbW > 0 ? markerW / vbW.Value : 1d;
        var sy = vbH.HasValue && vbH > 0 ? markerH / vbH.Value : 1d;

        // Compose: parent transform → translate to endpoint → rotate by
        // tangent angle → scale by marker units → translate by -refX/-refY.
        var orientAngle = endpoint.Angle;
        if (!string.IsNullOrEmpty(orient) &&
            !orient.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
            !orient.Equals("auto-start-reverse", StringComparison.OrdinalIgnoreCase))
        {
            // Static degree value.
            if (double.TryParse(orient.Replace("deg", "", StringComparison.OrdinalIgnoreCase),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var deg))
            {
                orientAngle = (float)(deg * Math.PI / 180.0);
            }
        }

        var markerTransform =
            Matrix3x2.CreateTranslation((float)-refX, (float)-refY) *
            Matrix3x2.CreateScale((float)(sx * unitScale), (float)(sy * unitScale)) *
            Matrix3x2.CreateRotation(orientAngle) *
            Matrix3x2.CreateTranslation(endpoint.Point.X, endpoint.Point.Y) *
            parentTransform;

        // Markers establish their own viewport — children inherit a fresh
        // style context, but stroke colour falls through from the path.
        var markerStyle = parentStyle;
        DrawChildren(ctx, marker, markerTransform, markerStyle);
    }

    private static (double X, double Y, double? W, double? H) ParseViewBoxLocal(string? viewBox)
    {
        if (string.IsNullOrWhiteSpace(viewBox)) return (0, 0, null, null);
        var nums = SvgValueParser.ParseNumberList(viewBox);
        if (nums.Count < 4) return (0, 0, null, null);
        return (nums[0], nums[1], nums[2], nums[3]);
    }

    /// <summary>
    /// Resolve a <c>clip-path="url(#id)"</c> reference to an
    /// <see cref="IPath"/> the caller can pass to
    /// <see cref="ClipPathExtensions.Clip"/>. Walks the <c>&lt;clipPath&gt;</c>
    /// def's children, builds a path for each shape, and combines them
    /// (currently just the first shape — most real-world clipPaths only
    /// have one rect or path).
    /// </summary>
    private IPath? TryBuildClipPathGeometry(string clipPathAttr, Matrix3x2 transform)
    {
        if (!clipPathAttr.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) return null;
        var id = ExtractUrlId(clipPathAttr);
        if (id == null || !_defs.TryGetValue(id, out var clipNode) || clipNode.Name != "clipPath")
            return null;

        IPath? combined = null;
        foreach (var child in clipNode.Children)
        {
            var shape = BuildShapeFromElement(child);
            if (shape == null) continue;
            // Apply the clipPath child's own transform (rare).
            var childTransform = SvgValueParser.ParseTransform(child.Get("transform"));
            if (!childTransform.IsIdentity)
                shape = shape.Transform(childTransform);

            combined = combined == null ? shape : combined; // Phase 1: first shape only.
        }

        if (combined == null) return null;
        // Apply the element's own transform so the clip aligns with the
        // shape we're about to draw.
        return transform.IsIdentity ? combined : combined.Transform(transform);
    }

    /// <summary>
    /// Build an <see cref="IPath"/> from a single SVG shape element. Used
    /// by clipPath resolution; doesn't handle styling, just geometry.
    /// </summary>
    private static IPath? BuildShapeFromElement(SvgNode node)
    {
        switch (node.Name)
        {
            case "rect":
            {
                var x  = (float)SvgValueParser.ParseNumber(node.Get("x"));
                var y  = (float)SvgValueParser.ParseNumber(node.Get("y"));
                var w  = (float)SvgValueParser.ParseNumber(node.Get("width"));
                var h  = (float)SvgValueParser.ParseNumber(node.Get("height"));
                var rx = (float)SvgValueParser.ParseNumber(node.Get("rx"));
                var ry = (float)SvgValueParser.ParseNumber(node.Get("ry"));
                if (w <= 0 || h <= 0) return null;
                return (rx > 0 || ry > 0)
                    ? BuildRoundedRect(x, y, w, h, MathF.Max(rx, ry), MathF.Max(ry, rx))
                    : new RectangularPolygon(x, y, w, h);
            }
            case "circle":
            {
                var cx = (float)SvgValueParser.ParseNumber(node.Get("cx"));
                var cy = (float)SvgValueParser.ParseNumber(node.Get("cy"));
                var r  = (float)SvgValueParser.ParseNumber(node.Get("r"));
                return r > 0 ? new EllipsePolygon(cx, cy, r) : null;
            }
            case "ellipse":
            {
                var cx = (float)SvgValueParser.ParseNumber(node.Get("cx"));
                var cy = (float)SvgValueParser.ParseNumber(node.Get("cy"));
                var rx = (float)SvgValueParser.ParseNumber(node.Get("rx"));
                var ry = (float)SvgValueParser.ParseNumber(node.Get("ry"));
                return rx > 0 && ry > 0 ? new EllipsePolygon(cx, cy, rx, ry) : null;
            }
            case "polygon":
            case "polyline":
            {
                var pts = SvgValueParser.ParseNumberList(node.Get("points"));
                if (pts.Count < 4) return null;
                var coords = new PointF[pts.Count / 2];
                for (var i = 0; i + 1 < pts.Count; i += 2)
                    coords[i / 2] = new PointF((float)pts[i], (float)pts[i + 1]);
                return node.Name == "polygon"
                    ? new SixLabors.ImageSharp.Drawing.Polygon(coords)
                    : BuildOpenPath(coords);
            }
            case "path":
            {
                var d = node.Get("d");
                if (string.IsNullOrWhiteSpace(d)) return null;
                return TryParsePath(d, out var geom) ? geom : null;
            }
            default:
                return null;
        }
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
        var fillAlpha = style.Opacity * style.FillOpacity;
        var color = SvgValueParser.ParseColor(style.Fill, fillAlpha) ?? SvgValueParser.ApplyOpacity(Color.Black, fillAlpha);

        // Position is in user units; transform it to canvas pixels first, then
        // adjust for text-anchor.
        var origin = TransformPoint(transform, new Vector2(x, y));

        // Only measure when we actually need an alignment offset — the
        // common case (text-anchor="start" / unspecified) skips the
        // measurement entirely. MeasureSize allocates a layout buffer per
        // call so this is the dominant text-rendering hot path.
        var anchor = style.TextAnchor;
        if (string.Equals(anchor, "middle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(anchor, "end", StringComparison.OrdinalIgnoreCase))
        {
            var measured = TextMeasurer.MeasureSize(content, new TextOptions(font));
            if (string.Equals(anchor, "middle", StringComparison.OrdinalIgnoreCase))
                origin.X -= measured.Width / 2f;
            else
                origin.X -= measured.Width;
        }

        // SVG text Y is the baseline; ImageSharp draws from the top — pull
        // up by the font ascent (~0.8 em is a close approximation).
        origin.Y -= font.Size * 0.8f;

        // Convert text → glyph paths via TextBuilder, then fill the paths
        // as regular shapes. ImageSharp's `ctx.DrawText` runs an internal
        // text-rendering pipeline that allocates ~250 KB per call (glyph
        // layout, rasterizer state, scanline buffers). The path-based
        // route shares the same Fill machinery as our other shapes and is
        // dramatically lighter on allocations because it skips the
        // dedicated text rasterizer.
        try
        {
            var textOptions = new TextOptions(font)
            {
                Origin = new PointF(origin.X, origin.Y),
            };
            var glyphPaths = TextBuilder.GenerateGlyphs(content, textOptions);
            ctx.Fill(color, glyphPaths);
        }
        catch
        {
            // Fall back to the heavy path if glyph extraction fails for
            // any reason (font without outline data, exotic input, etc.).
            ctx.DrawText(content, font, color, new PointF(origin.X, origin.Y));
        }
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

        // Fast path: if both fill and stroke are solid colours (the common
        // case for almost everything), avoid the brush allocation entirely
        // and use ImageSharp's Color-typed overloads. This is the dominant
        // allocation in the render hot path — every Fill via a Brush
        // instance allocates internal fill state, but Fill via Color uses
        // a stack-allocated SolidBrush internally.
        var fillAlpha   = style.Opacity * style.FillOpacity;
        var strokeAlpha = style.Opacity * style.StrokeOpacity;

        // Resolve fill: only build a Brush if the paint is a paint-server
        // (gradient / pattern). Solid colours collapse to a Color value
        // and the dedicated overload below.
        Color? solidFill;
        Brush? complexFill = null;
        if (TryResolveSolidPaint(style.Fill, defaultFill ? Color.Black : (Color?)null, fillAlpha, out solidFill))
        {
            // Solid path — no brush allocation needed.
        }
        else
        {
            complexFill = ResolvePaintToBrush(style.Fill, null, fillAlpha, path.Bounds);
        }

        if (solidFill.HasValue)
            ctx.Fill(solidFill.Value, path);
        else if (complexFill != null)
            ctx.Fill(complexFill, path);

        Color? solidStroke;
        Brush? complexStroke = null;
        if (TryResolveSolidPaint(style.Stroke, null, strokeAlpha, out solidStroke))
        {
            // Solid path
        }
        else
        {
            complexStroke = ResolvePaintToBrush(style.Stroke, null, strokeAlpha, path.Bounds);
        }

        var thickness = (float)Math.Max(0.1, (style.StrokeWidth ?? 1d) * GetApproxScale(transform));
        if (solidStroke.HasValue)
            ctx.Draw(solidStroke.Value, thickness, path);
        else if (complexStroke != null)
            ctx.Draw(complexStroke, thickness, path);
    }

    /// <summary>
    /// Try to resolve an SVG paint value to a single solid <see cref="Color"/>
    /// without allocating a <see cref="Brush"/> wrapper. Returns true and
    /// sets <paramref name="color"/> on the solid path; returns false when
    /// the paint references a gradient/pattern that needs a real Brush.
    /// "none" returns true with <c>color = null</c> so the caller can skip
    /// the draw call entirely.
    /// </summary>
    private static bool TryResolveSolidPaint(string? value, Color? fallback, double opacity, out Color? color)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            color = fallback.HasValue ? SvgValueParser.ApplyOpacity(fallback.Value, opacity) : null;
            return true;
        }

        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            color = null;
            return true;
        }

        // url(#…) — defer to the brush builder.
        if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            color = null;
            return false;
        }

        var parsed = SvgValueParser.ParseColor(value, opacity);
        if (parsed.HasValue)
        {
            color = parsed;
            return true;
        }

        // Unparseable → fall back.
        color = fallback.HasValue ? SvgValueParser.ApplyOpacity(fallback.Value, opacity) : null;
        return true;
    }

    /// <summary>
    /// Resolve an SVG paint value (named/hex/rgb/url(#id)) to an ImageSharp
    /// brush. Solid colors collapse to <see cref="SolidBrush"/>; gradient
    /// references build a real <see cref="LinearGradientBrush"/> /
    /// <see cref="RadialGradientBrush"/> using the shape's bounding box for
    /// objectBoundingBox-units coordinates.
    /// </summary>
    private Brush? ResolvePaintToBrush(string? value, Brush? fallback, double opacity, RectangleF shapeBounds)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ApplyOpacityToBrush(fallback, opacity);

        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var id = ExtractUrlId(value);
            if (id != null && _defs.TryGetValue(id, out var defNode))
            {
                if (defNode.Name == "linearGradient")
                    return BuildLinearGradientBrush(defNode, shapeBounds, opacity) ?? ApplyOpacityToBrush(fallback, opacity);
                if (defNode.Name == "radialGradient")
                    return BuildRadialGradientBrush(defNode, shapeBounds, opacity) ?? ApplyOpacityToBrush(fallback, opacity);
            }
            return ApplyOpacityToBrush(fallback, opacity);
        }

        var parsed = SvgValueParser.ParseColor(value, opacity);
        if (parsed.HasValue) return new SolidBrush(parsed.Value);
        return ApplyOpacityToBrush(fallback, opacity);
    }

    private static Brush? ApplyOpacityToBrush(Brush? brush, double opacity)
    {
        if (brush == null || opacity >= 1d) return brush;
        // Solid brush is the only case we can dim cleanly without rebuilding
        // the gradient stops. Gradient brushes carry their opacity baked in
        // (we apply it to each stop's color in BuildLinearGradientBrush).
        if (brush is SolidBrush solid)
            return new SolidBrush(SvgValueParser.ApplyOpacity(solid.Color, opacity));
        return brush;
    }

    private LinearGradientBrush? BuildLinearGradientBrush(SvgNode gradient, RectangleF bounds, double opacity)
    {
        var stops = CollectGradientStops(gradient, opacity);
        if (stops.Length < 2) return null;

        // Resolve x1/y1/x2/y2 with the SVG defaults: a horizontal vector
        // from (0%, 0%) to (100%, 0%) over the gradient's coordinate space.
        var x1 = ParseGradientCoord(gradient, "x1", 0);
        var y1 = ParseGradientCoord(gradient, "y1", 0);
        var x2 = ParseGradientCoord(gradient, "x2", 1);
        var y2 = ParseGradientCoord(gradient, "y2", 0);

        var units = ResolveGradientUnits(gradient);
        PointF start, end;
        if (units == "userSpaceOnUse")
        {
            start = new PointF((float)x1, (float)y1);
            end   = new PointF((float)x2, (float)y2);
        }
        else
        {
            // objectBoundingBox: 0..1 maps onto the shape bounds.
            start = new PointF(bounds.X + (float)x1 * bounds.Width, bounds.Y + (float)y1 * bounds.Height);
            end   = new PointF(bounds.X + (float)x2 * bounds.Width, bounds.Y + (float)y2 * bounds.Height);
        }

        return new LinearGradientBrush(start, end, GradientRepetitionMode.None, stops);
    }

    private RadialGradientBrush? BuildRadialGradientBrush(SvgNode gradient, RectangleF bounds, double opacity)
    {
        var stops = CollectGradientStops(gradient, opacity);
        if (stops.Length < 2) return null;

        var cx = ParseGradientCoord(gradient, "cx", 0.5);
        var cy = ParseGradientCoord(gradient, "cy", 0.5);
        var r  = ParseGradientCoord(gradient, "r",  0.5);

        var units = ResolveGradientUnits(gradient);
        PointF center;
        float radius;
        if (units == "userSpaceOnUse")
        {
            center = new PointF((float)cx, (float)cy);
            radius = (float)r;
        }
        else
        {
            center = new PointF(bounds.X + (float)cx * bounds.Width, bounds.Y + (float)cy * bounds.Height);
            radius = (float)r * Math.Max(bounds.Width, bounds.Height);
        }

        return new RadialGradientBrush(center, radius, GradientRepetitionMode.None, stops);
    }

    private string ResolveGradientUnits(SvgNode gradient)
    {
        // gradientUnits is inheritable through href chains.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = gradient;
        while (current != null)
        {
            var u = current.Get("gradientUnits");
            if (!string.IsNullOrEmpty(u)) return u;
            var hrefValue = current.Get("href") ?? current.Get("xlink:href");
            if (string.IsNullOrEmpty(hrefValue) || hrefValue[0] != '#') break;
            var refId = hrefValue[1..];
            if (!visited.Add(refId)) break;
            current = _defs.TryGetValue(refId, out var next) ? next : null;
        }
        return "objectBoundingBox";
    }

    private static double ParseGradientCoord(SvgNode gradient, string attr, double defaultValue)
    {
        var raw = gradient.Get(attr);
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        var s = raw.Trim();
        if (s.EndsWith('%') && double.TryParse(s[..^1],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var pct))
        {
            return pct / 100d;
        }
        return SvgValueParser.ParseNumber(s, defaultValue);
    }

    /// <summary>
    /// Walk the gradient + its href chain to gather <c>&lt;stop&gt;</c>
    /// children. Each stop becomes an ImageSharp <see cref="ColorStop"/>
    /// with the color premultiplied by the parent opacity.
    /// </summary>
    private ColorStop[] CollectGradientStops(SvgNode gradient, double opacity)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stops = new List<ColorStop>();
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
                // Default stop-color is black per SVG spec.
                var color = SvgValueParser.ParseColor(stopColorStr ?? "#000", stopOpacity * opacity)
                            ?? SvgValueParser.ApplyOpacity(Color.Black, stopOpacity * opacity);
                var offset = (float)Math.Clamp(
                    ParseGradientCoord(child, "offset", 0), 0d, 1d);
                stops.Add(new ColorStop(offset, color));
            }

            if (stops.Count > 0) break;

            var hrefValue = current.Get("href") ?? current.Get("xlink:href");
            if (string.IsNullOrEmpty(hrefValue) || hrefValue[0] != '#') break;
            var refId = hrefValue[1..];
            if (!visited.Add(refId)) break;
            current = _defs.TryGetValue(refId, out var next) ? next : null;
        }
        // ImageSharp requires stops in offset-ascending order.
        stops.Sort((a, b) => a.Ratio.CompareTo(b.Ratio));
        return stops.ToArray();
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

        // When ForceBundledFont is true (the default), use the embedded
        // DejaVu Sans for ALL text. This trades a small fidelity loss for
        // SVGs that genuinely depend on a specific host font, in exchange
        // for byte-identical rendering across Windows/macOS/Linux. Most
        // markdown content (shields, mermaid, icons) renders perceptually
        // identical or better with DejaVu than with whatever the host has.
        if (_options.ForceBundledFont && BundledFonts.Fallback is { } bundledFirst)
            return bundledFirst.CreateFont(size, style);

        // ForceBundledFont = false: walk the cascade.
        //   1. Each requested family looked up in system fonts.
        //   2. The bundled DejaVu Sans family.
        //   3. The configured fallback family (system).
        //   4. The first installed system font.
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

        if (BundledFonts.Fallback is { } bundled)
            return bundled.CreateFont(size, style);

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
