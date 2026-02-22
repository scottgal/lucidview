using System.Text.RegularExpressions;
using MermaidSharp.Layout;
using static MermaidSharp.Rendering.RenderUtils;
using ModelRect = MermaidSharp.Models.Rect;

namespace MermaidSharp.Diagrams.Flowchart;

public class FlowchartRenderer(ILayoutEngine? layoutEngine = null) :
    IDiagramRenderer<FlowchartModel>
{
    readonly ILayoutEngine _layoutEngine = layoutEngine ?? new Layout.DagreNetLayoutEngine();

    // FontAwesome icon pattern: fa:fa-icon-name or fab:fa-icon-name
    static readonly Regex IconPattern = new("(fa[bsr]?):fa-([a-z0-9-]+)", RegexCompat.Compiled);
    /// <summary>
    /// Run measurement, layout, edge routing, and skin resolution without generating SVG.
    /// Returns positioned model objects suitable for any rendering backend.
    /// </summary>
    public FlowchartLayoutResult LayoutModel(FlowchartModel model, RenderOptions options)
    {
        SecurityValidator.ValidateComplexity(model.Nodes.Count, model.Edges.Count, options);

        // Apply skin shape map from %% naiad: shapes directive
        if (options.SkinShapeMap is { Count: > 0 })
        {
            foreach (var node in model.Nodes)
            {
                if (options.SkinShapeMap.TryGetValue(node.Id, out var skinShape))
                    node.SkinShapeName = skinShape;
            }
        }

        // Calculate node sizes based on text
        foreach (var node in model.Nodes)
        {
            // Use DisplayLabel which processes <br/> to \n and strips HTML tags
            var label = node.DisplayLabel;
            // Strip icon syntax for measurement
            var textForMeasure = IconPattern.Replace(label, "").Trim();

            // Mermaid wraps long text at ~200px max-width using foreignObject.
            // Simulate wrapping: if single-line text exceeds max width, split into lines.
            const double maxTextWidth = 200;
            var singleLineWidth = RenderUtils.MeasureTextWidth(textForMeasure, options.FontSize);
            if (singleLineWidth > maxTextWidth && !textForMeasure.Contains('\n'))
            {
                textForMeasure = WrapText(textForMeasure, maxTextWidth, options.FontSize);
                node.WrappedLabel = textForMeasure;
            }

            var textSize = MeasureText(textForMeasure, options.FontSize);
            // Add extra width for icon if present
            var hasIcon = IconPattern.IsMatch(label);

            // Mermaid.js uses two height classes:
            //   - Rectangle/RoundedRectangle: 54px (text + 38px vertical padding)
            //   - Compact shapes (stadium, hexagon, etc.): 39px (text + 23px padding)
            // Width: text + horizontal padding, with a minimum width.
            var isCompactShape = node.Shape is NodeShape.Stadium or NodeShape.Subroutine
                or NodeShape.Hexagon or NodeShape.Asymmetric
                or NodeShape.Parallelogram or NodeShape.ParallelogramAlt
                or NodeShape.Trapezoid or NodeShape.TrapezoidAlt
                or NodeShape.Lean or NodeShape.LeanAlt;

            if (node.Shape is NodeShape.Circle or NodeShape.DoubleCircle)
            {
                // Circle: mermaid sizes tightly. "Circle" (6 chars) → 57x57.
                // Minimal padding around text width.
                var diameter = Math.Max(textSize.Width + 2, textSize.Height + 8);
                node.Width = diameter;
                node.Height = diameter;
            }
            else if (node.Shape == NodeShape.Diamond)
            {
                // Diamond: mermaid uses textWidth + ~36px padding (not sqrt(2) scaling).
                // "Decision" (8 chars) → 113x113, "Let me think" (12 chars) → 145x145.
                var diamondSize = Math.Max(textSize.Width + 36, textSize.Height + 36);
                node.Width = diamondSize;
                node.Height = diamondSize;
            }
            else if (node.Shape == NodeShape.Cylinder)
            {
                // Database/Cylinder: mermaid uses taller shape (68px) with tight width (~80px for "Database")
                node.Width = Math.Max(textSize.Width + 8, 64) + (hasIcon ? 20 : 0);
                node.Height = textSize.Height + 44;
            }
            else if (isCompactShape)
            {
                // Compact shapes: 39px height, tighter horizontal padding
                node.Width = Math.Max(textSize.Width + 18, 60) + (hasIcon ? 20 : 0);
                node.Height = textSize.Height + 16;
                // Hexagon needs extra width for angled sides
                if (node.Shape == NodeShape.Hexagon)
                    node.Width += 30;
                // Parallelogram/lean shapes need extra width for slant
                if (node.Shape is NodeShape.Parallelogram or NodeShape.ParallelogramAlt
                    or NodeShape.Lean or NodeShape.LeanAlt)
                    node.Width += 16;
            }
            else
            {
                // Rectangle and RoundedRectangle: 54px height, generous padding
                node.Width = Math.Max(textSize.Width + 36, 80) + (hasIcon ? 20 : 0);
                node.Height = textSize.Height + 30;
            }

            // B1: Apply minimum node size from directives
            if (options.MinNodeWidth > 0 && node.Width < options.MinNodeWidth)
                node.Width = options.MinNodeWidth;
            if (options.MinNodeHeight > 0 && node.Height < options.MinNodeHeight)
                node.Height = options.MinNodeHeight;

            // B1: Apply per-node size overrides
            if (options.NodeSizeMap is not null &&
                options.NodeSizeMap.TryGetValue(node.Id, out var overrideSize))
            {
                node.Width = overrideSize.W;
                node.Height = overrideSize.H;
            }
        }

        // B4: Equalize all node sizes to the maximum measured dimensions
        if (options.EqualizeNodeSizes && model.Nodes.Count > 1)
        {
            var maxW = 0.0;
            var maxH = 0.0;
            foreach (var node in model.Nodes)
            {
                if (node.Width > maxW) maxW = node.Width;
                if (node.Height > maxH) maxH = node.Height;
            }
            foreach (var node in model.Nodes)
            {
                node.Width = maxW;
                node.Height = maxH;
            }
        }

        // Match mermaid.js default spacing: nodeSpacing=50, rankSpacing=50
        var layoutOptions = new LayoutOptions
        {
            Direction = model.Direction,
            NodeSeparation = options.NodeSeparation ?? 50.0,
            RankSeparation = options.RankSeparation ?? 50.0,
            EdgeSeparation = options.EdgeSeparation ?? 10.0
        };
        // Pass font size to layout engine so it can measure edge labels
        if (_layoutEngine is DagreNetLayoutEngine dagreNet)
        {
            dagreNet.FontSize = options.FontSize;
        }
        _layoutEngine.Layout(model, layoutOptions);

        // Subgraph bounds are computed by the layout engine (dagre compound graph).
        // Only fall back to manual calculation if the layout engine didn't set bounds.
        CalculateSubgraphBounds(model, skipIfAlreadySet: true);

        // Recalculate layout bounds to include new edge routes
        var layoutResult = CalculateLayoutBounds(model);

        var skin = FlowchartSkin.Resolve(options.Theme, options.ThemeColors);

        return new FlowchartLayoutResult(model, skin, layoutResult.Width, layoutResult.Height, options.CurvedEdges);
    }

    public SvgDocument Render(FlowchartModel model, RenderOptions options)
    {
        var layout = LayoutModel(model, options);

        // Apply skin corner radius to options so ShapePathGenerator uses it
        options.NodeCornerRadius = layout.Skin.NodeCornerRadius;

        // Build SVG
        var builder = new SvgBuilder()
            .Size(layout.Width, layout.Height)
            .Padding(options.Padding)
            .IncludeExternalResources(options.IncludeExternalResources)
            .AddMermaidArrowMarker()
            .AddMermaidCircleMarker()
            .AddMermaidCrossMarker()
            .AddDropShadowFilter();

        // Add structured skin-based CSS styles
        builder.AddStyles(BuildSkinCss(options, layout.Skin));

        // Render subgraph containers first (background layer)
        RenderSubgraphs(builder, model);

        // Render edges first (behind nodes)
        foreach (var edge in model.Edges)
        {
            RenderEdge(builder, edge, options);
        }

        // Render nodes
        foreach (var node in model.Nodes)
        {
            RenderNode(builder, node, options);
        }

        return builder.Build();
    }

    // Dead code removed: Port enum, GetPortPosition, GetDefaultPorts, IntersectNode,
    // IntersectRect/Diamond/Ellipse/Hexagon, IsForwardEdge, IsNonRectangularShape,
    // ComputeExitPoint/EntryPoint, GenerateOrthogonalEdgeRoutes, EdgePassesThroughNodes,
    // GenerateSelfLoop, GenerateSideRoute — all superseded by DagreNetLayoutEngine.
    static LayoutResult CalculateLayoutBounds(FlowchartModel model)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var node in model.Nodes)
        {
            minX = Math.Min(minX, node.Position.X - node.Width / 2);
            minY = Math.Min(minY, node.Position.Y - node.Height / 2);
            maxX = Math.Max(maxX, node.Position.X + node.Width / 2);
            maxY = Math.Max(maxY, node.Position.Y + node.Height / 2);
        }

        foreach (var edge in model.Edges)
        {
            foreach (var point in edge.Points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        // Include subgraph bounds
        foreach (var sg in model.Subgraphs)
        {
            if (sg.Width <= 0) continue;
            var b = sg.Bounds;
            minX = Math.Min(minX, b.Left);
            minY = Math.Min(minY, b.Top);
            maxX = Math.Max(maxX, b.Right);
            maxY = Math.Max(maxY, b.Bottom);
        }

        if (model.Nodes.Count == 0)
            return new() { Width = 0, Height = 0 };

        // Shift all coordinates to ensure nothing is at negative positions
        if (minX < 0 || minY < 0)
        {
            var shiftX = minX < 0 ? -minX : 0;
            var shiftY = minY < 0 ? -minY : 0;

            foreach (var node in model.Nodes)
                node.Position = new(node.Position.X + shiftX, node.Position.Y + shiftY);

            foreach (var edge in model.Edges)
            {
                for (var i = 0; i < edge.Points.Count; i++)
                    edge.Points[i] = new(edge.Points[i].X + shiftX, edge.Points[i].Y + shiftY);
            }

            foreach (var sg in model.Subgraphs)
            {
                if (sg.Width > 0)
                    sg.Position = new(sg.Position.X + shiftX, sg.Position.Y + shiftY);
            }

            maxX += shiftX;
            maxY += shiftY;
        }

        return new() { Width = Math.Max(0, maxX), Height = Math.Max(0, maxY) };
    }

    static void RenderNode(SvgBuilder builder, Node node, RenderOptions options)
    {
        var x = node.Position.X - node.Width / 2;
        var y = node.Position.Y - node.Height / 2;

        var skinnedPath =
            ShapePathGenerator.GetPathWithSkin(node.Shape, x, y, node.Width, node.Height, options, "flowchart", node.SkinShapeName);
        var shapeClass = GetShapeClass(node.Shape);

        // Build inline style to override CSS class defaults when custom styles are set.
        // SVG presentation attributes (fill="...") are overridden by CSS class rules,
        // but inline style="..." takes highest precedence.
        var hasCustomStyle = node.Style.Fill is not null || node.Style.Stroke is not null
            || node.Style.StrokeWidth is not null || node.Style.StrokeDasharray is not null;

        string? customStyle = null;
        if (hasCustomStyle)
        {
            var sb = new StringBuilder(64);
            if (node.Style.Fill is not null) sb.Append($"fill:{node.Style.Fill}");
            if (node.Style.Stroke is not null) { if (sb.Length > 0) sb.Append(';'); sb.Append($"stroke:{node.Style.Stroke}"); }
            if (node.Style.StrokeWidth is not null) { if (sb.Length > 0) sb.Append(';'); sb.Append($"stroke-width:{node.Style.StrokeWidth}"); }
            if (node.Style.StrokeDasharray is not null) { if (sb.Length > 0) sb.Append(';'); sb.Append($"stroke-dasharray:{node.Style.StrokeDasharray}"); }
            customStyle = SecurityValidator.SanitizeCss(sb.ToString());
        }

        RenderSkinnedNodePaths(builder, skinnedPath, shapeClass, customStyle);

        // Render label as SVG text (centered in node)
        // Use wrapped label if text was wrapped during measurement
        var label = node.WrappedLabel ?? node.DisplayLabel;
        var displayLabel = IconPattern.Replace(label, "").Trim();
        var centerX = node.Position.X;
        var centerY = node.Position.Y;

        // Optical centering: diamond shapes appear bottom-heavy, shift text up slightly
        if (node.Shape == NodeShape.Diamond)
            centerY -= 2;

        // Build text style for custom text color/font
        string? textStyle = null;
        if (node.Style.TextColor is not null || node.Style.FontWeight is not null)
        {
            textStyle = (node.Style.TextColor, node.Style.FontWeight) switch
            {
                (not null, not null) => $"fill:{node.Style.TextColor};font-weight:{node.Style.FontWeight}",
                (not null, null) => $"fill:{node.Style.TextColor}",
                (null, not null) => $"font-weight:{node.Style.FontWeight}",
                _ => null
            };
            if (!string.IsNullOrEmpty(textStyle))
                textStyle = SecurityValidator.SanitizeCss(textStyle);
        }

        var lines = displayLabel.Split('\n');
        if (lines.Length <= 1)
        {
            builder.AddText(
                centerX, centerY,
                displayLabel,
                anchor: "middle",
                baseline: "central",
                cssClass: "flow-node-label",
                style: textStyle);
        }
        else
        {
            var lineHeight = 14.0 * 1.5;
            var totalHeight = lines.Length * lineHeight;
            var startY = centerY - totalHeight / 2 + lineHeight / 2;
            builder.AddMultiLineText(
                centerX, startY, lineHeight,
                lines,
                anchor: "middle",
                cssClass: "flow-node-label");
        }
    }

    static string? MergeInlineStyles(string? baseStyle, string? overrideStyle)
    {
        if (string.IsNullOrWhiteSpace(baseStyle))
            return string.IsNullOrWhiteSpace(overrideStyle) ? null : overrideStyle;
        if (string.IsNullOrWhiteSpace(overrideStyle))
            return baseStyle;

        return $"{baseStyle};{overrideStyle}";
    }

    static void RenderSkinnedNodePaths(
        SvgBuilder builder,
        ShapePathGenerator.SkinnedPath skinnedPath,
        string shapeClass,
        string? customStyle)
    {
        if (!string.IsNullOrWhiteSpace(skinnedPath.DefsContent))
        {
            builder.AddRawDefs(skinnedPath.DefsContent!);
        }

        var layers = skinnedPath.Layers;
        if (layers is null || layers.Count == 0)
        {
            builder.AddPath(skinnedPath.PathData,
                cssClass: $"flow-node flow-node-shape {shapeClass}",
                inlineStyle: MergeInlineStyles(skinnedPath.InlineStyle, customStyle),
                transform: skinnedPath.Transform);
            return;
        }

        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            var style = i == 0
                ? MergeInlineStyles(layer.InlineStyle, customStyle)
                : layer.InlineStyle;

            builder.AddPath(layer.PathData,
                cssClass: $"flow-node flow-node-shape {shapeClass}",
                inlineStyle: style,
                transform: skinnedPath.Transform);
        }
    }

    /// <summary>
    /// Build a smooth SVG path through waypoints using cubic bezier curves.
    /// Uses Catmull-Rom spline conversion: the curve passes through all points
    /// with smooth transitions, similar to mermaid.js/d3 curve rendering.
    /// </summary>
    /// <summary>
    /// Build a smooth SVG path using d3-style "basis" B-spline interpolation.
    /// Unlike Catmull-Rom (which passes through all points), basis curves
    /// approximate the control polygon, staying close without wild swings.
    /// This matches mermaid.js default curve: "basis".
    /// </summary>
    static string BuildSmoothPath(List<Position> points)
    {
        var sb = new StringBuilder();

        if (points.Count == 3)
        {
            // Quadratic bezier for 3 points
            sb.Append($"M{Fmt(points[0].X)},{Fmt(points[0].Y)}");
            sb.Append($" Q{Fmt(points[1].X)},{Fmt(points[1].Y)} {Fmt(points[2].X)},{Fmt(points[2].Y)}");
            return sb.ToString();
        }

        // Uniform cubic B-spline (matches d3.curveBasis).
        // The curve starts at point[0] and ends at point[n-1].
        // Intermediate points are approximated, not interpolated.
        // For each interior segment, the cubic bezier control points are:
        //   start = (P[i-1] + 4*P[i] + P[i+1]) / 6
        //   cp1   = (2*P[i] + P[i+1]) / 3
        //   cp2   = (P[i] + 2*P[i+1]) / 3
        //   end   = (P[i] + 4*P[i+1] + P[i+2]) / 6
        var n = points.Count;

        // Start at the first point
        sb.Append($"M{Fmt(points[0].X)},{Fmt(points[0].Y)}");

        if (n == 4)
        {
            // Special case: single interior segment
            var cp1x = (2 * points[1].X + points[2].X) / 3;
            var cp1y = (2 * points[1].Y + points[2].Y) / 3;
            var cp2x = (points[1].X + 2 * points[2].X) / 3;
            var cp2y = (points[1].Y + 2 * points[2].Y) / 3;
            sb.Append($" C{Fmt(cp1x)},{Fmt(cp1y)} {Fmt(cp2x)},{Fmt(cp2y)} {Fmt(points[3].X)},{Fmt(points[3].Y)}");
            return sb.ToString();
        }

        // First segment: from P[0] to the first B-spline knot
        var firstKnotX = (points[0].X + 4 * points[1].X + points[2].X) / 6;
        var firstKnotY = (points[0].Y + 4 * points[1].Y + points[2].Y) / 6;
        var fc1x = (2 * points[0].X + points[1].X) / 3;
        var fc1y = (2 * points[0].Y + points[1].Y) / 3;
        var fc2x = (points[0].X + 2 * points[1].X) / 3;
        var fc2y = (points[0].Y + 2 * points[1].Y) / 3;
        sb.Append($" C{Fmt(fc1x)},{Fmt(fc1y)} {Fmt(fc2x)},{Fmt(fc2y)} {Fmt(firstKnotX)},{Fmt(firstKnotY)}");

        // Interior segments
        for (var i = 1; i < n - 3; i++)
        {
            var knotX = (points[i].X + 4 * points[i + 1].X + points[i + 2].X) / 6;
            var knotY = (points[i].Y + 4 * points[i + 1].Y + points[i + 2].Y) / 6;
            var c1x = (2 * points[i].X + points[i + 1].X) / 3;
            var c1y = (2 * points[i].Y + points[i + 1].Y) / 3;
            var c2x = (points[i].X + 2 * points[i + 1].X) / 3;
            var c2y = (points[i].Y + 2 * points[i + 1].Y) / 3;
            sb.Append($" C{Fmt(c1x)},{Fmt(c1y)} {Fmt(c2x)},{Fmt(c2y)} {Fmt(knotX)},{Fmt(knotY)}");
        }

        // Last segment: to P[n-1]
        var li = n - 3;
        var lc1x = (2 * points[li].X + points[li + 1].X) / 3;
        var lc1y = (2 * points[li].Y + points[li + 1].Y) / 3;
        var lc2x = (points[li].X + 2 * points[li + 1].X) / 3;
        var lc2y = (points[li].Y + 2 * points[li + 1].Y) / 3;
        sb.Append($" C{Fmt(lc1x)},{Fmt(lc1y)} {Fmt(lc2x)},{Fmt(lc2y)} {Fmt(points[n - 1].X)},{Fmt(points[n - 1].Y)}");

        return sb.ToString();
    }

    /// <summary>
    /// Build a straight polyline path through all waypoints.
    /// </summary>
    static string BuildLinearPath(List<Position> points)
    {
        var sb = new StringBuilder();
        sb.Append($"M{Fmt(points[0].X)},{Fmt(points[0].Y)}");
        for (var i = 1; i < points.Count; i++)
            sb.Append($" L{Fmt(points[i].X)},{Fmt(points[i].Y)}");
        return sb.ToString();
    }

    /// <summary>
    /// Build an orthogonal (step/right-angle) path between waypoints.
    /// Each segment goes horizontal first, then vertical (H-V pattern).
    /// </summary>
    static string BuildStepPath(List<Position> points)
    {
        var sb = new StringBuilder();
        sb.Append($"M{Fmt(points[0].X)},{Fmt(points[0].Y)}");
        for (var i = 1; i < points.Count; i++)
        {
            var prev = points[i - 1];
            var curr = points[i];
            // Step: horizontal to target X, then vertical to target Y
            var midY = (prev.Y + curr.Y) / 2;
            sb.Append($" L{Fmt(prev.X)},{Fmt(midY)}");
            sb.Append($" L{Fmt(curr.X)},{Fmt(midY)}");
            sb.Append($" L{Fmt(curr.X)},{Fmt(curr.Y)}");
        }
        return sb.ToString();
    }

    static void RenderEdge(SvgBuilder builder, Edge edge, RenderOptions options)
    {
        if (edge.Points.Count < 2)
        {
            return;
        }

        var points = edge.Points;
        string pathData;

        if (points.Count == 2)
        {
            // Simple straight line
            pathData = $"M{Fmt(points[0].X)},{Fmt(points[0].Y)} L{Fmt(points[1].X)},{Fmt(points[1].Y)}";
        }
        else
        {
            pathData = options.CurveStyle switch
            {
                CurveStyle.Linear => BuildLinearPath(points),
                CurveStyle.Step => BuildStepPath(points),
                _ => BuildSmoothPath(points)
            };
        }

        var markerEnd = edge.HasArrowHead ? "url(#mermaid-svg_flowchart-v2-pointEnd)" :
                        edge.HasCircleEnd ? "url(#mermaid-svg_flowchart-v2-circleEnd)" :
                        edge.HasCrossEnd ? "url(#mermaid-svg_flowchart-v2-crossEnd)" : null;

        var markerStart = edge.HasArrowTail ? "url(#mermaid-svg_flowchart-v2-pointStart)" : null;
        var edgeStyleClass = edge.LineStyle switch
        {
            EdgeStyle.Dotted => "flow-edge-dotted",
            EdgeStyle.Thick => "flow-edge-thick",
            _ => "flow-edge-solid"
        };

        var edgeTypeClass = edge.Type switch
        {
            EdgeType.BiDirectional => "flow-edge-bidirectional",
            EdgeType.BiDirectionalCircle => "flow-edge-bidirectional-circle",
            EdgeType.BiDirectionalCross => "flow-edge-bidirectional-cross",
            EdgeType.CircleEnd => "flow-edge-circle-end",
            EdgeType.CrossEnd => "flow-edge-cross-end",
            EdgeType.DottedArrow => "flow-edge-dotted-arrow",
            EdgeType.ThickArrow => "flow-edge-thick-arrow",
            EdgeType.Open => "flow-edge-open",
            _ => "flow-edge-arrow"
        };

        builder.AddPath(pathData,
            fill: "none",
            markerEnd: markerEnd,
            markerStart: markerStart,
            cssClass: $"flow-edge {edgeStyleClass} {edgeTypeClass}");

        // Render edge label if present
        if (!string.IsNullOrEmpty(edge.Label))
        {
            var labelX = edge.LabelPosition.X;
            var labelY = edge.LabelPosition.Y;
            var labelSize = MeasureText(edge.Label, options.FontSize * 0.9);
            var labelWidth = labelSize.Width + 16;
            var labelHeight = labelSize.Height + 8;

            builder.AddRect(
                labelX - labelWidth / 2, labelY - labelHeight / 2,
                labelWidth, labelHeight,
                cssClass: "flow-edge-label-box");

            builder.AddText(
                labelX, labelY,
                edge.Label,
                anchor: "middle",
                baseline: "central",
                cssClass: "flow-edge-label");
        }
    }

    /// <summary>
    /// After layout positions all nodes, compute bounding boxes for subgraphs
    /// from their member nodes. Processes nested subgraphs leaf-first so that
    /// parent subgraphs encompass child subgraph bounds.
    /// </summary>
    static void CalculateSubgraphBounds(FlowchartModel model, bool skipIfAlreadySet = false)
    {
        if (model.Subgraphs.Count == 0) return;

        // Build node lookup for fast access
        var nodeById = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (var node in model.Nodes)
            nodeById[node.Id] = node;

        foreach (var sg in model.Subgraphs)
            CalculateSubgraphBoundsRecursive(sg, nodeById, skipIfAlreadySet);
    }

    static void CalculateSubgraphBoundsRecursive(Subgraph subgraph, Dictionary<string, Node> nodeById, bool skipIfAlreadySet)
    {
        // Process children first (leaf-to-root)
        foreach (var nested in subgraph.NestedSubgraphs)
            CalculateSubgraphBoundsRecursive(nested, nodeById, skipIfAlreadySet);

        // Skip if layout engine already computed bounds (dagre compound graph)
        if (skipIfAlreadySet && subgraph.Width > 0 && subgraph.Height > 0) return;

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        var hasContent = false;

        // Include direct member nodes
        foreach (var nodeId in subgraph.NodeIds)
        {
            if (!nodeById.TryGetValue(nodeId, out var node)) continue;
            hasContent = true;
            minX = Math.Min(minX, node.Position.X - node.Width / 2);
            minY = Math.Min(minY, node.Position.Y - node.Height / 2);
            maxX = Math.Max(maxX, node.Position.X + node.Width / 2);
            maxY = Math.Max(maxY, node.Position.Y + node.Height / 2);
        }

        // Include nested subgraph bounds
        foreach (var nested in subgraph.NestedSubgraphs)
        {
            if (nested.Width <= 0) continue;
            hasContent = true;
            var b = nested.Bounds;
            minX = Math.Min(minX, b.Left);
            minY = Math.Min(minY, b.Top);
            maxX = Math.Max(maxX, b.Right);
            maxY = Math.Max(maxY, b.Bottom);
        }

        if (!hasContent) return;

        // Add padding around content, extra top padding for title
        const double padding = 20;
        const double titlePadding = 24;

        minX -= padding;
        minY -= padding + (string.IsNullOrWhiteSpace(subgraph.Title) ? 0 : titlePadding);
        maxX += padding;
        maxY += padding;

        subgraph.Width = maxX - minX;
        subgraph.Height = maxY - minY;
        subgraph.Position = new((minX + maxX) / 2, (minY + maxY) / 2);
    }

    static void RenderSubgraphs(SvgBuilder builder, FlowchartModel model)
    {
        if (model.Subgraphs.Count == 0)
        {
            return;
        }

        var flattened = new List<(Subgraph Subgraph, int Depth)>();
        foreach (var subgraph in model.Subgraphs)
        {
            FlattenSubgraphs(subgraph, 0, flattened);
        }

        foreach (var (subgraph, depth) in flattened.OrderBy(x => x.Depth))
        {
            if (subgraph.Width <= 0 || subgraph.Height <= 0)
            {
                continue;
            }

            var bounds = subgraph.Bounds;

            builder.AddRect(
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                rx: 8,
                cssClass: $"flow-subgraph-box flow-subgraph-depth-{Math.Min(depth, 4)}");

            if (!string.IsNullOrWhiteSpace(subgraph.Title))
            {
                builder.AddText(
                    bounds.Center.X,
                    bounds.Y + 14,
                    subgraph.Title!,
                    anchor: "middle",
                    baseline: "middle",
                    cssClass: "flow-subgraph-title");
            }
        }
    }

    static void FlattenSubgraphs(Subgraph subgraph, int depth, List<(Subgraph Subgraph, int Depth)> result)
    {
        result.Add((subgraph, depth));
        foreach (var nested in subgraph.NestedSubgraphs)
        {
            FlattenSubgraphs(nested, depth + 1, result);
        }
    }

    static string GetShapeClass(NodeShape shape) =>
        shape switch
        {
            NodeShape.RoundedRectangle => "flow-node-shape-rounded",
            NodeShape.Stadium => "flow-node-shape-stadium",
            NodeShape.Subroutine => "flow-node-shape-subroutine",
            NodeShape.Cylinder => "flow-node-shape-cylinder",
            NodeShape.Circle => "flow-node-shape-circle",
            NodeShape.DoubleCircle => "flow-node-shape-double-circle",
            NodeShape.Asymmetric => "flow-node-shape-asymmetric",
            NodeShape.Diamond => "flow-node-shape-diamond",
            NodeShape.Hexagon => "flow-node-shape-hexagon",
            _ => "flow-node-shape-rectangle"
        };

    static string BuildSkinCss(RenderOptions options, FlowchartSkin skin)
    {
        var fontFamily = CssEscape(options.FontFamily);
        var fontSize = options.FontSize.ToString("0.##", CultureInfo.InvariantCulture);
        var textColor = skin.TextColor;

        // Use direct color values (not CSS variables) for Svg.Skia/SkiaSharp compatibility.
        // CSS custom properties (var()) are not supported by most SVG rasterizers.
        return
            $$"""
              /* Mermaid skin: {{skin.Name}} */
              #mermaid-svg{
                font-family:{{fontFamily}};
                font-size:{{fontSize}}px;
                fill:{{textColor}};
              }
              #mermaid-svg .flow-node-shape{
                fill:{{skin.NodeFill}};
                stroke:{{skin.NodeStroke}};
                stroke-width:1px;
              }
              #mermaid-svg .flow-node-label,
              #mermaid-svg .flow-node-label p{
                color:{{textColor}};
                fill:{{textColor}};
                margin:0;
              }
              #mermaid-svg .flow-edge{
                stroke:{{skin.EdgeStroke}};
                stroke-width:2.0px;
                fill:none;
              }
              #mermaid-svg .flow-edge-dotted{stroke-dasharray:2;}
              #mermaid-svg .flow-edge-thick{stroke-width:3.5px;}
              #mermaid-svg .flow-edge-label-box{
                fill:{{skin.EdgeLabelBackground}};
                stroke:none;
                opacity:0.9;
              }
              #mermaid-svg .flow-edge-label,
              #mermaid-svg .flow-edge-label p{
                color:{{textColor}};
                fill:{{textColor}};
                margin:0;
                text-align:center;
              }
              #mermaid-svg .flow-subgraph-box{
                fill:{{skin.SubgraphFill}};
                stroke:{{skin.SubgraphStroke}};
                stroke-width:1px;
              }
              #mermaid-svg .flow-subgraph-title{
                fill:{{skin.SubgraphTitleColor}};
                font-weight:600;
                font-size:{{Math.Max(options.FontSize - 1, 10):0.##}}px;
              }
              #mermaid-svg .marker{
                fill:{{skin.EdgeStroke}};
                stroke:{{skin.EdgeStroke}};
              }
              """;
    }

    /// <summary>
    /// Encode text for SVG text content — only &amp;, &lt;, &gt; need escaping.
    /// Unlike HtmlEncode, quotes are left as-is since they're safe in text content.
    /// </summary>
    static string XmlEncodeText(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal);

    static string CssEscape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    static Size MeasureText(string text, double fontSize)
    {
        var (w, h) = RenderUtils.MeasureTextBlock(text, fontSize);
        return new(w, h);
    }

    /// <summary>
    /// Wrap text to fit within maxWidth by inserting newlines at word boundaries.
    /// Matches mermaid.js behavior of wrapping long labels in foreignObject.
    /// </summary>
    static string WrapText(string text, double maxWidth, double fontSize)
    {
        var words = text.Split(' ');
        var lines = new List<string>();
        var currentLine = "";

        foreach (var word in words)
        {
            var testLine = currentLine.Length == 0 ? word : currentLine + " " + word;
            var testWidth = RenderUtils.MeasureTextWidth(testLine, fontSize);

            if (testWidth > maxWidth && currentLine.Length > 0)
            {
                lines.Add(currentLine);
                currentLine = word;
            }
            else
            {
                currentLine = testLine;
            }
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine);

        return string.Join("\n", lines);
    }

}
