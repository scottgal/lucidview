using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MermaidSharp.Diagrams.Flowchart;
using MermaidSharp.Models;
using AvRect = Avalonia.Rect;
using MermaidSharp.Rendering;

namespace MarkdownViewer.Controls;

/// <summary>
/// Renders a Naiad FlowchartLayoutResult as native Avalonia vector graphics
/// with interactive hover highlighting of connected nodes and edges.
/// Highlighted edges show animated "marching ants" to indicate flow direction.
/// </summary>
public class FlowchartCanvas : Control
{
    public static readonly StyledProperty<FlowchartLayoutResult?> LayoutProperty =
        AvaloniaProperty.Register<FlowchartCanvas, FlowchartLayoutResult?>(nameof(Layout));

    public static readonly StyledProperty<FlowchartSkin?> SkinOverrideProperty =
        AvaloniaProperty.Register<FlowchartCanvas, FlowchartSkin?>(nameof(SkinOverride));

    string? _hoveredNodeId;
    string? _hoveredEdgeKey;

    // Pre-built lookups rebuilt when Layout changes
    Dictionary<string, Node> _nodeById = new(StringComparer.Ordinal);
    HashSet<string> _connectedNodeIds = [];
    HashSet<string> _connectedEdgeKeys = [];
    List<(Subgraph Subgraph, int Depth)> _flattenedSubgraphs = [];

    // Marching ants animation for highlighted edges
    DispatcherTimer? _animTimer;
    double _dashOffset;

    // Shape-aware color palette — light and dark variants per shape type
    // Light: pastel fills with bold strokes. Dark: deep saturated fills with lighter strokes.
    static readonly Dictionary<NodeShape, (string Fill, string Stroke)> LightShapeColors = new()
    {
        [NodeShape.RoundedRectangle] = ("#E3F2FD", "#1565C0"), // Soft blue — processes
        [NodeShape.Rectangle]        = ("#F3E5F5", "#7B1FA2"), // Lavender — data/IO
        [NodeShape.Stadium]          = ("#E8F5E9", "#2E7D32"), // Mint green — terminals
        [NodeShape.Diamond]          = ("#FFF8E1", "#F57F17"), // Warm amber — decisions
        [NodeShape.Hexagon]          = ("#FCE4EC", "#C62828"), // Rose — preparation
        [NodeShape.Circle]           = ("#E0F7FA", "#00838F"), // Cyan — connectors
        [NodeShape.DoubleCircle]     = ("#E0F7FA", "#00838F"), // Cyan — connectors
        [NodeShape.Subroutine]       = ("#EDE7F6", "#4527A0"), // Deep purple — subroutines
        [NodeShape.Cylinder]         = ("#FFF3E0", "#E65100"), // Orange — databases
        [NodeShape.Asymmetric]       = ("#F1F8E9", "#558B2F"), // Olive — flags
        [NodeShape.Parallelogram]    = ("#E8EAF6", "#283593"), // Indigo — IO
        [NodeShape.ParallelogramAlt] = ("#E8EAF6", "#283593"), // Indigo — IO
        [NodeShape.Trapezoid]        = ("#EFEBE9", "#4E342E"), // Brown — manual ops
        [NodeShape.TrapezoidAlt]     = ("#EFEBE9", "#4E342E"), // Brown — manual ops
    };

    static readonly Dictionary<NodeShape, (string Fill, string Stroke)> DarkShapeColors = new()
    {
        [NodeShape.RoundedRectangle] = ("#1A3A5C", "#64B5F6"), // Deep navy → soft blue stroke
        [NodeShape.Rectangle]        = ("#3A1F4E", "#CE93D8"), // Dark plum → lavender stroke
        [NodeShape.Stadium]          = ("#1B3B2A", "#81C784"), // Deep forest → mint stroke
        [NodeShape.Diamond]          = ("#4A3A10", "#FFD54F"), // Dark amber → gold stroke
        [NodeShape.Hexagon]          = ("#4A1520", "#EF9A9A"), // Deep crimson → rose stroke
        [NodeShape.Circle]           = ("#0D3B3F", "#4DD0E1"), // Deep teal → cyan stroke
        [NodeShape.DoubleCircle]     = ("#0D3B3F", "#4DD0E1"), // Deep teal → cyan stroke
        [NodeShape.Subroutine]       = ("#2A1B4E", "#B39DDB"), // Dark violet → purple stroke
        [NodeShape.Cylinder]         = ("#4A2800", "#FFB74D"), // Dark burnt → orange stroke
        [NodeShape.Asymmetric]       = ("#2A3A1B", "#AED581"), // Dark olive → lime stroke
        [NodeShape.Parallelogram]    = ("#1A2050", "#9FA8DA"), // Deep indigo → periwinkle stroke
        [NodeShape.ParallelogramAlt] = ("#1A2050", "#9FA8DA"), // Deep indigo → periwinkle stroke
        [NodeShape.Trapezoid]        = ("#3E2723", "#BCAAA4"), // Dark brown → tan stroke
        [NodeShape.TrapezoidAlt]     = ("#3E2723", "#BCAAA4"), // Dark brown → tan stroke
    };

    // Decision edge label patterns for semantic coloring
    static readonly string[] PositiveLabels = ["yes", "true", "ok", "success", "pass", "accept"];
    static readonly string[] NegativeLabels = ["no", "false", "fail", "error", "reject", "cancel"];

    // Cached: which shape color palette to use (determined from skin)
    bool _isDarkMode;

    // Cached brushes/pens (rebuilt when skin changes)
    IBrush _nodeFillBrush = Brushes.White;
    IBrush _nodeHighlightBrush = Brushes.LightBlue;
    IPen _nodeStrokePen = new Pen(Brushes.Black, 1);
    IPen _nodeHighlightStrokePen = new Pen(Brushes.DodgerBlue, 2);
    IBrush _textBrush = Brushes.Black;
    IPen _edgePen = new Pen(Brushes.Gray, 1.5);
    IPen _edgeHighlightPen = new Pen(Brushes.DodgerBlue, 2.5);
    IPen _edgeDottedPen = new Pen(Brushes.Gray, 1.5) { DashStyle = DashStyle.Dash };
    IPen _edgeDottedHighlightPen = new Pen(Brushes.DodgerBlue, 2.5) { DashStyle = DashStyle.Dash };
    IPen _edgeThickPen = new Pen(Brushes.Gray, 3.5);
    IPen _edgeThickHighlightPen = new Pen(Brushes.DodgerBlue, 4.5);
    IBrush _edgeLabelBgBrush = new SolidColorBrush(Colors.White, 0.9);
    IBrush _subgraphFillBrush = new SolidColorBrush(Colors.LightGray, 0.25);
    IPen _subgraphStrokePen = new Pen(Brushes.Gray, 1);
    IBrush _subgraphTitleBrush = Brushes.Black;
    IBrush _arrowBrush = Brushes.Gray;
    IBrush _arrowHighlightBrush = Brushes.DodgerBlue;

    // Cached semantic colors for decision edges (rebuilt when skin changes)
    Color _semanticGreen = Color.FromRgb(46, 125, 50);
    Color _semanticRed = Color.FromRgb(198, 40, 40);
    IBrush _semanticGreenBrush = new SolidColorBrush(Color.FromRgb(46, 125, 50));
    IBrush _semanticRedBrush = new SolidColorBrush(Color.FromRgb(198, 40, 40));
    IPen _semanticGreenPen = new Pen(new SolidColorBrush(Color.FromRgb(46, 125, 50)), 2.0);
    IPen _semanticRedPen = new Pen(new SolidColorBrush(Color.FromRgb(198, 40, 40)), 2.0);
    IPen _semanticGreenDottedPen = new Pen(new SolidColorBrush(Color.FromRgb(46, 125, 50)), 1.5) { DashStyle = DashStyle.Dash };
    IPen _semanticRedDottedPen = new Pen(new SolidColorBrush(Color.FromRgb(198, 40, 40)), 1.5) { DashStyle = DashStyle.Dash };
    IPen _semanticGreenThickPen = new Pen(new SolidColorBrush(Color.FromRgb(46, 125, 50)), 3.5);
    IPen _semanticRedThickPen = new Pen(new SolidColorBrush(Color.FromRgb(198, 40, 40)), 3.5);

    // Cached shape-aware brushes/pens (rebuilt when skin changes)
    Dictionary<NodeShape, (IBrush Fill, IBrush HighlightFill, IPen Stroke)> _shapeBrushes = new();

    public FlowchartLayoutResult? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public FlowchartSkin? SkinOverride
    {
        get => GetValue(SkinOverrideProperty);
        set => SetValue(SkinOverrideProperty, value);
    }

    /// <summary>
    /// Fired when a node with a Link property is clicked.
    /// </summary>
    public event EventHandler<string>? LinkClicked;

    static FlowchartCanvas()
    {
        AffectsRender<FlowchartCanvas>(LayoutProperty, SkinOverrideProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LayoutProperty)
        {
            RebuildLookups();
            RebuildBrushes();
            InvalidateMeasure();
        }
        else if (change.Property == SkinOverrideProperty)
        {
            RebuildBrushes();
        }
    }

    // Computed scale factor to fit within available width
    double _scale = 1.0;

    protected override global::Avalonia.Size MeasureOverride(global::Avalonia.Size availableSize)
    {
        var layout = Layout;
        if (layout is null) return default;

        var naturalWidth = layout.Width + 40;
        var naturalHeight = layout.Height + 40;

        // Scale down to fit available width (never scale up)
        _scale = 1.0;
        if (availableSize.Width > 0 && !double.IsInfinity(availableSize.Width) && naturalWidth > availableSize.Width)
        {
            _scale = availableSize.Width / naturalWidth;
        }

        return new global::Avalonia.Size(
            naturalWidth * _scale,
            naturalHeight * _scale);
    }

    protected override global::Avalonia.Size ArrangeOverride(global::Avalonia.Size finalSize)
    {
        var layout = Layout;
        if (layout is null) return default;

        var naturalWidth = layout.Width + 40;
        var naturalHeight = layout.Height + 40;

        // Re-check scale with final size
        _scale = 1.0;
        if (finalSize.Width > 0 && naturalWidth > finalSize.Width)
        {
            _scale = finalSize.Width / naturalWidth;
        }

        return new global::Avalonia.Size(
            naturalWidth * _scale,
            naturalHeight * _scale);
    }

    public override void Render(DrawingContext context)
    {
        var layout = Layout;
        if (layout is null) return;

        var padding = 20.0;

        // Apply scale + padding transform
        var transform = _scale < 1.0
            ? Matrix.CreateScale(_scale, _scale) * Matrix.CreateTranslation(padding * _scale, padding * _scale)
            : Matrix.CreateTranslation(padding, padding);

        using (context.PushTransform(transform))
        {
            RenderSubgraphs(context);
            RenderEdges(context, layout);
            RenderNodes(context, layout);
        }
    }

    void RenderSubgraphs(DrawingContext context)
    {
        foreach (var (subgraph, _) in _flattenedSubgraphs)
        {
            if (subgraph.Width <= 0 || subgraph.Height <= 0) continue;

            var bounds = subgraph.Bounds;
            var rect = new AvRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            var roundedRect = new RoundedRect(rect, 8, 8);

            context.DrawRectangle(_subgraphFillBrush, _subgraphStrokePen, roundedRect);

            if (!string.IsNullOrWhiteSpace(subgraph.Title))
            {
                var text = CreateFormattedText(subgraph.Title!, 12, _subgraphTitleBrush, FontWeight.SemiBold);
                context.DrawText(text, new Point(
                    bounds.Center.X - text.Width / 2,
                    bounds.Y + 6));
            }
        }
    }

    void RenderEdges(DrawingContext context, FlowchartLayoutResult layout)
    {
        foreach (var edge in layout.Model.Edges)
        {
            if (edge.Points.Count < 2) continue;

            var edgeKey = $"{edge.SourceId}->{edge.TargetId}";
            var isHighlighted = _connectedEdgeKeys.Contains(edgeKey);

            var pen = GetEdgePen(edge, isHighlighted);

            // Build edge path
            var geometry = BuildEdgePath(edge, layout.CurvedEdges);
            context.DrawGeometry(null, pen, geometry);

            // Draw arrowhead — use semantic color if applicable
            var arrowBrush = GetArrowBrush(edge, isHighlighted);
            if (edge.HasArrowHead)
            {
                DrawArrowhead(context, edge.Points, arrowBrush, atStart: false);
            }

            if (edge.HasArrowTail)
            {
                DrawArrowhead(context, edge.Points, arrowBrush, atStart: true);
            }

            // Draw edge label
            if (!string.IsNullOrEmpty(edge.Label))
            {
                RenderEdgeLabel(context, edge);
            }
        }
    }

    void RenderNodes(DrawingContext context, FlowchartLayoutResult layout)
    {
        foreach (var node in layout.Model.Nodes)
        {
            var x = node.Position.X - node.Width / 2;
            var y = node.Position.Y - node.Height / 2;

            var isHighlighted = _connectedNodeIds.Contains(node.Id);
            var isHovered = node.Id == _hoveredNodeId;

            // Get node fill — respect per-node style overrides
            var fill = GetNodeFillBrush(node, isHighlighted);
            var strokePen = GetNodeStrokePen(node, isHighlighted || isHovered);

            // Draw shape
            var pathData = ShapePathGenerator.GetPath(node.Shape, x, y, node.Width, node.Height);
            var geometry = Geometry.Parse(pathData);
            context.DrawGeometry(fill, strokePen, geometry);

            // Draw label — ensure contrast against the node's fill color
            var label = node.DisplayLabel;
            IBrush textBrush;
            if (node.Style.TextColor is not null)
            {
                var styleTextColor = TryParseColor(node.Style.TextColor);
                if (styleTextColor is not null && _isDarkMode)
                    styleTextColor = EnsureMinLuminance(styleTextColor.Value, 168);
                textBrush = styleTextColor is not null ? new SolidColorBrush(styleTextColor.Value) : _textBrush;
            }
            else
            {
                // Use skin text color, which should contrast with the shape fill
                textBrush = _textBrush;
            }

            // Handle multi-line labels (from <br/> tags)
            var lines = label.Split('\n');
            if (lines.Length <= 1)
            {
                var text = CreateFormattedText(label, 14, textBrush);
                context.DrawText(text, new Point(
                    node.Position.X - text.Width / 2,
                    node.Position.Y - text.Height / 2));
            }
            else
            {
                var lineHeight = 14 * 1.5;
                var totalHeight = lines.Length * lineHeight;
                var startY = node.Position.Y - totalHeight / 2;
                for (var li = 0; li < lines.Length; li++)
                {
                    var lineText = CreateFormattedText(lines[li].Trim(), 14, textBrush);
                    context.DrawText(lineText, new Point(
                        node.Position.X - lineText.Width / 2,
                        startY + li * lineHeight));
                }
            }

            // Draw tooltip cursor hint for nodes with links
            if (node.Link is not null && isHovered)
            {
                Cursor = new Cursor(StandardCursorType.Hand);
            }
        }
    }

    static StreamGeometry BuildEdgePath(Edge edge, bool curved)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();

        var points = edge.Points;
        ctx.BeginFigure(ToPoint(points[0]), false);

        if (points.Count == 2 || !curved)
        {
            for (var i = 1; i < points.Count; i++)
                ctx.LineTo(ToPoint(points[i]));
        }
        else if (IsCubicBezierSequence(points))
        {
            // MSAGL outputs: start + groups of (cp1, cp2, endpoint)
            // Render as native cubic bezier segments
            for (var i = 1; i + 2 < points.Count; i += 3)
                ctx.CubicBezierTo(ToPoint(points[i]), ToPoint(points[i + 1]), ToPoint(points[i + 2]));
        }
        else
        {
            // d3.curveBasis (uniform cubic B-spline) — matches mermaid.js edge rendering
            var n = points.Count;

            // Line to first B-spline approach point
            var lx = (5 * points[0].X + points[1].X) / 6;
            var ly = (5 * points[0].Y + points[1].Y) / 6;
            ctx.LineTo(new Point(lx, ly));

            // Interior cubic bezier segments using consecutive point triples
            for (var k = 0; k <= n - 3; k++)
            {
                var p0 = points[k];
                var p1 = points[k + 1];
                var p2 = points[k + 2];
                ctx.CubicBezierTo(
                    new Point((2 * p0.X + p1.X) / 3, (2 * p0.Y + p1.Y) / 3),
                    new Point((p0.X + 2 * p1.X) / 3, (p0.Y + 2 * p1.Y) / 3),
                    new Point((p0.X + 4 * p1.X + p2.X) / 6, (p0.Y + 4 * p1.Y + p2.Y) / 6));
            }

            // Final segment from lineEnd — uses P[N-2], P[N-1], P[N-1]
            {
                var p0 = points[n - 2];
                var p1 = points[n - 1];
                ctx.CubicBezierTo(
                    new Point((2 * p0.X + p1.X) / 3, (2 * p0.Y + p1.Y) / 3),
                    new Point((p0.X + 2 * p1.X) / 3, (p0.Y + 2 * p1.Y) / 3),
                    new Point((p0.X + 5 * p1.X) / 6, (p0.Y + 5 * p1.Y) / 6));
            }

            // Line to exact endpoint
            ctx.LineTo(ToPoint(points[^1]));
        }

        return geometry;
    }

    /// <summary>
    /// Detect if points are cubic bezier sequence from MSAGL:
    /// start point + N groups of 3 (cp1, cp2, endpoint) = 1 + 3N points.
    /// </summary>
    static bool IsCubicBezierSequence(List<Position> points)
    {
        return points.Count >= 4 && (points.Count - 1) % 3 == 0;
    }

    IBrush GetArrowBrush(Edge edge, bool highlighted)
    {
        if (highlighted) return _arrowHighlightBrush;

        if (!string.IsNullOrEmpty(edge.Label))
        {
            var label = edge.Label!.Trim().ToLowerInvariant();
            if (PositiveLabels.Any(l => label == l))
                return _semanticGreenBrush;
            if (NegativeLabels.Any(l => label == l))
                return _semanticRedBrush;
        }

        return _arrowBrush;
    }

    static void DrawArrowhead(DrawingContext context, List<Position> points, IBrush brush, bool atStart)
    {
        Point tip, from;
        if (atStart)
        {
            tip = ToPoint(points[0]);
            from = ToPoint(points[1]);
        }
        else
        {
            tip = ToPoint(points[^1]);
            from = ToPoint(points[^2]);
        }

        var dx = tip.X - from.X;
        var dy = tip.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.1) return;

        var ux = dx / len;
        var uy = dy / len;

        const double arrowLength = 10;
        const double arrowWidth = 5;

        var baseX = tip.X - ux * arrowLength;
        var baseY = tip.Y - uy * arrowLength;

        var left = new Point(baseX - uy * arrowWidth, baseY + ux * arrowWidth);
        var right = new Point(baseX + uy * arrowWidth, baseY - ux * arrowWidth);

        var arrow = new StreamGeometry();
        using (var ctx = arrow.Open())
        {
            ctx.BeginFigure(tip, true);
            ctx.LineTo(left);
            ctx.LineTo(right);
            ctx.EndFigure(true);
        }

        context.DrawGeometry(brush, null, arrow);
    }

    void RenderEdgeLabel(DrawingContext context, Edge edge)
    {
        var labelPos = edge.LabelPosition;
        var text = CreateFormattedText(edge.Label!, 12, _textBrush);

        var bgWidth = text.Width + 8;
        var bgHeight = text.Height + 4;
        var bgRect = new AvRect(
            labelPos.X - bgWidth / 2,
            labelPos.Y - bgHeight / 2,
            bgWidth,
            bgHeight);

        context.DrawRectangle(_edgeLabelBgBrush, null, new RoundedRect(bgRect, 3, 3));
        context.DrawText(text, new Point(
            labelPos.X - text.Width / 2,
            labelPos.Y - text.Height / 2));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _animTimer?.Stop();
        _animTimer = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var layout = Layout;
        if (layout is null) return;

        var pos = e.GetPosition(this);
        var padding = 20.0;
        // Account for scale when converting pointer position to layout coordinates
        var testPos = _scale < 1.0
            ? new Position(pos.X / _scale - padding, pos.Y / _scale - padding)
            : new Position(pos.X - padding, pos.Y - padding);

        string? newHoveredNode = null;
        string? newHoveredEdge = null;

        // Check nodes
        foreach (var node in layout.Model.Nodes)
        {
            if (node.Bounds.Contains(testPos))
            {
                newHoveredNode = node.Id;
                break;
            }
        }

        // Check edges if no node hovered
        if (newHoveredNode is null)
        {
            foreach (var edge in layout.Model.Edges)
            {
                if (IsNearEdge(testPos, edge, 12))
                {
                    newHoveredEdge = $"{edge.SourceId}->{edge.TargetId}";
                    newHoveredNode = edge.SourceId; // Highlight source node's connections
                    break;
                }
            }
        }

        if (newHoveredNode != _hoveredNodeId || newHoveredEdge != _hoveredEdgeKey)
        {
            _hoveredNodeId = newHoveredNode;
            _hoveredEdgeKey = newHoveredEdge;
            RebuildHighlightSets();
            Cursor = _hoveredNodeId is not null && _nodeById.TryGetValue(_hoveredNodeId, out var n) && n.Link is not null
                ? new Cursor(StandardCursorType.Hand)
                : Cursor.Default;
            UpdateAnimationTimer();
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_hoveredNodeId is not null || _hoveredEdgeKey is not null)
        {
            _hoveredNodeId = null;
            _hoveredEdgeKey = null;
            _connectedNodeIds.Clear();
            _connectedEdgeKeys.Clear();
            Cursor = Cursor.Default;
            UpdateAnimationTimer();
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_hoveredNodeId is null) return;
        if (!_nodeById.TryGetValue(_hoveredNodeId, out var node)) return;

        if (node.Link is not null)
        {
            LinkClicked?.Invoke(this, node.Link);
        }
    }

    void RebuildLookups()
    {
        var layout = Layout;
        _nodeById.Clear();
        _flattenedSubgraphs.Clear();
        _connectedNodeIds.Clear();
        _connectedEdgeKeys.Clear();
        _hoveredNodeId = null;
        _hoveredEdgeKey = null;

        if (layout is null) return;

        foreach (var node in layout.Model.Nodes)
        {
            _nodeById[node.Id] = node;
        }

        FlattenSubgraphs(layout.Model.Subgraphs, 0);
    }

    void FlattenSubgraphs(List<Subgraph> subgraphs, int depth)
    {
        foreach (var sg in subgraphs)
        {
            _flattenedSubgraphs.Add((sg, depth));
            FlattenSubgraphs(sg.NestedSubgraphs, depth + 1);
        }
    }

    void RebuildHighlightSets()
    {
        _connectedNodeIds.Clear();
        _connectedEdgeKeys.Clear();

        if (Layout is null) return;

        // Edge hover: highlight only that specific edge and its two endpoints
        if (_hoveredEdgeKey is not null)
        {
            _connectedEdgeKeys.Add(_hoveredEdgeKey);
            // Parse "source->target" to get both node IDs
            var arrow = _hoveredEdgeKey.IndexOf("->", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                _connectedNodeIds.Add(_hoveredEdgeKey[..arrow]);
                _connectedNodeIds.Add(_hoveredEdgeKey[(arrow + 2)..]);
            }
            return;
        }

        // Data flow path tracing: show the specific path this node participates in.
        // Trace upstream (single incoming edge chain) and downstream (single outgoing
        // edge chain). At branch points, only follow the branch containing the hovered node.
        // This highlights the linear flow path, not sibling branches.
        if (_hoveredNodeId is null) return;

        _connectedNodeIds.Add(_hoveredNodeId);

        // Build edge lookup for efficient traversal
        var outgoing = new Dictionary<string, List<Edge>>(StringComparer.Ordinal);
        var incoming = new Dictionary<string, List<Edge>>(StringComparer.Ordinal);
        foreach (var edge in Layout.Model.Edges)
        {
            if (!outgoing.TryGetValue(edge.SourceId, out var outList))
            {
                outList = [];
                outgoing[edge.SourceId] = outList;
            }
            outList.Add(edge);

            if (!incoming.TryGetValue(edge.TargetId, out var inList))
            {
                inList = [];
                incoming[edge.TargetId] = inList;
            }
            inList.Add(edge);
        }

        // Trace upstream: follow incoming edges back to the source
        var current = _hoveredNodeId;
        var upVisited = new HashSet<string>(StringComparer.Ordinal) { current };
        while (incoming.TryGetValue(current, out var inEdges))
        {
            // If multiple incoming edges, follow the first one (primary flow)
            var edge = inEdges[0];
            _connectedEdgeKeys.Add($"{edge.SourceId}->{edge.TargetId}");
            _connectedNodeIds.Add(edge.SourceId);
            if (!upVisited.Add(edge.SourceId)) break; // cycle guard
            current = edge.SourceId;
        }

        // Trace downstream: follow outgoing edges, but only single-output paths.
        // At branch points (multiple outgoing), stop — we don't know which branch
        // the user cares about. Only continue if there's exactly one outgoing edge.
        current = _hoveredNodeId;
        var downVisited = new HashSet<string>(StringComparer.Ordinal) { current };
        while (outgoing.TryGetValue(current, out var outEdges))
        {
            if (outEdges.Count == 1)
            {
                // Single outgoing edge — follow it
                var edge = outEdges[0];
                _connectedEdgeKeys.Add($"{edge.SourceId}->{edge.TargetId}");
                _connectedNodeIds.Add(edge.TargetId);
                if (!downVisited.Add(edge.TargetId)) break;
                current = edge.TargetId;
            }
            else
            {
                // Branch point — highlight all outgoing edges but don't recurse
                foreach (var edge in outEdges)
                {
                    _connectedEdgeKeys.Add($"{edge.SourceId}->{edge.TargetId}");
                    _connectedNodeIds.Add(edge.TargetId);
                }
                break;
            }
        }
    }

    void UpdateAnimationTimer()
    {
        if (_connectedEdgeKeys.Count > 0)
        {
            // Start marching ants animation
            if (_animTimer is null)
            {
                _dashOffset = 0;
                _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _animTimer.Tick += (_, _) =>
                {
                    _dashOffset -= 1.0;
                    if (_dashOffset < -20) _dashOffset = 0;
                    InvalidateVisual();
                };
                _animTimer.Start();
            }
        }
        else
        {
            // Stop animation
            _animTimer?.Stop();
            _animTimer = null;
            _dashOffset = 0;
        }
    }

    void RebuildBrushes()
    {
        var skin = SkinOverride ?? Layout?.Skin;
        if (skin is null) return;

        // Detect dark mode from skin background luminance
        var bgColor = TryParseColor(skin.Background) ?? Colors.White;
        _isDarkMode = (0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B) < 128;

        var nodeFillColor = TryParseColor(skin.NodeFill) ?? (_isDarkMode ? Color.FromRgb(34, 40, 49) : Colors.White);
        var textColor = TryParseColor(skin.TextColor) ?? (_isDarkMode ? Color.FromRgb(230, 237, 243) : Colors.Black);
        var edgeStrokeColor = TryParseColor(skin.EdgeStroke) ?? (_isDarkMode ? Color.FromRgb(173, 186, 199) : Colors.Gray);
        var nodeStrokeColor = TryParseColor(skin.NodeStroke) ?? (_isDarkMode ? Color.FromRgb(173, 186, 199) : Colors.Black);
        var subgraphTitleColor = TryParseColor(skin.SubgraphTitleColor) ?? textColor;

        // Some skins still emit low-contrast dark text/strokes even for dark backgrounds.
        // Force minimum brightness so nodes/connectors remain visible.
        if (_isDarkMode)
        {
            textColor = EnsureMinLuminance(textColor, 168);
            edgeStrokeColor = EnsureMinLuminance(edgeStrokeColor, 136);
            nodeStrokeColor = EnsureMinLuminance(nodeStrokeColor, 136);
            subgraphTitleColor = EnsureMinLuminance(subgraphTitleColor, 168);
        }

        _nodeFillBrush = new SolidColorBrush(nodeFillColor);
        _textBrush = new SolidColorBrush(textColor);
        _arrowBrush = new SolidColorBrush(edgeStrokeColor);
        _arrowHighlightBrush = new SolidColorBrush(BlendColor(edgeStrokeColor, Color.FromRgb(60, 130, 240), 0.6));
        _subgraphTitleBrush = new SolidColorBrush(subgraphTitleColor);

        // Highlight color: blend toward blue
        var highlightColor = Color.FromRgb(60, 130, 240);

        _nodeHighlightBrush = new SolidColorBrush(BlendColor(
            TryParseColor(skin.NodeFill) ?? Colors.White, highlightColor, 0.15));
        _nodeStrokePen = new Pen(new SolidColorBrush(nodeStrokeColor), 1);
        _nodeHighlightStrokePen = new Pen(new SolidColorBrush(highlightColor), 2);

        _edgePen = new Pen(new SolidColorBrush(edgeStrokeColor), 1.5);
        _edgeHighlightPen = new Pen(new SolidColorBrush(highlightColor), 2.5);
        _edgeDottedPen = new Pen(new SolidColorBrush(edgeStrokeColor), 1.5) { DashStyle = DashStyle.Dash };
        _edgeDottedHighlightPen = new Pen(new SolidColorBrush(highlightColor), 2.5) { DashStyle = DashStyle.Dash };
        _edgeThickPen = new Pen(new SolidColorBrush(edgeStrokeColor), 3.5);
        _edgeThickHighlightPen = new Pen(new SolidColorBrush(highlightColor), 4.5);

        var edgeLabelBgColor = TryParseColor(skin.EdgeLabelBackground);
        if (_isDarkMode)
        {
            // Prefer dark translucent label background so bright text remains readable.
            if (edgeLabelBgColor is null || (0.299 * edgeLabelBgColor.Value.R + 0.587 * edgeLabelBgColor.Value.G + 0.114 * edgeLabelBgColor.Value.B) > 120)
                edgeLabelBgColor = Color.FromArgb(220, 22, 27, 34);
        }
        _edgeLabelBgBrush = edgeLabelBgColor is not null
            ? new SolidColorBrush(edgeLabelBgColor.Value)
            : new SolidColorBrush(Colors.White, 0.9);

        _subgraphFillBrush = TryParseBrush(skin.SubgraphFill)
            ?? new SolidColorBrush(Colors.LightGray, 0.25);
        _subgraphStrokePen = new Pen(
            TryParseBrush(skin.SubgraphStroke) ?? Brushes.Gray, 1);

        // Rebuild semantic decision-edge colors
        _semanticGreen = _isDarkMode ? Color.FromRgb(129, 199, 132) : Color.FromRgb(46, 125, 50);
        _semanticRed = _isDarkMode ? Color.FromRgb(239, 154, 154) : Color.FromRgb(198, 40, 40);
        _semanticGreenBrush = new SolidColorBrush(_semanticGreen);
        _semanticRedBrush = new SolidColorBrush(_semanticRed);
        _semanticGreenPen = new Pen(_semanticGreenBrush, 2.0);
        _semanticRedPen = new Pen(_semanticRedBrush, 2.0);
        _semanticGreenDottedPen = new Pen(new SolidColorBrush(_semanticGreen), 1.5) { DashStyle = DashStyle.Dash };
        _semanticRedDottedPen = new Pen(new SolidColorBrush(_semanticRed), 1.5) { DashStyle = DashStyle.Dash };
        _semanticGreenThickPen = new Pen(new SolidColorBrush(_semanticGreen), 3.5);
        _semanticRedThickPen = new Pen(new SolidColorBrush(_semanticRed), 3.5);

        // Rebuild shape-aware brushes
        _shapeBrushes.Clear();
        var palette = _isDarkMode ? DarkShapeColors : LightShapeColors;
        foreach (var (shape, (fill, stroke)) in palette)
        {
            var fillColor = TryParseColor(fill) ?? Colors.White;
            var strokeColor = TryParseColor(stroke) ?? Colors.Black;
            _shapeBrushes[shape] = (
                Fill: new SolidColorBrush(fillColor),
                HighlightFill: new SolidColorBrush(BlendColor(fillColor, highlightColor, 0.2)),
                Stroke: new Pen(new SolidColorBrush(strokeColor), 1.5));
        }
    }

    IPen GetEdgePen(Edge edge, bool highlighted)
    {
        // Semantic coloring for decision edges (using cached pens)
        if (!highlighted && !string.IsNullOrEmpty(edge.Label))
        {
            var label = edge.Label!.Trim().ToLowerInvariant();
            if (PositiveLabels.Any(l => label == l))
                return GetCachedSemanticPen(edge.LineStyle, isGreen: true);
            if (NegativeLabels.Any(l => label == l))
                return GetCachedSemanticPen(edge.LineStyle, isGreen: false);
        }

        // Highlighted edges use marching ants (animated dashed stroke) to show flow direction
        if (highlighted)
        {
            var highlightColor = Color.FromRgb(60, 130, 240);
            var thickness = edge.LineStyle == EdgeStyle.Thick ? 4.5 : 2.5;
            return new Pen(new SolidColorBrush(highlightColor), thickness)
            {
                DashStyle = new DashStyle([4, 3], _dashOffset)
            };
        }

        return edge.LineStyle switch
        {
            EdgeStyle.Dotted => _edgeDottedPen,
            EdgeStyle.Thick => _edgeThickPen,
            _ => _edgePen
        };
    }

    IPen GetCachedSemanticPen(EdgeStyle style, bool isGreen) => (style, isGreen) switch
    {
        (EdgeStyle.Dotted, true) => _semanticGreenDottedPen,
        (EdgeStyle.Dotted, false) => _semanticRedDottedPen,
        (EdgeStyle.Thick, true) => _semanticGreenThickPen,
        (EdgeStyle.Thick, false) => _semanticRedThickPen,
        (_, true) => _semanticGreenPen,
        (_, false) => _semanticRedPen,
    };

    IBrush GetNodeFillBrush(Node node, bool highlighted)
    {
        // Per-node style overrides take priority
        if (node.Style.Fill is not null)
        {
            var custom = TryParseBrush(node.Style.Fill);
            if (custom is not null)
            {
                return highlighted
                    ? new SolidColorBrush(BlendColor(
                        TryParseColor(node.Style.Fill) ?? Colors.White,
                        Color.FromRgb(60, 130, 240), 0.15))
                    : custom;
            }
        }

        // Shape-aware coloring — use cached brushes
        if (_shapeBrushes.TryGetValue(node.Shape, out var cached))
            return highlighted ? cached.HighlightFill : cached.Fill;

        return highlighted ? _nodeHighlightBrush : _nodeFillBrush;
    }

    IPen GetNodeStrokePen(Node node, bool highlighted)
    {
        if (highlighted) return _nodeHighlightStrokePen;

        // Per-node style overrides
        if (node.Style.Stroke is not null)
        {
            var color = TryParseColor(node.Style.Stroke);
            if (color is not null)
            {
                return new Pen(new SolidColorBrush(color.Value),
                    node.Style.StrokeWidth ?? 1);
            }
        }

        // Shape-aware stroke coloring — use cached pens
        if (_shapeBrushes.TryGetValue(node.Shape, out var cached))
            return cached.Stroke;

        return _nodeStrokePen;
    }

    static bool IsNearEdge(Position point, Edge edge, double threshold)
    {
        var pts = edge.Points;
        for (var i = 0; i < pts.Count - 1; i++)
        {
            if (DistanceToSegment(point, pts[i], pts[i + 1]) < threshold)
                return true;
        }

        return false;
    }

    static double DistanceToSegment(Position p, Position a, Position b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq < 0.001) return p.DistanceTo(a);

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        var proj = new Position(a.X + t * dx, a.Y + t * dy);
        return p.DistanceTo(proj);
    }

    static Point ToPoint(Position p) => new(p.X, p.Y);

    static FormattedText CreateFormattedText(string text, double fontSize, IBrush brush,
        FontWeight weight = FontWeight.Normal)
    {
        return new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Arial, sans-serif", FontStyle.Normal, weight),
            fontSize,
            brush);
    }

    static IBrush? TryParseBrush(string? color)
    {
        var c = TryParseColor(color);
        return c is not null ? new SolidColorBrush(c.Value) : null;
    }

    static Color EnsureMinLuminance(Color color, double minLuminance)
    {
        var luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        if (luminance >= minLuminance) return color;

        // Blend toward white until we hit the requested minimum luminance.
        var target = Math.Clamp((minLuminance - luminance) / 255.0, 0.0, 1.0);
        return BlendColor(color, Colors.White, target);
    }

    static Color? TryParseColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;

        if (color.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
        {
            var inner = color.AsSpan()[5..^1];
            var parts = inner.ToString().Split(',');
            if (parts.Length == 4 &&
                byte.TryParse(parts[0].Trim(), out var r) &&
                byte.TryParse(parts[1].Trim(), out var g) &&
                byte.TryParse(parts[2].Trim(), out var b) &&
                double.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var a))
            {
                return Color.FromArgb((byte)(a * 255), r, g, b);
            }
        }

        try
        {
            return Color.Parse(color);
        }
        catch
        {
            return null;
        }
    }

    static IBrush CreateHighlightBrush(string? baseColor)
    {
        var color = TryParseColor(baseColor) ?? Colors.Gray;
        return new SolidColorBrush(BlendColor(color, Color.FromRgb(60, 130, 240), 0.6));
    }

    static Color BlendColor(Color a, Color b, double t)
    {
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
