using MermaidSharp.Models;

namespace MermaidSharp.Layout;

public class DagreLayoutEngine : ILayoutEngine
{
    public LayoutResult Layout(GraphDiagramBase diagram, LayoutOptions options)
    {
        if (diagram.Nodes.Count == 0)
        {
            return new() { Width = 0, Height = 0 };
        }

        // Build internal graph with subgraph constraints
        var graph = BuildLayoutGraph(diagram);

        // Phase 1: Make acyclic
        Acyclic.Run(graph);

        // Phase 2: Assign ranks
        Ranker.Run(graph, options.Ranker);

        // Phase 2b: Pull outlier subgraph members to their subgraph's rank range
        CompactSubgraphRanks(graph, diagram.Subgraphs);

        // Rebuild ranks array after adjustments
        graph.BuildRanks();

        // Phase 3: Order nodes within ranks
        Ordering.Run(graph);

        // Phase 4: Assign coordinates
        CoordinateAssignment.Run(graph, options.NodeSeparation, options.RankSeparation, options.Direction);

        // Phase 5: Route edges
        CoordinateAssignment.RouteEdges(graph, options.Direction);

        // Undo edge reversals
        Acyclic.Undo(graph);

        // Apply positions back to diagram
        ApplyLayout(graph, diagram, options);

        // Clip edge endpoints to actual shape boundaries
        ClipEdgeEndpoints(diagram);

        // Calculate bounds (don't add margin again - positions already include it)
        var width = diagram.Nodes.Max(n => n.Position.X + n.Width / 2);
        var height = diagram.Nodes.Max(n => n.Position.Y + n.Height / 2);

        return new()
        {
            Width = width,
            Height = height
        };
    }

    static LayoutGraph BuildLayoutGraph(GraphDiagramBase diagram)
    {
        var graph = new LayoutGraph();

        // Build subgraph membership lookup
        var nodeToSubgraph = new Dictionary<string, string>(StringComparer.Ordinal);
        void MapSubgraph(Subgraph sg)
        {
            foreach (var nid in sg.NodeIds)
                nodeToSubgraph[nid] = sg.Id;
            foreach (var nested in sg.NestedSubgraphs)
                MapSubgraph(nested);
        }
        foreach (var sg in diagram.Subgraphs)
            MapSubgraph(sg);

        foreach (var node in diagram.Nodes)
        {
            graph.AddNode(new()
            {
                Id = node.Id,
                Width = node.Width,
                Height = node.Height,
                Shape = node.Shape,
                SkinShapeName = node.SkinShapeName,
                SubgraphId = nodeToSubgraph.GetValueOrDefault(node.Id)
            });
        }

        foreach (var edge in diagram.Edges)
        {
            graph.AddEdge(new()
            {
                SourceId = edge.SourceId,
                TargetId = edge.TargetId
            });
        }

        // Add lightweight constraint edges within subgraphs to keep members
        // on contiguous ranks. For each subgraph, chain members that don't
        // already have a path between them so the ranker keeps them together.
        AddSubgraphConstraints(graph, diagram.Subgraphs);

        return graph;
    }

    /// <summary>
    /// After rank assignment, pull outlier subgraph members to be within the
    /// rank range of their siblings. This fixes cases where a back-edge reversal
    /// pushes a node to rank 0 or the max rank, far from its subgraph.
    /// Only moves nodes that have NO real edges anchoring them at their rank
    /// (i.e., no predecessor at rank-1 or successor at rank+1).
    /// </summary>
    static void CompactSubgraphRanks(LayoutGraph graph, List<Subgraph> subgraphs)
    {
        foreach (var sg in subgraphs)
        {
            // Process nested subgraphs first (leaf-to-root)
            if (sg.NestedSubgraphs.Count > 0)
                CompactSubgraphRanks(graph, sg.NestedSubgraphs);

            if (sg.NodeIds.Count < 2) continue;

            // Collect ranks of ALL descendants (including nested subgraph nodes)
            var allDescendantRanks = new List<int>();
            CollectAllDescendantRanks(graph, sg, allDescendantRanks);

            if (allDescendantRanks.Count < 2) continue;

            allDescendantRanks.Sort();
            var fullMin = allDescendantRanks[0];
            var fullMax = allDescendantRanks[^1];
            var fullMedian = allDescendantRanks[allDescendantRanks.Count / 2];

            if (fullMax - fullMin <= 2) continue;

            foreach (var nodeId in sg.NodeIds)
            {
                var node = graph.GetNode(nodeId);
                if (node is null) continue;

                if (Math.Abs(node.Rank - fullMedian) > 2)
                {
                    // Don't move if the node has real edges anchoring it
                    // (predecessor at rank-1 or successor at rank+1)
                    var hasAnchoringEdge =
                        node.InEdges.Any(e => !e.IsConstraint &&
                            graph.GetNode(e.SourceId) is { } src && src.Rank == node.Rank - 1) ||
                        node.OutEdges.Any(e => !e.IsConstraint &&
                            graph.GetNode(e.TargetId) is { } tgt && tgt.Rank == node.Rank + 1);

                    if (!hasAnchoringEdge)
                    {
                        node.SetRank(fullMedian);
                    }
                }
            }
        }
    }

    static void CollectAllDescendantRanks(LayoutGraph graph, Subgraph sg, List<int> ranks)
    {
        foreach (var nodeId in sg.NodeIds)
        {
            var node = graph.GetNode(nodeId);
            if (node is not null)
                ranks.Add(node.Rank);
        }
        foreach (var nested in sg.NestedSubgraphs)
            CollectAllDescendantRanks(graph, nested, ranks);
    }

    /// <summary>
    /// For each subgraph, find members with no edges at all (orphaned) and add
    /// a single zero-weight constraint edge to an anchored sibling. This keeps
    /// orphaned members near their subgraph without forcing same-rank members
    /// into a sequential chain.
    /// </summary>
    static void AddSubgraphConstraints(LayoutGraph graph, List<Subgraph> subgraphs)
    {
        foreach (var sg in subgraphs)
        {
            if (sg.NodeIds.Count < 2) continue;

            var members = new HashSet<string>(sg.NodeIds, StringComparer.Ordinal);

            // A node is "anchored" if it has ANY real edge (in or out) to a non-constraint node
            var anchored = new List<string>();
            var orphaned = new List<string>();

            foreach (var nodeId in sg.NodeIds)
            {
                var node = graph.GetNode(nodeId);
                if (node is null) continue;

                var hasRealEdge = node.InEdges.Any(e => !e.IsConstraint) ||
                                  node.OutEdges.Any(e => !e.IsConstraint);
                if (hasRealEdge)
                    anchored.Add(nodeId);
                else
                    orphaned.Add(nodeId);
            }

            // Only add constraints for truly orphaned nodes
            if (orphaned.Count > 0 && anchored.Count > 0)
            {
                var anchor = anchored[0];
                foreach (var nodeId in orphaned)
                {
                    // Use a same-rank-ish constraint: bidirectional zero-weight
                    // by making anchor → orphan with weight 0 and min rank length 0
                    graph.AddEdge(new()
                    {
                        SourceId = anchor,
                        TargetId = nodeId,
                        Weight = 0,
                        IsConstraint = true
                    });
                }
            }
            else if (orphaned.Count > 1)
            {
                // All orphaned — chain them with zero-weight edges
                for (var i = 0; i < orphaned.Count - 1; i++)
                {
                    graph.AddEdge(new()
                    {
                        SourceId = orphaned[i],
                        TargetId = orphaned[i + 1],
                        Weight = 0,
                        IsConstraint = true
                    });
                }
            }

            if (sg.NestedSubgraphs.Count > 0)
                AddSubgraphConstraints(graph, sg.NestedSubgraphs);
        }
    }

    static void ApplyLayout(LayoutGraph graph, GraphDiagramBase diagram, LayoutOptions options)
    {
        // Don't add margin here - let the renderer handle padding
        foreach (var node in diagram.Nodes)
        {
            var layoutNode = graph.GetNode(node.Id);
            if (layoutNode is not null)
            {
                node.Position = new(layoutNode.X, layoutNode.Y);
                node.Rank = layoutNode.Rank;
                node.Order = layoutNode.Order;
            }
        }

        foreach (var edge in diagram.Edges)
        {
            // Find the original edge or reconstruct from dummies
            var layoutEdge = graph.Edges.FirstOrDefault(e =>
                e.SourceId == edge.SourceId && e.TargetId == edge.TargetId);

            if (layoutEdge is not null)
            {
                edge.Points.Clear();
                foreach (var point in layoutEdge.Points)
                {
                    edge.Points.Add(new(point.X, point.Y));
                }
            }
            else
            {
                // Edge was split by dummy nodes - collect points
                CollectEdgePoints(graph, edge, options);
            }
        }
    }

    static void CollectEdgePoints(LayoutGraph graph, Edge edge, LayoutOptions options)
    {
        edge.Points.Clear();

        var source = graph.GetNode(edge.SourceId);
        var target = graph.GetNode(edge.TargetId);

        if (source is null || target is null)
        {
            return;
        }

        var isHorizontal = options.Direction is Direction.LeftToRight or Direction.RightToLeft;

        // For horizontal layout: connect right edge of source to left edge of target
        // For vertical layout: connect bottom edge of source to top edge of target
        var sourceEdgeX = isHorizontal ? source.X + source.Width / 2 : source.X;
        var sourceEdgeY = isHorizontal ? source.Y : source.Y + source.Height / 2;
        edge.Points.Add(new(sourceEdgeX, sourceEdgeY));

        // Find dummy nodes - check both directions since back-edges were reversed
        // during layout (dummies store the reversed source/target)
        var dummies = graph.Nodes.Values
            .Where(n => n.IsDummy &&
                        ((n.OriginalEdgeSource == edge.SourceId && n.OriginalEdgeTarget == edge.TargetId) ||
                         (n.OriginalEdgeSource == edge.TargetId && n.OriginalEdgeTarget == edge.SourceId)))
            .ToList();

        // Order dummies by rank in the direction of the edge
        var source2 = graph.GetNode(edge.SourceId);
        var target2 = graph.GetNode(edge.TargetId);
        if (source2 is not null && target2 is not null && source2.Rank > target2.Rank)
            dummies = dummies.OrderByDescending(n => n.Rank).ToList();
        else
            dummies = dummies.OrderBy(n => n.Rank).ToList();

        foreach (var dummy in dummies)
        {
            edge.Points.Add(new(dummy.X, dummy.Y));
        }

        var targetEdgeX = isHorizontal ? target.X - target.Width / 2 : target.X;
        var targetEdgeY = isHorizontal ? target.Y : target.Y - target.Height / 2;
        edge.Points.Add(new(targetEdgeX, targetEdgeY));
    }

    /// <summary>
    /// Clip edge endpoints to actual node shape boundaries using universal geometry.
    /// For edges with ≥2 points, replaces first/last points with shape intersection.
    /// </summary>
    static void ClipEdgeEndpoints(GraphDiagramBase diagram)
    {
        foreach (var edge in diagram.Edges)
        {
            if (edge.Points.Count < 2 || edge.SourceId == edge.TargetId) continue;

            var srcNode = diagram.GetNode(edge.SourceId);
            var tgtNode = diagram.GetNode(edge.TargetId);

            if (srcNode is not null)
            {
                var hit = IntersectNodeUniversal(srcNode, edge.Points[1]);
                if (hit is not null) edge.Points[0] = hit.Value;
            }

            if (tgtNode is not null)
            {
                var hit = IntersectNodeUniversal(tgtNode, edge.Points[^2]);
                if (hit is not null) edge.Points[^1] = hit.Value;
            }
        }
    }

    static Position? IntersectNodeUniversal(Node node, Position target)
    {
        var segments = DagreNetLayoutEngine.GetNodeSegments(node);
        if (segments.Count == 0) return null;

        var centroid = ShapeGeometry.Centroid(segments);
        var dir = new PointD(target.X - centroid.X, target.Y - centroid.Y);
        var hit = ShapeGeometry.IntersectRay(segments, centroid, dir);
        return hit is not null ? new Position(hit.Value.X, hit.Value.Y) : null;
    }
}
