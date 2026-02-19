using System.Text.RegularExpressions;
using static MermaidSharp.Rendering.RenderUtils;
using ModelRect = MermaidSharp.Models.Rect;

namespace MermaidSharp.Diagrams.Flowchart;

public class FlowchartRenderer(ILayoutEngine? layoutEngine = null) :
    IDiagramRenderer<FlowchartModel>
{
    readonly ILayoutEngine _layoutEngine = layoutEngine ?? new DagreLayoutEngine();

    // FontAwesome icon pattern: fa:fa-icon-name or fab:fa-icon-name
    static readonly Regex IconPattern = new("(fa[bsr]?):fa-([a-z0-9-]+)", RegexOptions.Compiled);
    /// <summary>
    /// Run measurement, layout, edge routing, and skin resolution without generating SVG.
    /// Returns positioned model objects suitable for any rendering backend.
    /// </summary>
    public FlowchartLayoutResult LayoutModel(FlowchartModel model, RenderOptions options)
    {
        SecurityValidator.ValidateComplexity(model.Nodes.Count, model.Edges.Count, options);

        // Calculate node sizes based on text
        foreach (var node in model.Nodes)
        {
            // Use DisplayLabel which processes <br/> to \n and strips HTML tags
            var label = node.DisplayLabel;
            // Strip icon syntax for measurement
            var textForMeasure = IconPattern.Replace(label, "").Trim();
            var textSize = MeasureText(textForMeasure, options.FontSize);
            // Add extra width for icon if present
            var hasIcon = IconPattern.IsMatch(label);
            node.Width = textSize.Width + 30 + (hasIcon ? 20 : 0);
            node.Height = textSize.Height + 27;

            // Adjust size for different shapes
            if (node.Shape is NodeShape.Circle or NodeShape.DoubleCircle)
            {
                var diameter = Math.Max(node.Width, node.Height);
                node.Width = diameter;
                node.Height = diameter;
            }
            else if (node.Shape == NodeShape.Diamond)
            {
                node.Width *= 1.4;
                node.Height *= 1.4;
            }
        }

        var hasSubgraphs = model.Subgraphs.Count > 0;
        var subgraphCount = model.Subgraphs.Count;
        // Use tighter spacing for dense multi-subgraph diagrams to avoid excessive stretching
        var rankSep = subgraphCount >= 5 ? 60.0 : subgraphCount >= 3 ? 75.0 : 100.0;
        var nodeSep = subgraphCount >= 5 ? 50.0 : subgraphCount >= 3 ? 60.0 : 80.0;
        var layoutOptions = new LayoutOptions
        {
            Direction = model.Direction,
            NodeSeparation = nodeSep,
            RankSeparation = rankSep
        };
        _layoutEngine.Layout(model, layoutOptions);

        // Center each rank around the diagram's cross-axis midpoint
        if (!hasSubgraphs)
        {
            // Simple diagrams: center globally
            CenterRanks(model);
        }
        else
        {
            // Subgraph diagrams: center nodes within each subgraph
            CenterSubgraphRanks(model);
        }

        // Compute subgraph bounds first, then resolve any overlaps
        ComputeSubgraphBoundsAll(model);
        ResolveSubgraphOverlaps(model);

        // Recompute subgraph bounds after overlap resolution (nodes may have moved)
        ComputeSubgraphBoundsAll(model);

        // Center the entire diagram horizontally (subgraph overlap resolution can skew it)
        // MUST happen before edge routing so edges match final node positions
        if (hasSubgraphs)
            CenterRanks(model);

        // Recompute bounds after centering, then route edges on final positions
        ComputeSubgraphBoundsAll(model);
        var nodeById = model.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        GenerateOrthogonalEdgeRoutes(model, nodeById);

        // Final bounds computation for layout sizing
        ComputeSubgraphBoundsAll(model);

        // Recalculate layout bounds to include new edge routes
        var layoutResult = CalculateLayoutBounds(model);

        var skin = FlowchartSkin.Resolve(options.Theme, options.ThemeColors);

        return new FlowchartLayoutResult(model, skin, layoutResult.Width, layoutResult.Height, options.CurvedEdges);
    }

    public SvgDocument Render(FlowchartModel model, RenderOptions options)
    {
        var layout = LayoutModel(model, options);

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
            RenderEdge(builder, edge, layout.CurvedEdges, options);
        }

        // Render nodes
        foreach (var node in model.Nodes)
        {
            RenderNode(builder, node);
        }

        return builder.Build();
    }

    /// <summary>
    /// Cardinal port on a node boundary. Each port is a specific point
    /// (N=top-center, E=right-center, S=bottom-center, W=left-center).
    /// </summary>
    enum Port { North, East, South, West }

    /// <summary>
    /// Get the position of a named port on a node.
    /// </summary>
    static Position GetPortPosition(Node node, Port port) => port switch
    {
        Port.North => new(node.Position.X, node.Position.Y - node.Height / 2),
        Port.East => new(node.Position.X + node.Width / 2, node.Position.Y),
        Port.South => new(node.Position.X, node.Position.Y + node.Height / 2),
        Port.West => new(node.Position.X - node.Width / 2, node.Position.Y),
        _ => node.Position
    };

    /// <summary>
    /// Get the position of a port with an offset along the port's edge.
    /// For N/S ports: offset shifts in X. For E/W ports: offset shifts in Y.
    /// </summary>
    static Position GetPortPosition(Node node, Port port, double offset) => port switch
    {
        Port.North => new(node.Position.X + offset, node.Position.Y - node.Height / 2),
        Port.East => new(node.Position.X + node.Width / 2, node.Position.Y + offset),
        Port.South => new(node.Position.X + offset, node.Position.Y + node.Height / 2),
        Port.West => new(node.Position.X - node.Width / 2, node.Position.Y + offset),
        _ => node.Position
    };

    /// <summary>
    /// Determine the default exit and entry ports based on flow direction.
    /// </summary>
    static (Port Exit, Port Entry) GetDefaultPorts(Direction direction) => direction switch
    {
        Direction.TopToBottom => (Port.South, Port.North),
        Direction.BottomToTop => (Port.North, Port.South),
        Direction.LeftToRight => (Port.East, Port.West),
        Direction.RightToLeft => (Port.West, Port.East),
        _ => (Port.South, Port.North)
    };

    /// <summary>
    /// For a source with multiple outgoing edges, assign each edge to a specific exit port.
    /// Decision diamonds with 2 outputs use side ports (e.g., in TB: East + West).
    /// Other multi-output nodes route off-axis targets via side ports (North/South for LR,
    /// East/West for TB) to keep edges on the same side as their target and avoid wandering.
    /// Targets roughly inline with the source use the default exit with distributed offsets.
    /// </summary>
    static (Port port, double offset) AssignExitPort(Node source, Node target, Edge edge,
        List<Edge>? siblings, Direction direction, IReadOnlyDictionary<string, Node> nodeById)
    {
        var (defaultExit, _) = GetDefaultPorts(direction);

        if (siblings is null || siblings.Count <= 1)
            return (defaultExit, 0);

        // Decision diamonds with exactly 2 outputs → use side ports
        if (source.Shape == NodeShape.Diamond && siblings.Count == 2)
        {
            var edgeIndex = siblings.IndexOf(edge);
            if (edgeIndex < 0) return (defaultExit, 0);

            // Sort siblings by cross-axis target position
            var sorted = siblings.OrderBy(e =>
            {
                var t = nodeById.GetValueOrDefault(e.TargetId);
                if (t is null) return 0.0;
                return direction is Direction.LeftToRight or Direction.RightToLeft
                    ? t.Position.Y : t.Position.X;
            }).ToList();

            var sortedIndex = sorted.IndexOf(edge);

            // In TB/BT flow: first (left target) → West, second (right target) → East
            // In LR/RL flow: first (top target) → North, second (bottom target) → South
            return direction switch
            {
                Direction.TopToBottom => sortedIndex == 0 ? (Port.West, 0.0) : (Port.East, 0.0),
                Direction.BottomToTop => sortedIndex == 0 ? (Port.West, 0.0) : (Port.East, 0.0),
                Direction.LeftToRight => sortedIndex == 0 ? (Port.North, 0.0) : (Port.South, 0.0),
                Direction.RightToLeft => sortedIndex == 0 ? (Port.North, 0.0) : (Port.South, 0.0),
                _ => sortedIndex == 0 ? (Port.West, 0.0) : (Port.East, 0.0)
            };
        }

        // For regular nodes: ALL edges exit via the default port (East in LR, South in TB).
        // Distribute offsets across ALL siblings sorted by cross-axis target position.
        // This keeps edges flowing in the natural direction and avoids awkward side-port routes.
        var orderedSiblings = siblings.OrderBy(e =>
        {
            var t = nodeById.GetValueOrDefault(e.TargetId);
            if (t is null) return 0.0;
            return direction is Direction.LeftToRight or Direction.RightToLeft
                ? t.Position.Y : t.Position.X;
        }).ToList();

        var idx = orderedSiblings.IndexOf(edge);
        if (idx < 0) return (defaultExit, 0);

        var count = orderedSiblings.Count;
        if (count <= 1) return (defaultExit, 0);

        var crossAxisSize = defaultExit is Port.North or Port.South
            ? source.Width : source.Height;
        var usableRange = crossAxisSize * 0.6;
        var spacing = usableRange / Math.Max(count - 1, 1);
        var offset = -usableRange / 2 + idx * spacing;

        return (defaultExit, offset);
    }

    /// <summary>
    /// Distribute entry port offsets when multiple edges enter the same target node.
    /// </summary>
    static double AssignEntryOffset(Node target, Edge edge, List<Edge>? inSiblings, Port entryPort)
    {
        if (inSiblings is null || inSiblings.Count <= 1)
            return 0;

        // Diamond vertices are points, not flat edges — no room for offset distribution
        if (target.Shape == NodeShape.Diamond)
            return 0;

        var idx = inSiblings.IndexOf(edge);
        if (idx < 0) return 0;

        var count = inSiblings.Count;
        var crossAxisSize = entryPort is Port.North or Port.South
            ? target.Width : target.Height;
        var usableRange = crossAxisSize * 0.5;
        var spacing = usableRange / Math.Max(count - 1, 1);
        return -usableRange / 2 + idx * spacing;
    }

    /// <summary>
    /// Compute a channel offset for the Manhattan route midpoint so that sibling
    /// edges from the same source use different mid-channels instead of overlapping.
    /// </summary>
    static double ComputeChannelOffset(Edge edge, List<Edge>? siblings, Port exitPort)
    {
        if (siblings is null || siblings.Count <= 1)
            return 0;

        var idx = siblings.IndexOf(edge);
        if (idx < 0) return 0;

        var count = siblings.Count;
        const double channelSpacing = 20.0;
        return (idx - (count - 1) / 2.0) * channelSpacing;
    }

    /// <summary>
    /// Replace all edge points with clean orthogonal (H/V only) routes based on
    /// node positions and the diagram's flow direction. Uses stable named ports
    /// (N/E/S/W) to avoid jitter. Decision nodes branch from side ports.
    /// </summary>
    static void GenerateOrthogonalEdgeRoutes(FlowchartModel model,
        IReadOnlyDictionary<string, Node> nodeById)
    {
        // Pre-compute outgoing edges per node for port assignment
        var outgoingEdges = new Dictionary<string, List<Edge>>(StringComparer.Ordinal);
        foreach (var edge in model.Edges)
        {
            if (!outgoingEdges.TryGetValue(edge.SourceId, out var list))
            {
                list = [];
                outgoingEdges[edge.SourceId] = list;
            }
            list.Add(edge);
        }

        // Sort outgoing edges by target cross-axis position for consistent port assignment
        foreach (var (sourceId, edges) in outgoingEdges)
        {
            if (edges.Count <= 1) continue;
            if (!nodeById.TryGetValue(sourceId, out _)) continue;

            edges.Sort((a, b) =>
            {
                var tA = nodeById.GetValueOrDefault(a.TargetId);
                var tB = nodeById.GetValueOrDefault(b.TargetId);
                if (tA is null || tB is null) return 0;
                return model.Direction is Direction.LeftToRight or Direction.RightToLeft
                    ? tA.Position.Y.CompareTo(tB.Position.Y)
                    : tA.Position.X.CompareTo(tB.Position.X);
            });
        }

        // Pre-compute incoming edges per node for entry port distribution
        var incomingEdges = new Dictionary<string, List<Edge>>(StringComparer.Ordinal);
        foreach (var edge in model.Edges)
        {
            if (!incomingEdges.TryGetValue(edge.TargetId, out var list))
            {
                list = [];
                incomingEdges[edge.TargetId] = list;
            }
            list.Add(edge);
        }

        // Sort incoming edges by source cross-axis position for consistent port assignment
        foreach (var (targetId, edges) in incomingEdges)
        {
            if (edges.Count <= 1) continue;
            if (!nodeById.TryGetValue(targetId, out _)) continue;

            edges.Sort((a, b) =>
            {
                var sA = nodeById.GetValueOrDefault(a.SourceId);
                var sB = nodeById.GetValueOrDefault(b.SourceId);
                if (sA is null || sB is null) return 0;
                return model.Direction is Direction.LeftToRight or Direction.RightToLeft
                    ? sA.Position.Y.CompareTo(sB.Position.Y)
                    : sA.Position.X.CompareTo(sB.Position.X);
            });
        }

        foreach (var edge in model.Edges)
        {
            if (!nodeById.TryGetValue(edge.SourceId, out var source) ||
                !nodeById.TryGetValue(edge.TargetId, out var target))
            {
                continue;
            }

            edge.Points.Clear();

            if (source.Id == target.Id)
            {
                GenerateSelfLoop(edge, source, model.Direction);
                continue;
            }

            var siblings = outgoingEdges.GetValueOrDefault(source.Id);
            var inSiblings = incomingEdges.GetValueOrDefault(target.Id);
            var (_, defaultEntry) = GetDefaultPorts(model.Direction);
            var (exitPort, exitOffset) = AssignExitPort(
                source, target, edge, siblings, model.Direction, nodeById);

            var exitPos = GetPortPosition(source, exitPort, exitOffset);

            // Distribute entry ports when multiple edges enter the same node
            var entryOffset = AssignEntryOffset(target, edge, inSiblings, defaultEntry);
            var entryPos = GetPortPosition(target, defaultEntry, entryOffset);

            // Check for back-edge (target is "upstream")
            if (IsBackEdge(exitPort, exitPos, entryPos))
            {
                foreach (var pt in RouteBackEdge(source, target, exitPort, exitPos, entryPos))
                    edge.Points.Add(pt);
                continue;
            }

            // Compute channel offset so sibling edges don't share the same mid-channel
            var channelOffset = ComputeChannelOffset(edge, siblings, exitPort);

            // Manhattan route: exit → midpoint bend → entry
            foreach (var pt in ManhattanRoute(exitPos, entryPos, exitPort, defaultEntry, channelOffset))
                edge.Points.Add(pt);
        }

        // Post-process: reroute edges that pass through intermediate nodes
        AvoidNodeOverlaps(model, nodeById);
    }

    /// <summary>
    /// Detects edge segments that pass through non-source/non-target node bounds
    /// and adds detours around those obstacle nodes.
    /// </summary>
    static void AvoidNodeOverlaps(FlowchartModel model, IReadOnlyDictionary<string, Node> nodeById)
    {
        const double margin = 12;
        var allNodes = model.Nodes;

        foreach (var edge in model.Edges)
        {
            if (edge.Points.Count < 2) continue;

            // Check each segment for intersections with obstacle nodes
            var modified = false;
            List<Position> newPoints = [edge.Points[0]];

            for (var i = 0; i < edge.Points.Count - 1; i++)
            {
                var segStart = edge.Points[i];
                var segEnd = edge.Points[i + 1];

                Node? hitNode = null;
                foreach (var node in allNodes)
                {
                    if (node.Id == edge.SourceId || node.Id == edge.TargetId) continue;
                    if (SegmentIntersectsNodeBounds(segStart, segEnd, node, margin))
                    {
                        hitNode = node;
                        break;
                    }
                }

                if (hitNode is not null)
                {
                    // Route around the obstacle: pick the side with less deviation
                    var nodeTop = hitNode.Position.Y - hitNode.Height / 2 - margin;
                    var nodeBottom = hitNode.Position.Y + hitNode.Height / 2 + margin;
                    var nodeLeft = hitNode.Position.X - hitNode.Width / 2 - margin;
                    var nodeRight = hitNode.Position.X + hitNode.Width / 2 + margin;

                    var isHorizontalSeg = Math.Abs(segStart.Y - segEnd.Y) < 1;

                    if (isHorizontalSeg)
                    {
                        // Horizontal segment crosses a node — detour above or below
                        var distToTop = Math.Abs(segStart.Y - nodeTop);
                        var distToBottom = Math.Abs(segStart.Y - nodeBottom);
                        var detourY = distToTop <= distToBottom ? nodeTop : nodeBottom;

                        // Go to the approach X, bend to detour Y, pass the obstacle, bend back
                        newPoints.Add(new(nodeLeft, segStart.Y));
                        newPoints.Add(new(nodeLeft, detourY));
                        newPoints.Add(new(nodeRight, detourY));
                        newPoints.Add(new(nodeRight, segEnd.Y));
                    }
                    else
                    {
                        // Vertical segment crosses a node — detour left or right
                        var distToLeft = Math.Abs(segStart.X - nodeLeft);
                        var distToRight = Math.Abs(segStart.X - nodeRight);
                        var detourX = distToLeft <= distToRight ? nodeLeft : nodeRight;

                        newPoints.Add(new(segStart.X, nodeTop));
                        newPoints.Add(new(detourX, nodeTop));
                        newPoints.Add(new(detourX, nodeBottom));
                        newPoints.Add(new(segEnd.X, nodeBottom));
                    }

                    modified = true;
                }

                newPoints.Add(segEnd);
            }

            if (modified)
            {
                edge.Points.Clear();
                // Remove duplicate consecutive points
                Position prev = default;
                var first = true;
                foreach (var pt in newPoints)
                {
                    if (first || pt.DistanceTo(prev) > 0.5)
                    {
                        edge.Points.Add(pt);
                        prev = pt;
                        first = false;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks if a line segment intersects with a node's bounding box (expanded by margin).
    /// </summary>
    static bool SegmentIntersectsNodeBounds(Position a, Position b, Node node, double margin)
    {
        var left = node.Position.X - node.Width / 2 - margin / 2;
        var right = node.Position.X + node.Width / 2 + margin / 2;
        var top = node.Position.Y - node.Height / 2 - margin / 2;
        var bottom = node.Position.Y + node.Height / 2 + margin / 2;

        // Check if the segment's bounding box overlaps the node's bounds
        var segMinX = Math.Min(a.X, b.X);
        var segMaxX = Math.Max(a.X, b.X);
        var segMinY = Math.Min(a.Y, b.Y);
        var segMaxY = Math.Max(a.Y, b.Y);

        if (segMaxX < left || segMinX > right || segMaxY < top || segMinY > bottom)
            return false;

        // For axis-aligned segments (most of our routes), check actual crossing
        var isHorizontal = Math.Abs(a.Y - b.Y) < 1;
        var isVertical = Math.Abs(a.X - b.X) < 1;

        if (isHorizontal)
        {
            // Horizontal line at Y=a.Y — does it pass through the node's Y range
            // AND overlap the node's X range?
            return a.Y > top && a.Y < bottom &&
                   segMaxX > left && segMinX < right;
        }

        if (isVertical)
        {
            return a.X > left && a.X < right &&
                   segMaxY > top && segMinY < bottom;
        }

        // Diagonal — use line-rect intersection (rare in our orthogonal routing)
        return true; // Conservative: assume intersection
    }

    /// <summary>
    /// Is the target position "behind" the exit direction (i.e. a back-edge)?
    /// </summary>
    static bool IsBackEdge(Port exitPort, Position exitPos, Position entryPos) =>
        exitPort switch
        {
            Port.South => entryPos.Y < exitPos.Y + 1,
            Port.North => entryPos.Y > exitPos.Y - 1,
            Port.East => entryPos.X < exitPos.X + 1,
            Port.West => entryPos.X > exitPos.X - 1,
            _ => false
        };

    /// <summary>
    /// Produce an orthogonal Manhattan route between two port positions.
    /// Ensures the first segment leaves perpendicular to the exit face and
    /// the last segment arrives perpendicular to the entry face. When exit
    /// and entry axes differ (e.g. East→North), an extra bend is added.
    /// <paramref name="channelOffset"/> shifts the midpoint bend so sibling
    /// edges from the same source don't overlap.
    /// </summary>
    static List<Position> ManhattanRoute(Position exit, Position entry,
        Port exitPort, Port entryPort, double channelOffset = 0)
    {
        // Stub length for bends: 30% of gap, clamped
        const double minStub = 15.0;
        const double maxStub = 40.0;

        var exitVertical = exitPort is Port.South or Port.North;
        var entryVertical = entryPort is Port.South or Port.North;

        // Same axis (both vertical or both horizontal)
        if (exitVertical == entryVertical)
        {
            if (exitVertical)
            {
                // Both vertical — straight if perfectly X-aligned
                if (Math.Abs(exit.X - entry.X) < 1 && Math.Abs(channelOffset) < 1)
                    return [exit, entry];
                var gap = Math.Abs(entry.Y - exit.Y);
                var sign = exitPort == Port.South ? 1.0 : -1.0;
                var stub = Math.Clamp(gap * 0.3, minStub, maxStub);
                var midY = exit.Y + sign * stub + channelOffset;
                return [exit, new(exit.X, midY), new(entry.X, midY), entry];
            }
            else
            {
                // Both horizontal — straight if perfectly Y-aligned
                if (Math.Abs(exit.Y - entry.Y) < 1 && Math.Abs(channelOffset) < 1)
                    return [exit, entry];
                var gap = Math.Abs(entry.X - exit.X);
                var sign = exitPort == Port.East ? 1.0 : -1.0;
                var stub = Math.Clamp(gap * 0.3, minStub, maxStub);
                var midX = exit.X + sign * stub + channelOffset;
                return [exit, new(midX, exit.Y), new(midX, entry.Y), entry];
            }
        }

        // Cross-axis routes: ensure final segment matches entry port direction.
        // L-bend (3 points) when no offset; 5-point route when offset forces it.
        const double approachStub = 15.0;

        if (!exitVertical && entryVertical)
        {
            // Horizontal exit → vertical entry: final approach must be vertical
            if (Math.Abs(channelOffset) < 1)
            {
                // Simple L-bend: horizontal then vertical into entry
                return [exit, new(entry.X, exit.Y), entry];
            }
            // 5-point: horizontal → vertical → horizontal → vertical into entry
            var bendX = entry.X + channelOffset;
            var approachSign = entryPort == Port.North ? -1.0 : 1.0;
            var approachY = entry.Y + approachSign * approachStub;
            return [exit, new(bendX, exit.Y), new(bendX, approachY),
                    new(entry.X, approachY), entry];
        }

        // Vertical exit → horizontal entry: final approach must be horizontal
        {
            if (Math.Abs(channelOffset) < 1)
            {
                // Simple L-bend: vertical then horizontal into entry
                return [exit, new(exit.X, entry.Y), entry];
            }
            // 5-point: vertical → horizontal → vertical → horizontal into entry
            var bendY = entry.Y + channelOffset;
            var approachSign = entryPort == Port.West ? -1.0 : 1.0;
            var approachX = entry.X + approachSign * approachStub;
            return [exit, new(exit.X, bendY), new(approachX, bendY),
                    new(approachX, entry.Y), entry];
        }
    }

    /// <summary>
    /// Route a back-edge using the assigned exit port. Detours around both nodes
    /// to reach the entry side of the target.
    /// </summary>
    static List<Position> RouteBackEdge(Node source, Node target,
        Port exitPort, Position exitPos, Position entryPos)
    {
        const double gap = 20;

        var isVerticalExit = exitPort is Port.South or Port.North;

        if (isVerticalExit)
        {
            // Vertical exit → detour right, then vertical to entry
            var goingDown = exitPort == Port.South;
            var rightEdge = Math.Max(
                source.Position.X + source.Width / 2,
                target.Position.X + target.Width / 2) + gap;

            var extendY = goingDown ? exitPos.Y + gap : exitPos.Y - gap;
            var approachY = goingDown ? entryPos.Y - gap : entryPos.Y + gap;

            return
            [
                exitPos,
                new(exitPos.X, extendY),
                new(rightEdge, extendY),
                new(rightEdge, approachY),
                new(entryPos.X, approachY),
                entryPos
            ];
        }
        else
        {
            // Horizontal exit → detour below, then horizontal to entry
            var goingRight = exitPort == Port.East;
            var bottomEdge = Math.Max(
                source.Position.Y + source.Height / 2,
                target.Position.Y + target.Height / 2) + gap;

            var extendX = goingRight ? exitPos.X + gap : exitPos.X - gap;
            var approachX = goingRight ? entryPos.X - gap : entryPos.X + gap;

            return
            [
                exitPos,
                new(extendX, exitPos.Y),
                new(extendX, bottomEdge),
                new(approachX, bottomEdge),
                new(approachX, entryPos.Y),
                entryPos
            ];
        }
    }

    static void GenerateSelfLoop(Edge edge, Node node, Direction direction)
    {
        const double loopSize = 25;
        const double loopOffset = 15;

        var right = node.Position.X + node.Width / 2;
        var top = node.Position.Y - node.Height / 2;

        // Loop goes out the right side, up, and back in the top
        edge.Points.Add(new(right, node.Position.Y - loopOffset));
        edge.Points.Add(new(right + loopSize, node.Position.Y - loopOffset));
        edge.Points.Add(new(right + loopSize, top - loopSize));
        edge.Points.Add(new(node.Position.X + loopOffset, top - loopSize));
        edge.Points.Add(new(node.Position.X + loopOffset, top));
    }

    static void CenterRanks(FlowchartModel model) =>
        CenterNodesInRanks(model.Nodes, model.Direction);

    static void CenterSubgraphRanks(FlowchartModel model)
    {
        var nodeById = model.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var sg in model.Subgraphs)
            CenterSubgraphRanksRecursive(sg, nodeById, model.Direction);
    }

    static void CenterSubgraphRanksRecursive(Subgraph sg,
        IReadOnlyDictionary<string, Node> nodeById, Direction direction)
    {
        foreach (var nested in sg.NestedSubgraphs)
            CenterSubgraphRanksRecursive(nested, nodeById, direction);

        var nodes = new List<Node>();
        foreach (var nodeId in sg.NodeIds)
            if (nodeById.TryGetValue(nodeId, out var node))
                nodes.Add(node);

        CenterNodesInRanks(nodes, direction);
    }

    /// <summary>
    /// Group nodes into ranks (same layer in the flow direction) and center each rank
    /// around the cross-axis midpoint. Fixes fan-out patterns where the layout engine
    /// pushes source nodes to one side.
    /// </summary>
    static void CenterNodesInRanks(IReadOnlyList<Node> nodes, Direction direction)
    {
        if (nodes.Count <= 1) return;

        var isHorizontal = direction is Direction.LeftToRight or Direction.RightToLeft;

        // Group nodes into ranks by their primary-axis position
        const double rankTolerance = 5.0;
        var ranks = new List<List<Node>>();
        var sorted = nodes
            .OrderBy(n => isHorizontal ? n.Position.X : n.Position.Y)
            .ToList();

        List<Node>? currentRank = null;
        var currentRankPos = double.MinValue;

        foreach (var node in sorted)
        {
            var pos = isHorizontal ? node.Position.X : node.Position.Y;
            if (currentRank is null || Math.Abs(pos - currentRankPos) > rankTolerance)
            {
                currentRank = [node];
                ranks.Add(currentRank);
                currentRankPos = pos;
            }
            else
            {
                currentRank.Add(node);
            }
        }

        // Find cross-axis center of all nodes
        double allMin, allMax;
        if (isHorizontal)
        {
            allMin = nodes.Min(n => n.Position.Y - n.Height / 2);
            allMax = nodes.Max(n => n.Position.Y + n.Height / 2);
        }
        else
        {
            allMin = nodes.Min(n => n.Position.X - n.Width / 2);
            allMax = nodes.Max(n => n.Position.X + n.Width / 2);
        }
        var center = (allMin + allMax) / 2;

        // Center each rank
        foreach (var rank in ranks)
        {
            double rankMin, rankMax;
            if (isHorizontal)
            {
                rankMin = rank.Min(n => n.Position.Y - n.Height / 2);
                rankMax = rank.Max(n => n.Position.Y + n.Height / 2);
            }
            else
            {
                rankMin = rank.Min(n => n.Position.X - n.Width / 2);
                rankMax = rank.Max(n => n.Position.X + n.Width / 2);
            }

            var shift = center - (rankMin + rankMax) / 2;
            if (Math.Abs(shift) < 2) continue;

            foreach (var node in rank)
            {
                node.Position = isHorizontal
                    ? new(node.Position.X, node.Position.Y + shift)
                    : new(node.Position.X + shift, node.Position.Y);
            }
        }
    }

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

    static void RenderNode(SvgBuilder builder, Node node)
    {
        var x = node.Position.X - node.Width / 2;
        var y = node.Position.Y - node.Height / 2;

        var shapePath = ShapePathGenerator.GetPath(node.Shape, x, y, node.Width, node.Height);
        var shapeClass = GetShapeClass(node.Shape);

        // Apply inline style from style directives if present
        builder.AddPath(shapePath,
            fill: node.Style.Fill,
            stroke: node.Style.Stroke,
            strokeWidth: node.Style.StrokeWidth,
            strokeDasharray: node.Style.StrokeDasharray,
            cssClass: $"flow-node flow-node-shape {shapeClass}");

        // Render label as SVG text (centered in node)
        var label = node.DisplayLabel;
        // Strip icon syntax for display (icons require foreignObject/HTML which Avalonia can't render)
        var displayLabel = IconPattern.Replace(label, "").Trim();
        var centerX = node.Position.X;
        var centerY = node.Position.Y;

        var lines = displayLabel.Split('\n');
        if (lines.Length <= 1)
        {
            builder.AddText(
                centerX, centerY,
                displayLabel,
                anchor: "middle",
                baseline: "central",
                cssClass: "flow-node-label");
        }
        else
        {
            // Multi-line: use tspan elements, vertically centered
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

    static void RenderEdge(SvgBuilder builder, Edge edge, bool curved, RenderOptions options)
    {
        if (edge.Points.Count < 2)
        {
            return;
        }

        var points = edge.Points;
        var pathData = $"M{Fmt(points[0].X)},{Fmt(points[0].Y)}";

        if (points.Count == 2)
        {
            pathData += $" L{Fmt(points[1].X)},{Fmt(points[1].Y)}";
        }
        else if (curved)
        {
            // Round each 90° corner with a quadratic Bezier: line to (corner - radius),
            // Q-curve through corner to (corner + radius). Matches mermaid.js style.
            const double radius = 8.0;

            for (var i = 1; i < points.Count - 1; i++)
            {
                var prev = points[i - 1];
                var corner = points[i];
                var next = points[i + 1];

                // Vector from corner back toward previous point
                var dxIn = prev.X - corner.X;
                var dyIn = prev.Y - corner.Y;
                var lenIn = Math.Sqrt(dxIn * dxIn + dyIn * dyIn);

                // Vector from corner toward next point
                var dxOut = next.X - corner.X;
                var dyOut = next.Y - corner.Y;
                var lenOut = Math.Sqrt(dxOut * dxOut + dyOut * dyOut);

                var r = Math.Min(radius, Math.Min(lenIn / 2, lenOut / 2));
                if (r < 0.5)
                {
                    pathData += $" L{Fmt(corner.X)},{Fmt(corner.Y)}";
                    continue;
                }

                // Points just before and after the corner
                var beforeX = corner.X + r * dxIn / lenIn;
                var beforeY = corner.Y + r * dyIn / lenIn;
                var afterX = corner.X + r * dxOut / lenOut;
                var afterY = corner.Y + r * dyOut / lenOut;

                pathData += $" L{Fmt(beforeX)},{Fmt(beforeY)}";
                pathData += $" Q{Fmt(corner.X)},{Fmt(corner.Y)} {Fmt(afterX)},{Fmt(afterY)}";
            }

            // Final segment to last point
            pathData += $" L{Fmt(points[^1].X)},{Fmt(points[^1].Y)}";
        }
        else
        {
            for (var i = 1; i < points.Count; i++)
            {
                pathData += $" L{Fmt(points[i].X)},{Fmt(points[i].Y)}";
            }
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
    /// Detect overlapping sibling subgraphs and push their member nodes apart.
    /// Works adaptively — only adjusts when subgraphs actually overlap.
    /// Iterates until convergence (max 10 passes) to handle cascade effects.
    /// </summary>
    static void ResolveSubgraphOverlaps(FlowchartModel model)
    {
        if (model.Subgraphs.Count < 2) return;

        var nodeById = model.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var minGap = model.Subgraphs.Count >= 5 ? 12.0 : 20.0;
        const int maxPasses = 10;

        var subgraphs = model.Subgraphs
            .Where(sg => sg.Width > 0 && sg.Height > 0)
            .ToList();

        // For TB/BT flow, prefer pushing vertically to maintain top-to-bottom ordering.
        // For LR/RL flow, prefer pushing horizontally.
        var preferVertical = model.Direction is Direction.TopToBottom or Direction.BottomToTop;

        // Iterate until no overlaps remain or max passes reached
        for (var pass = 0; pass < maxPasses; pass++)
        {
            var anyFixed = false;

            // Check all pairs (O(n^2) but n is small for subgraphs)
            for (var i = 0; i < subgraphs.Count; i++)
            {
                for (var j = i + 1; j < subgraphs.Count; j++)
                {
                    var a = subgraphs[i];
                    var b = subgraphs[j];

                    if (!a.Bounds.Intersects(b.Bounds)) continue;

                    // Compute overlap amounts in both axes
                    var overlapX = Math.Min(a.Bounds.Right, b.Bounds.Right) - Math.Max(a.Bounds.Left, b.Bounds.Left);
                    var overlapY = Math.Min(a.Bounds.Bottom, b.Bounds.Bottom) - Math.Max(a.Bounds.Top, b.Bounds.Top);

                    if (overlapX <= 0 || overlapY <= 0) continue;

                    // Push apart respecting flow direction, but allow horizontal push
                    // when subgraphs are at similar Y positions (same logical rank).
                    // Use the axis with less overlap — resolves with minimal displacement.
                    bool pushVertically;
                    if (preferVertical)
                    {
                        // For TB: push horizontally if the X overlap is smaller (cheaper fix),
                        // or if subgraphs have large Y overlap (they're at the same rank level)
                        pushVertically = overlapY < overlapX;
                    }
                    else
                    {
                        pushVertically = overlapX >= overlapY;
                    }

                    if (!pushVertically)
                    {
                        var pushX = overlapX + minGap;
                        if (a.Bounds.Center.X <= b.Bounds.Center.X)
                        {
                            ShiftSubgraphNodes(b, nodeById, pushX, 0);
                            RecomputeSingleSubgraphBounds(b, nodeById);
                        }
                        else
                        {
                            ShiftSubgraphNodes(a, nodeById, pushX, 0);
                            RecomputeSingleSubgraphBounds(a, nodeById);
                        }
                    }
                    else
                    {
                        var pushY = overlapY + minGap;
                        if (a.Bounds.Center.Y <= b.Bounds.Center.Y)
                        {
                            ShiftSubgraphNodes(b, nodeById, 0, pushY);
                            RecomputeSingleSubgraphBounds(b, nodeById);
                        }
                        else
                        {
                            ShiftSubgraphNodes(a, nodeById, 0, pushY);
                            RecomputeSingleSubgraphBounds(a, nodeById);
                        }
                    }

                    anyFixed = true;
                }
            }

            if (!anyFixed) break;
        }

        // Also resolve overlaps between subgraph bounds and free (non-subgraph) nodes
        var allSubgraphNodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sg in model.Subgraphs)
            CollectAllNodeIds(sg, allSubgraphNodeIds);

        foreach (var sg in subgraphs)
        {
            foreach (var node in model.Nodes)
            {
                if (allSubgraphNodeIds.Contains(node.Id)) continue;

                var sgBounds = sg.Bounds;
                var nodeBounds = node.Bounds;
                if (!sgBounds.Intersects(nodeBounds)) continue;

                // Push node away from subgraph center
                var dx = node.Position.X - sgBounds.Center.X;
                var dy = node.Position.Y - sgBounds.Center.Y;

                if (Math.Abs(dx) > Math.Abs(dy))
                {
                    // Push horizontally
                    node.Position = dx >= 0
                        ? new(sgBounds.Right + node.Width / 2 + minGap, node.Position.Y)
                        : new(sgBounds.Left - node.Width / 2 - minGap, node.Position.Y);
                }
                else
                {
                    // Push vertically
                    node.Position = dy >= 0
                        ? new(node.Position.X, sgBounds.Bottom + node.Height / 2 + minGap)
                        : new(node.Position.X, sgBounds.Top - node.Height / 2 - minGap);
                }
            }
        }
    }

    static void CollectAllNodeIds(Subgraph sg, HashSet<string> ids)
    {
        foreach (var nodeId in sg.NodeIds) ids.Add(nodeId);
        foreach (var nested in sg.NestedSubgraphs) CollectAllNodeIds(nested, ids);
    }

    static void ShiftSubgraphNodes(Subgraph sg, IReadOnlyDictionary<string, Node> nodeById, double dx, double dy)
    {
        foreach (var nodeId in sg.NodeIds)
        {
            if (nodeById.TryGetValue(nodeId, out var node))
                node.Position = new(node.Position.X + dx, node.Position.Y + dy);
        }
        foreach (var nested in sg.NestedSubgraphs)
            ShiftSubgraphNodes(nested, nodeById, dx, dy);
    }

    static void RecomputeSingleSubgraphBounds(Subgraph sg, IReadOnlyDictionary<string, Node> nodeById)
    {
        foreach (var nested in sg.NestedSubgraphs)
            RecomputeSingleSubgraphBounds(nested, nodeById);

        var nestedBounds = new Dictionary<string, ModelRect>(StringComparer.Ordinal);
        foreach (var nested in sg.NestedSubgraphs)
            if (nested.Width > 0 && nested.Height > 0)
                nestedBounds[nested.Id] = nested.Bounds;

        var bounds = ComputeSubgraphBounds(sg, nodeById, nestedBounds);
        if (bounds is null) return;

        sg.Position = bounds.Value.Center;
        sg.Width = bounds.Value.Width;
        sg.Height = bounds.Value.Height;
    }

    /// <summary>
    /// Compute bounds for all subgraphs (leaf to root) and store on each Subgraph's Position/Width/Height.
    /// Called during layout so that native renderers have access to subgraph geometry.
    /// </summary>
    static void ComputeSubgraphBoundsAll(FlowchartModel model)
    {
        if (model.Subgraphs.Count == 0)
        {
            return;
        }

        var nodeById = model.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var flattened = new List<(Subgraph Subgraph, int Depth)>();
        foreach (var subgraph in model.Subgraphs)
        {
            FlattenSubgraphs(subgraph, 0, flattened);
        }

        var computedBounds = new Dictionary<string, ModelRect>(StringComparer.Ordinal);
        foreach (var (subgraph, _) in flattened.OrderByDescending(x => x.Depth))
        {
            var bounds = ComputeSubgraphBounds(subgraph, nodeById, computedBounds);
            if (bounds is null)
            {
                continue;
            }

            computedBounds[subgraph.Id] = bounds.Value;
            subgraph.Position = bounds.Value.Center;
            subgraph.Width = bounds.Value.Width;
            subgraph.Height = bounds.Value.Height;
        }
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

    const double SubgraphPadX = 24;
    const double SubgraphPadTop = 32;
    const double SubgraphPadBottom = 18;

    static ModelRect? ComputeSubgraphBounds(Subgraph subgraph,
        IReadOnlyDictionary<string, Node> nodeById,
        IReadOnlyDictionary<string, ModelRect> nestedBounds)
    {
        var entries = new List<ModelRect>();

        foreach (var nodeId in subgraph.NodeIds)
            if (nodeById.TryGetValue(nodeId, out var node))
                entries.Add(node.Bounds);

        foreach (var nestedSubgraph in subgraph.NestedSubgraphs)
            if (nestedBounds.TryGetValue(nestedSubgraph.Id, out var nested))
                entries.Add(nested);

        if (entries.Count == 0) return null;

        var left = entries.Min(r => r.Left) - SubgraphPadX;
        var right = entries.Max(r => r.Right) + SubgraphPadX;
        var top = entries.Min(r => r.Top) - SubgraphPadTop;
        var bottom = entries.Max(r => r.Bottom) + SubgraphPadBottom;
        return new ModelRect(left, top, right - left, bottom - top);
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
                stroke-width:1.5px;
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

}
