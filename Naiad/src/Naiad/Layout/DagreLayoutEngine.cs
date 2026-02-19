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
    /// </summary>
    static void CompactSubgraphRanks(LayoutGraph graph, List<Subgraph> subgraphs)
    {
        foreach (var sg in subgraphs)
        {
            if (sg.NodeIds.Count < 2) continue;

            var memberRanks = new List<(string Id, int Rank)>();
            foreach (var nodeId in sg.NodeIds)
            {
                var node = graph.GetNode(nodeId);
                if (node is not null)
                    memberRanks.Add((nodeId, node.Rank));
            }

            if (memberRanks.Count < 2) continue;

            // Find the rank range of the majority of members
            var ranks = memberRanks.Select(m => m.Rank).OrderBy(r => r).ToList();
            var medianRank = ranks[ranks.Count / 2];
            var minRank = ranks.Min();
            var maxRank = ranks.Max();

            // If spread is small (2 ranks or fewer), nothing to fix
            if (maxRank - minRank <= 2) continue;

            // Pull outliers: any member whose rank is > 2 ranks from the median
            foreach (var (id, rank) in memberRanks)
            {
                if (Math.Abs(rank - medianRank) > 2)
                {
                    // Place outlier at the median rank (will be ordered by Phase 3)
                    graph.GetNode(id)?.SetRank(medianRank);
                }
            }

            if (sg.NestedSubgraphs.Count > 0)
                CompactSubgraphRanks(graph, sg.NestedSubgraphs);
        }
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

        // Find dummy nodes
        var dummies = graph.Nodes.Values
            .Where(n => n.IsDummy &&
                        n.OriginalEdgeSource == edge.SourceId &&
                        n.OriginalEdgeTarget == edge.TargetId)
            .OrderBy(n => n.Rank)
            .ToList();

        foreach (var dummy in dummies)
        {
            edge.Points.Add(new(dummy.X, dummy.Y));
        }

        var targetEdgeX = isHorizontal ? target.X - target.Width / 2 : target.X;
        var targetEdgeY = isHorizontal ? target.Y : target.Y - target.Height / 2;
        edge.Points.Add(new(targetEdgeX, targetEdgeY));
    }
}
