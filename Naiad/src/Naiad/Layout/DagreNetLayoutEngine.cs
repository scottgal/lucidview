using Mostlylucid.Dagre;
using MermaidSharp.Models;
using MermaidSharp.Rendering;

namespace MermaidSharp.Layout;

/// <summary>
/// Layout engine that delegates to Mostlylucid.Dagre (C# port of the dagre JS library).
/// Uses the full dagre pipeline: network simplex ranking, barycenter ordering,
/// Brandes-Kopf coordinate assignment, and intersectRect edge routing.
/// </summary>
public class DagreNetLayoutEngine : ILayoutEngine
{
    public double FontSize { get; set; } = 16;

    public LayoutResult Layout(GraphDiagramBase diagram, LayoutOptions options)
    {
        if (diagram.Nodes.Count == 0)
            return new() { Width = 0, Height = 0 };

        // Build a compound DagreGraph (required by dagre's nestingGraph module)
        // Must also be multigraph so acyclic module can reverse back-edges with unique names
        var dg = new DagreGraph(true) { _isMultigraph = true };
        var isVertical = options.Direction is Direction.TopToBottom or Direction.BottomToTop;

        // Index: map node IDs to sequential string keys for dagre
        var nodeList = new List<(string Key, Node Node)>();
        var keyByNodeId = new Dictionary<string, string>(StringComparer.Ordinal);

        // Add nodes
        var nextKey = 0;
        foreach (var node in diagram.Nodes)
        {
            var key = (nextKey++).ToString();
            keyByNodeId[node.Id] = key;
            nodeList.Add((key, node));

            var nd = new NodeLabel
            {
                ["source"] = node,
                ["width"] = (float)node.Width,
                ["height"] = (float)node.Height
            };
            dg.SetNode(key, nd);
        }

        // Register subgraphs as compound parent nodes so dagre accounts for them
        var subgraphKeyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        RegisterSubgraphs(diagram.Subgraphs, null, dg, keyByNodeId, subgraphKeyMap, ref nextKey);

        // Add edges
        var addedEdges = new HashSet<(string, string)>();
        foreach (var edge in diagram.Edges)
        {
            if (edge.SourceId == edge.TargetId) continue;
            if (!keyByNodeId.TryGetValue(edge.SourceId, out var srcKey) ||
                !keyByNodeId.TryGetValue(edge.TargetId, out var tgtKey))
                continue;
            if (!addedEdges.Add((srcKey, tgtKey))) continue;

            // Measure edge label dimensions so dagre allocates space between ranks.
            // Without this, edges with labels ("Yes"/"No") get no extra vertical spacing,
            // causing the layout to appear vertically compressed vs mermaid.js.
            var labelWidth = 0.0;
            var labelHeight = 0.0;
            if (!string.IsNullOrEmpty(edge.Label))
            {
                var labelSize = RenderUtils.MeasureTextBlock(edge.Label, FontSize * 0.9);
                labelWidth = labelSize.Width + 16; // match FlowchartRenderer label box padding
                labelHeight = labelSize.Height + 8;
            }

            var edgeObj = new EdgeLabel
            {
                ["minlen"] = 1,
                ["weight"] = 1,
                ["width"] = (float)labelWidth,
                ["height"] = (float)labelHeight,
                ["labeloffset"] = 10,
                ["labelpos"] = "r",
                ["source"] = edge
            };

            try
            {
                dg.SetEdge(srcKey, tgtKey, edgeObj);
            }
            catch
            {
                // Skip edges dagre can't handle
            }
        }

        // Set graph options - match mermaid.js defaults
        var graphLabel = dg.Graph();
        graphLabel.RankSep = (int)options.RankSeparation;
        graphLabel.EdgeSep = (int)options.EdgeSeparation;
        graphLabel.NodeSep = (int)options.NodeSeparation;
        graphLabel.RankDir = isVertical ? "tb" : "lr";

        // Run dagre layout
        try
        {
            Mostlylucid.Dagre.Indexed.IndexedDagreLayout.RunLayout(dg);
        }
        catch (Exception ex)
        {
            throw new MermaidException($"Mostlylucid.Dagre layout failed: {ex.Message}");
        }

        // Read node positions back
        foreach (var (key, node) in nodeList)
        {
            var graphNode = dg.Node(key);
            if (graphNode == null) continue;
            double x = graphNode.X;
            double y = graphNode.Y;
            node.Position = new(x, y);
        }

        // Pre-build edge lookup from dagre output: (srcKey, tgtKey) → edge data
        // This avoids O(E²) iteration when matching diagram edges to dagre output
        var dagreEdgeLookup = new Dictionary<(string, string), EdgeLabel>();
        try
        {
            foreach (var e in dg.Edges())
            {
                var edgeData = dg.Edge(e);
                if (edgeData?.Points != null)
                    dagreEdgeLookup[(e.v, e.w)] = edgeData;
            }
        }
        catch
        {
            // dagre may throw on malformed graphs
        }

        // Read edge points back and clip endpoints to node boundaries
        foreach (var edge in diagram.Edges)
        {
            edge.Points.Clear();

            if (edge.SourceId == edge.TargetId)
            {
                var node = diagram.GetNode(edge.SourceId);
                if (node != null)
                {
                    var cx = node.Position.X;
                    var cy = node.Position.Y;
                    var w = node.Width / 2;
                    var h = node.Height / 2;
                    edge.Points.Add(new(cx + w, cy));
                    edge.Points.Add(new(cx + w + 30, cy - h - 10));
                    edge.Points.Add(new(cx + w + 30, cy - h - 30));
                    edge.Points.Add(new(cx, cy - h));
                }
                continue;
            }

            if (!keyByNodeId.TryGetValue(edge.SourceId, out var eSrcKey) ||
                !keyByNodeId.TryGetValue(edge.TargetId, out var eTgtKey))
                continue;

            // O(1) lookup instead of O(E) scan
            if (dagreEdgeLookup.TryGetValue((eSrcKey, eTgtKey), out var edgeLabel))
            {
                foreach (var pt in edgeLabel.Points)
                    edge.Points.Add(new(pt.X, pt.Y));
            }

            if (edge.Points.Count == 0)
            {
                var srcNode = diagram.GetNode(edge.SourceId);
                var tgtNode = diagram.GetNode(edge.TargetId);
                if (srcNode != null && tgtNode != null)
                {
                    edge.Points.Add(new(srcNode.Position.X, srcNode.Position.Y));
                    edge.Points.Add(new(tgtNode.Position.X, tgtNode.Position.Y));
                }
            }
        }

        // Read subgraph bounds from dagre's compound layout output
        // (must happen before edge routing so we know subgraph boundaries)
        ReadSubgraphBounds(diagram, dg, subgraphKeyMap);

        // Distribute multiple edges on the same node side evenly along the border.
        // Must run BEFORE ClipEdgeEndpoints so that distributed points on rectangular
        // bounds can be projected onto actual shape boundaries (diamond, circle, etc.)
        DistributeEdgePorts(diagram);

        // Clip edge endpoints to node shape boundaries using universal geometry.
        // For non-rectangular shapes, casts ray from node center through each
        // distributed point to find the actual boundary intersection.
        ClipEdgeEndpoints(diagram);

        // Merge near-collinear edge segments to eliminate micro-bends from dagre waypoints
        SmoothEdgePaths(diagram);

        // Route edges around subgraph boundaries they shouldn't cross through
        RouteAroundSubgraphs(diagram);

        // Pull back edge endpoints to leave room for arrowhead markers
        // (markers extend ~5px past the path endpoint; nodes are drawn on top of edges)
        PullBackArrowEndpoints(diagram);

        // Handle direction reversal for BT
        if (options.Direction == Direction.BottomToTop)
        {
            var maxY = diagram.Nodes.Max(n => n.Position.Y + n.Height / 2);
            foreach (var node in diagram.Nodes)
                node.Position = new(node.Position.X, maxY - node.Position.Y);
            foreach (var edge in diagram.Edges)
            {
                for (var i = 0; i < edge.Points.Count; i++)
                    edge.Points[i] = new(edge.Points[i].X, maxY - edge.Points[i].Y);
            }
        }

        // Calculate total bounds (include edge points for back-edges that extend beyond nodes)
        var boundsMaxX = 0.0;
        var boundsMaxY = 0.0;
        foreach (var node in diagram.Nodes)
        {
            var nx = node.Position.X + node.Width / 2;
            var ny = node.Position.Y + node.Height / 2;
            if (nx > boundsMaxX) boundsMaxX = nx;
            if (ny > boundsMaxY) boundsMaxY = ny;
        }
        foreach (var edge in diagram.Edges)
        {
            foreach (var pt in edge.Points)
            {
                if (pt.X > boundsMaxX) boundsMaxX = pt.X;
                if (pt.Y > boundsMaxY) boundsMaxY = pt.Y;
            }
        }
        var width = boundsMaxX;
        var height = boundsMaxY;

        return new() { Width = width, Height = height };
    }

    /// <summary>
    /// Clip edge endpoints to non-rectangular node shape boundaries.
    /// Dagre already clips edges to rectangle boundaries via intersectRect.
    /// We only need to re-clip for shapes that aren't rectangles (diamond, circle, etc.)
    /// by casting a ray from the node center through dagre's endpoint to find the
    /// actual shape boundary intersection.
    /// </summary>
    static void ClipEdgeEndpoints(GraphDiagramBase diagram)
    {
        // Cache segments per node to avoid recomputing geometry for nodes with multiple edges
        var segmentCache = new Dictionary<string, List<PathSegment>?>(StringComparer.Ordinal);

        foreach (var edge in diagram.Edges)
        {
            if (edge.Points.Count < 2 || edge.SourceId == edge.TargetId) continue;

            // Re-clip source endpoint for non-rect shapes
            var srcNode = diagram.GetNode(edge.SourceId);
            if (srcNode is not null && !IsRectangularShape(srcNode.Shape))
            {
                var clipped = ClipPointToShape(srcNode, edge.Points[0], segmentCache);
                if (clipped is not null)
                    edge.Points[0] = new(clipped.Value.X, clipped.Value.Y);
            }

            // Re-clip target endpoint for non-rect shapes
            var tgtNode = diagram.GetNode(edge.TargetId);
            if (tgtNode is not null && !IsRectangularShape(tgtNode.Shape))
            {
                var clipped = ClipPointToShape(tgtNode, edge.Points[^1], segmentCache);
                if (clipped is not null)
                    edge.Points[^1] = new(clipped.Value.X, clipped.Value.Y);
            }
        }
    }

    static bool IsRectangularShape(NodeShape shape) =>
        shape is NodeShape.Rectangle or NodeShape.RoundedRectangle
            or NodeShape.Subroutine or NodeShape.Cylinder;

    /// <summary>
    /// Cast a ray from the node center through the given point and return where
    /// it intersects the node's shape boundary. Returns null if no intersection.
    /// </summary>
    static PointD? ClipPointToShape(Node node, Position point,
        Dictionary<string, List<PathSegment>?>? segmentCache = null)
    {
        List<PathSegment>? segments;
        if (segmentCache is not null)
        {
            if (!segmentCache.TryGetValue(node.Id, out segments))
            {
                segments = GetNodeSegments(node);
                segmentCache[node.Id] = segments.Count > 0 ? segments : null;
            }
        }
        else
        {
            segments = GetNodeSegments(node);
        }

        if (segments is null || segments.Count == 0) return null;

        var center = new PointD(node.Position.X, node.Position.Y);
        var dir = new PointD(point.X - center.X, point.Y - center.Y);

        // Avoid zero-length direction — point is at node center
        if (Math.Abs(dir.X) < 0.01 && Math.Abs(dir.Y) < 0.01)
        {
            // Default to bottom for vertical layouts
            dir = new PointD(0, 1);
        }

        return ShapeGeometry.IntersectRay(segments, center, dir);
    }

    /// <summary>
    /// Get parsed shape segments for a node, scaled to the node's actual position and size.
    /// Uses ShapePathGenerator for built-in shapes (no RenderOptions needed).
    /// </summary>
    internal static List<PathSegment> GetNodeSegments(Node node)
    {
        const double refSize = 100;
        var pathData = ShapePathGenerator.GetPath(node.Shape, 0, 0, refSize, refSize);
        var segments = SvgPathExtractor.ExtractOuterContour(pathData);
        if (segments.Count == 0)
        {
            // Fallback to rectangle
            segments =
            [
                PathSegment.Line(0, 0, refSize, 0),
                PathSegment.Line(refSize, 0, refSize, refSize),
                PathSegment.Line(refSize, refSize, 0, refSize),
                PathSegment.Line(0, refSize, 0, 0)
            ];
        }

        return ShapeGeometry.ScaleToNode(
            segments, refSize, refSize,
            node.Position.X, node.Position.Y, node.Width, node.Height);
    }

    /// <summary>
    /// Register subgraphs as compound parent nodes in dagre so it accounts for
    /// subgraph boundaries during layout (spacing, border nodes, etc.).
    /// </summary>
    static void RegisterSubgraphs(
        IEnumerable<Subgraph> subgraphs, string? parentKey,
        DagreGraph dg, Dictionary<string, string> keyByNodeId,
        Dictionary<string, string> subgraphKeyMap, ref int nextKey)
    {
        var stack = new Stack<(IEnumerable<Subgraph> subgraphs, string? parentKey)>();
        stack.Push((subgraphs, parentKey));
        
        while (stack.Count > 0)
        {
            var (currentSubgraphs, currentParentKey) = stack.Pop();
            
            foreach (var sg in currentSubgraphs)
            {
                var sgKey = nextKey.ToString();
                nextKey++;
                subgraphKeyMap[sg.Id] = sgKey;

                var sgLabel = new NodeLabel
                {
                    IsGroup = true,
                    Width = 0f,
                    Height = 0f
                };
                dg.SetNode(sgKey, sgLabel);

                if (currentParentKey != null)
                    TrySetParent(dg, sgKey, currentParentKey);

                foreach (var nodeId in sg.NodeIds)
                {
                    if (keyByNodeId.TryGetValue(nodeId, out var nodeKey))
                        TrySetParent(dg, nodeKey, sgKey);
                }

                if (sg.NestedSubgraphs.Count > 0)
                    stack.Push((sg.NestedSubgraphs, sgKey));
            }
        }
    }

    static void TrySetParent(DagreGraph graph, string childKey, string parentKey)
    {
        try
        {
            graph.SetParent(childKey, parentKey);
        }
        catch
        {
            // Ignore unsupported parent assignment in dagre forks.
        }
    }

    /// <summary>
    /// Read subgraph bounds from dagre's compound layout output.
    /// Dagre computes parent node bounds from border nodes in removeBorderNodes().
    /// Add extra top padding for subgraph titles.
    /// </summary>
    static void ReadSubgraphBounds(
        GraphDiagramBase diagram, DagreGraph dg,
        Dictionary<string, string> subgraphKeyMap)
    {
        // Build flat lookup of subgraphs by ID
        var sgById = new Dictionary<string, Subgraph>(StringComparer.Ordinal);
        FlattenSubgraphs(diagram.Subgraphs, sgById);

        const double titlePadding = 28.0; // Extra top space for title text
        const double extraPadding = 10.0; // Extra padding around content

        foreach (var (sgId, sgKey) in subgraphKeyMap)
        {
            var sgNode = dg.Node(sgKey);
            if (sgNode == null) continue;
            if (!sgById.TryGetValue(sgId, out var sg)) continue;

            var hasTitle = !string.IsNullOrWhiteSpace(sg.Title);
            var topExtra = hasTitle ? titlePadding : extraPadding;

            sg.Width = sgNode.Width + extraPadding * 2;
            sg.Height = sgNode.Height + topExtra + extraPadding;
            sg.Position = new(sgNode.X, sgNode.Y + (topExtra - extraPadding) / 2);

            // Label collision avoidance: if any child node overlaps with the title area,
            // push the subgraph top boundary up to avoid overlap
            if (hasTitle)
            {
                var sgTop = sg.Position.Y - sg.Height / 2;
                var titleBottom = sgTop + titlePadding;
                var minNodeTop = double.MaxValue;

                foreach (var nodeId in sg.NodeIds)
                {
                    var node = diagram.GetNode(nodeId);
                    if (node is null) continue;
                    var nodeTop = node.Position.Y - node.Height / 2;
                    if (nodeTop < minNodeTop) minNodeTop = nodeTop;
                }

                if (minNodeTop < double.MaxValue && minNodeTop < titleBottom + 4)
                {
                    // Push subgraph top up by the overlap amount + small gap
                    var overlap = titleBottom - minNodeTop + 8;
                    sg.Height += overlap;
                    sg.Position = new(sg.Position.X, sg.Position.Y - overlap / 2);
                }
            }
        }
    }

    static void FlattenSubgraphs(IEnumerable<Subgraph> subgraphs, Dictionary<string, Subgraph> result)
    {
        var stack = new Stack<IEnumerable<Subgraph>>();
        stack.Push(subgraphs);
        
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var sg in current)
            {
                result[sg.Id] = sg;
                if (sg.NestedSubgraphs.Count > 0)
                    stack.Push(sg.NestedSubgraphs);
            }
        }
    }

    /// <summary>
    /// Distribute multiple edges sharing the same node side evenly along the border.
    /// Dagre's intersectRect aims all edges at the node center, so multiple edges
    /// converge to nearly the same point. This spreads them out for cleaner visuals.
    /// </summary>
    static void DistributeEdgePorts(GraphDiagramBase diagram)
    {
        // Build lookup: for each node, collect (edge index, isSource, attachmentPoint)
        // Group by node + side, then redistribute
        var nodeEdges = new Dictionary<string, List<(Edge Edge, bool IsSource)>>(StringComparer.Ordinal);

        foreach (var edge in diagram.Edges)
        {
            if (edge.Points.Count < 2 || edge.SourceId == edge.TargetId) continue;

            if (!nodeEdges.TryGetValue(edge.SourceId, out var srcList))
            {
                srcList = [];
                nodeEdges[edge.SourceId] = srcList;
            }
            srcList.Add((edge, true));

            if (!nodeEdges.TryGetValue(edge.TargetId, out var tgtList))
            {
                tgtList = [];
                nodeEdges[edge.TargetId] = tgtList;
            }
            tgtList.Add((edge, false));
        }

        foreach (var (nodeId, edges) in nodeEdges)
        {
            if (edges.Count < 2) continue;
            var node = diagram.GetNode(nodeId);
            if (node is null) continue;

            // Group edges by which side of the node they attach to
            var groups = new Dictionary<Side, List<(Edge Edge, bool IsSource)>>();
            foreach (var (edge, isSource) in edges)
            {
                var pt = isSource ? edge.Points[0] : edge.Points[^1];
                var side = ClassifySide(node, pt);
                if (!groups.TryGetValue(side, out var list))
                {
                    list = [];
                    groups[side] = list;
                }
                list.Add((edge, isSource));
            }

            // For each side with multiple edges, distribute evenly
            foreach (var (side, group) in groups)
            {
                if (group.Count < 2) continue;
                DistributeOnSide(node, side, group);
            }
        }
    }

    enum Side { Top, Bottom, Left, Right }

    static Side ClassifySide(Node node, Position pt)
    {
        var cx = node.Position.X;
        var cy = node.Position.Y;
        var hw = node.Width / 2;
        var hh = node.Height / 2;

        // Compute normalized distance to each side
        var dx = pt.X - cx;
        var dy = pt.Y - cy;

        // Compare aspect-ratio-normalized distances to determine dominant side
        var normX = hw > 0 ? Math.Abs(dx) / hw : 0;
        var normY = hh > 0 ? Math.Abs(dy) / hh : 0;

        if (normY >= normX)
            return dy <= 0 ? Side.Top : Side.Bottom;
        return dx <= 0 ? Side.Left : Side.Right;
    }

    static void DistributeOnSide(Node node, Side side, List<(Edge Edge, bool IsSource)> group)
    {
        var cx = node.Position.X;
        var cy = node.Position.Y;
        var hw = node.Width / 2;
        var hh = node.Height / 2;

        // Sort edges by the angle of their next/previous waypoint relative to center,
        // projected onto the side axis (left-to-right for top/bottom, top-to-bottom for left/right)
        group.Sort((a, b) =>
        {
            var ptA = GetAdjacentWaypoint(a.Edge, a.IsSource);
            var ptB = GetAdjacentWaypoint(b.Edge, b.IsSource);
            return side switch
            {
                Side.Top or Side.Bottom => ptA.X.CompareTo(ptB.X),
                _ => ptA.Y.CompareTo(ptB.Y)
            };
        });

        var isNonRect = !IsRectangularShape(node.Shape);
        var count = group.Count;
        for (var i = 0; i < count; i++)
        {
            var (edge, isSource) = group[i];
            var t = (i + 1.0) / (count + 1.0); // Even distribution [0..1]

            var newPt = side switch
            {
                Side.Top => new Position(cx - hw + t * node.Width, cy - hh),
                Side.Bottom => new Position(cx - hw + t * node.Width, cy + hh),
                Side.Left => new Position(cx - hw, cy - hh + t * node.Height),
                Side.Right => new Position(cx + hw, cy - hh + t * node.Height),
                _ => isSource ? edge.Points[0] : edge.Points[^1]
            };

            // For non-rectangular shapes (diamond, circle, etc.), project the
            // rectangular distribution point onto the actual shape boundary.
            // This prevents edges from terminating outside the visible shape.
            if (isNonRect)
            {
                var clipped = ClipPointToShape(node, newPt);
                if (clipped is not null)
                    newPt = new(clipped.Value.X, clipped.Value.Y);
            }

            if (isSource)
                edge.Points[0] = newPt;
            else
                edge.Points[^1] = newPt;
        }
    }

    static Position GetAdjacentWaypoint(Edge edge, bool isSource)
    {
        // Get the waypoint adjacent to the endpoint (the next point inward along the path)
        if (isSource && edge.Points.Count > 1)
            return edge.Points[1];
        if (!isSource && edge.Points.Count > 1)
            return edge.Points[^2];
        return edge.Points[0];
    }

    /// <summary>
    /// Merge near-collinear consecutive edge segments to eliminate micro-bends
    /// from dagre's dummy node positions. Also removes very short segments.
    /// </summary>
    static void SmoothEdgePaths(GraphDiagramBase diagram)
    {
        const double angleThreshold = 5.0 * Math.PI / 180.0; // 5 degrees
        const double minSegmentLength = 3.0;

        foreach (var edge in diagram.Edges)
        {
            if (edge.Points.Count < 3 || edge.SourceId == edge.TargetId) continue;

            // Remove near-zero-length intermediate points
            for (var i = edge.Points.Count - 2; i >= 1; i--)
            {
                var p = edge.Points[i];
                var prev = edge.Points[i - 1];
                var next = edge.Points[i + 1];
                var dPrev = Math.Sqrt((p.X - prev.X) * (p.X - prev.X) + (p.Y - prev.Y) * (p.Y - prev.Y));
                var dNext = Math.Sqrt((p.X - next.X) * (p.X - next.X) + (p.Y - next.Y) * (p.Y - next.Y));
                if (dPrev < minSegmentLength && dNext < minSegmentLength)
                {
                    edge.Points.RemoveAt(i);
                }
            }

            // Merge near-collinear segments
            for (var i = edge.Points.Count - 2; i >= 1; i--)
            {
                if (i >= edge.Points.Count - 1) continue;
                var prev = edge.Points[i - 1];
                var curr = edge.Points[i];
                var next = edge.Points[i + 1];

                var dx1 = curr.X - prev.X;
                var dy1 = curr.Y - prev.Y;
                var dx2 = next.X - curr.X;
                var dy2 = next.Y - curr.Y;

                var len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
                var len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
                if (len1 < 0.01 || len2 < 0.01) continue;

                // Angle between consecutive segments
                var dot = (dx1 * dx2 + dy1 * dy2) / (len1 * len2);
                dot = Math.Clamp(dot, -1.0, 1.0);
                var angle = Math.Acos(dot);

                if (angle < angleThreshold)
                {
                    edge.Points.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>
    /// Route edges around subgraph boundaries that they shouldn't pass through.
    /// If an edge connects two nodes and neither node is inside a given subgraph,
    /// the edge path should not cross through that subgraph's bounding box.
    /// Adds waypoints to route around the left or right side of blocking subgraphs.
    /// </summary>
    static void RouteAroundSubgraphs(GraphDiagramBase diagram)
    {
        if (diagram.Subgraphs.Count == 0) return;

        // Build flat list of all subgraphs with their bounds
        var allSubgraphs = new List<Subgraph>();
        var nodeToSubgraphs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        FlattenSubgraphsWithMembership(diagram.Subgraphs, allSubgraphs, nodeToSubgraphs);

        // Filter to subgraphs that have valid bounds (been laid out)
        var validSubgraphs = allSubgraphs
            .Where(sg => sg.Width > 0 && sg.Height > 0)
            .ToList();

        if (validSubgraphs.Count == 0) return;

        const double margin = 12.0; // Gap between edge and subgraph border

        foreach (var edge in diagram.Edges)
        {
            if (edge.Points.Count < 2 || edge.SourceId == edge.TargetId) continue;

            // Find subgraphs that block this edge (neither source nor target is inside)
            var blockers = new List<Subgraph>();
            foreach (var sg in validSubgraphs)
            {
                var srcInside = nodeToSubgraphs.TryGetValue(edge.SourceId, out var srcSgs) &&
                                srcSgs.Contains(sg.Id);
                var tgtInside = nodeToSubgraphs.TryGetValue(edge.TargetId, out var tgtSgs) &&
                                tgtSgs.Contains(sg.Id);
                if (srcInside || tgtInside) continue;

                // Check if the edge path actually crosses this subgraph
                var bounds = sg.Bounds;
                if (EdgeCrossesRect(edge.Points, bounds))
                    blockers.Add(sg);
            }

            if (blockers.Count == 0) continue;

            // Route around each blocking subgraph by inserting waypoints.
            // Process blockers sorted by Y position (top to bottom for TD layout)
            blockers.Sort((a, b) => a.Position.Y.CompareTo(b.Position.Y));

            var newPoints = new List<Position> { edge.Points[0] };

            for (var i = 0; i < edge.Points.Count - 1; i++)
            {
                var segStart = i == 0 ? newPoints[^1] : edge.Points[i];
                var segEnd = edge.Points[i + 1];

                foreach (var sg in blockers)
                {
                    var bounds = sg.Bounds;
                    if (!SegmentCrossesRect(segStart, segEnd, bounds)) continue;

                    // Decide which side to route around: left or right
                    var midX = (segStart.X + segEnd.X) / 2;
                    var distToLeft = Math.Abs(midX - bounds.Left);
                    var distToRight = Math.Abs(midX - bounds.Right);

                    var routeX = distToLeft <= distToRight
                        ? bounds.Left - margin   // Route around left
                        : bounds.Right + margin;  // Route around right

                    // Only add waypoints if they make sense directionally
                    if (segStart.Y < segEnd.Y) // Going downward (TD layout)
                    {
                        newPoints.Add(new(routeX, bounds.Top - margin));
                        newPoints.Add(new(routeX, bounds.Bottom + margin));
                    }
                    else // Going upward
                    {
                        newPoints.Add(new(routeX, bounds.Bottom + margin));
                        newPoints.Add(new(routeX, bounds.Top - margin));
                    }

                    break; // Only handle one blocker per segment
                }

                if (i < edge.Points.Count - 2)
                    newPoints.Add(edge.Points[i + 1]);
            }

            newPoints.Add(edge.Points[^1]);

            // Deduplicate consecutive points
            var deduped = new List<Position> { newPoints[0] };
            for (var i = 1; i < newPoints.Count; i++)
            {
                var prev = deduped[^1];
                var curr = newPoints[i];
                if (Math.Abs(prev.X - curr.X) > 0.5 || Math.Abs(prev.Y - curr.Y) > 0.5)
                    deduped.Add(curr);
            }

            if (deduped.Count > edge.Points.Count)
            {
                edge.Points.Clear();
                edge.Points.AddRange(deduped);
            }
        }
    }

    /// <summary>
    /// Build flat subgraph list and node-to-subgraph membership map.
    /// A node belongs to a subgraph if it's a direct member OR a member of a nested subgraph.
    /// </summary>
    static void FlattenSubgraphsWithMembership(
        IEnumerable<Subgraph> subgraphs,
        List<Subgraph> allSubgraphs,
        Dictionary<string, HashSet<string>> nodeToSubgraphs)
    {
        var stack = new Stack<(IEnumerable<Subgraph> sgs, string? parentId)>();
        stack.Push((subgraphs, null));

        while (stack.Count > 0)
        {
            var (currentSgs, parentId) = stack.Pop();
            foreach (var sg in currentSgs)
            {
                allSubgraphs.Add(sg);

                // All direct member nodes belong to this subgraph and all ancestors
                foreach (var nodeId in sg.NodeIds)
                {
                    if (!nodeToSubgraphs.TryGetValue(nodeId, out var set))
                    {
                        set = new(StringComparer.Ordinal);
                        nodeToSubgraphs[nodeId] = set;
                    }
                    set.Add(sg.Id);
                }

                if (sg.NestedSubgraphs.Count > 0)
                    stack.Push((sg.NestedSubgraphs, sg.Id));
            }
        }

        // Propagate: if a node is in a nested subgraph, it's also "in" all ancestor subgraphs
        PropagateAncestorMembership(subgraphs, nodeToSubgraphs);
    }

    static void PropagateAncestorMembership(
        IEnumerable<Subgraph> subgraphs,
        Dictionary<string, HashSet<string>> nodeToSubgraphs)
    {
        // Recursively collect all node IDs within a subgraph (including nested)
        foreach (var sg in subgraphs)
        {
            var allNodeIds = new HashSet<string>(StringComparer.Ordinal);
            CollectAllNodeIds(sg, allNodeIds);

            foreach (var nodeId in allNodeIds)
            {
                if (!nodeToSubgraphs.TryGetValue(nodeId, out var set))
                {
                    set = new(StringComparer.Ordinal);
                    nodeToSubgraphs[nodeId] = set;
                }
                set.Add(sg.Id);
            }
        }
    }

    static void CollectAllNodeIds(Subgraph sg, HashSet<string> result)
    {
        foreach (var nodeId in sg.NodeIds)
            result.Add(nodeId);
        foreach (var nested in sg.NestedSubgraphs)
            CollectAllNodeIds(nested, result);
    }

    /// <summary>
    /// Check if any segment of an edge path crosses through a rectangle.
    /// </summary>
    static bool EdgeCrossesRect(List<Position> points, Models.Rect rect)
    {
        for (var i = 0; i < points.Count - 1; i++)
        {
            if (SegmentCrossesRect(points[i], points[i + 1], rect))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if a line segment from p1 to p2 passes through a rectangle's interior.
    /// A segment "crosses" if it enters and exits the rect (not just touches an edge).
    /// </summary>
    static bool SegmentCrossesRect(Position p1, Position p2, Models.Rect rect)
    {
        // Quick check: if both points are on the same side, no crossing
        if (p1.X < rect.Left && p2.X < rect.Left) return false;
        if (p1.X > rect.Right && p2.X > rect.Right) return false;
        if (p1.Y < rect.Top && p2.Y < rect.Top) return false;
        if (p1.Y > rect.Bottom && p2.Y > rect.Bottom) return false;

        // Check if both endpoints are inside (edge is entirely within - not crossing)
        var p1Inside = rect.Contains(p1);
        var p2Inside = rect.Contains(p2);
        if (p1Inside && p2Inside) return false;

        // Check if the segment intersects any side of the rectangle
        // using parametric line intersection
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;

        // Check intersection with each rect edge
        double tMin = 0, tMax = 1;

        if (Math.Abs(dx) > 1e-10)
        {
            var t1 = (rect.Left - p1.X) / dx;
            var t2 = (rect.Right - p1.X) / dx;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            if (tMin > tMax) return false;
        }
        else if (p1.X < rect.Left || p1.X > rect.Right)
        {
            return false;
        }

        if (Math.Abs(dy) > 1e-10)
        {
            var t1 = (rect.Top - p1.Y) / dy;
            var t2 = (rect.Bottom - p1.Y) / dy;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            if (tMin > tMax) return false;
        }
        else if (p1.Y < rect.Top || p1.Y > rect.Bottom)
        {
            return false;
        }

        // The segment actually crosses if it enters and has some thickness of intersection
        return tMax - tMin > 0.01;
    }

    /// <summary>
    /// Pull back edge endpoints by the arrowhead marker size so the arrow tip
    /// isn't hidden behind the node shape (nodes are rendered on top of edges).
    /// Applies to ALL shapes since nodes are drawn on top of edge paths.
    /// </summary>
    static void PullBackArrowEndpoints(GraphDiagramBase diagram)
    {
        // SVG marker has refX=5 in a 10-unit viewBox mapped to 8px markerWidth,
        // so the arrow tip extends ~4px past the endpoint. Rectangles need a larger
        // pullback because the node fill fully covers the edge path at the boundary.
        // Non-rectangular shapes (diamond, circle) have thinner boundaries so the
        // tip needs to end closer to the shape outline.
        const double rectPullback = 5.0;
        const double shapePullback = 2.0; // Just enough to prevent tip clipping

        foreach (var edge in diagram.Edges)
        {
            if (edge.Points.Count < 2 || edge.SourceId == edge.TargetId) continue;

            // Pull back last point (target end) if arrow head
            if (edge.HasArrowHead)
            {
                var tgtNode = diagram.GetNode(edge.TargetId);
                var pb = tgtNode is not null && !IsRectangularShape(tgtNode.Shape)
                    ? shapePullback : rectPullback;
                var last = edge.Points[^1];
                var prev = edge.Points[^2];
                var dx = last.X - prev.X;
                var dy = last.Y - prev.Y;
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len > pb * 2)
                {
                    edge.Points[^1] = new(
                        last.X - dx / len * pb,
                        last.Y - dy / len * pb);
                }
            }

            // Pull back first point (source end) if arrow tail
            if (edge.HasArrowTail)
            {
                var srcNode = diagram.GetNode(edge.SourceId);
                var pb = srcNode is not null && !IsRectangularShape(srcNode.Shape)
                    ? shapePullback : rectPullback;
                var first = edge.Points[0];
                var next = edge.Points[1];
                var dx = first.X - next.X;
                var dy = first.Y - next.Y;
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len > pb * 2)
                {
                    edge.Points[0] = new(
                        first.X - dx / len * pb,
                        first.Y - dy / len * pb);
                }
            }
        }
    }
}
