namespace MermaidSharp.Layout;

static class CoordinateAssignment
{
    public static void Run(LayoutGraph graph, double nodeSep, double rankSep, Direction direction)
    {
        graph.BuildRanks();
        graph.UpdateOrderInRanks();

        var isHorizontal = direction is Direction.LeftToRight or Direction.RightToLeft;

        // Assign Y coordinates based on ranks
        AssignRankCoordinates(graph, rankSep, isHorizontal);

        // Assign X coordinates using Brandes-Köpf (4-pass with median balancing)
        AssignPositionCoordinates(graph, nodeSep, isHorizontal);

        // Handle direction reversal
        AdjustForDirection(graph, direction);
    }

    static void AssignRankCoordinates(LayoutGraph graph, double rankSep, bool isHorizontal)
    {
        double currentY = 0;

        for (var r = 0; r < graph.Ranks.Length; r++)
        {
            var nodesInRank = graph.Ranks[r];
            var maxHeight = nodesInRank.Count > 0
                ? nodesInRank.Max(n => isHorizontal ? n.Width : n.Height)
                : 0;

            foreach (var node in nodesInRank)
            {
                if (isHorizontal)
                {
                    node.X = currentY + maxHeight / 2;
                }
                else
                {
                    node.Y = currentY + maxHeight / 2;
                }
            }

            currentY += maxHeight + rankSep;
        }
    }

    /// <summary>
    /// Brandes-Köpf coordinate assignment: run 4 directional passes
    /// (up-left, up-right, down-left, down-right), then balance by taking
    /// the median of the 4 results for each node.
    /// </summary>
    static void AssignPositionCoordinates(LayoutGraph graph, double nodeSep, bool isHorizontal)
    {
        // Build layering matrix (ranks ordered by node.Order)
        var layering = new List<LayoutNode>[graph.Ranks.Length];
        for (var r = 0; r < graph.Ranks.Length; r++)
        {
            layering[r] = graph.Ranks[r].OrderBy(n => n.Order).ToList();
        }

        // Build position lookup (order within rank)
        var pos = new Dictionary<string, int>();
        foreach (var layer in layering)
        {
            for (var i = 0; i < layer.Count; i++)
                pos[layer[i].Id] = i;
        }

        // Find type-1 conflicts (non-inner segment crossing inner segment)
        var conflicts = FindType1Conflicts(graph, layering, pos);

        // Run 4 directional passes
        var results = new Dictionary<string, double>[4];
        var dirs = new[] { (true, true), (true, false), (false, true), (false, false) };
        // (upward, leftward) combinations: UL, UR, DL, DR

        for (var d = 0; d < 4; d++)
        {
            var (upward, leftward) = dirs[d];

            // Adjust layering for direction
            var adjustedLayering = new List<LayoutNode>[layering.Length];
            if (upward)
            {
                for (var r = 0; r < layering.Length; r++)
                    adjustedLayering[r] = new List<LayoutNode>(layering[r]);
            }
            else
            {
                for (var r = 0; r < layering.Length; r++)
                    adjustedLayering[r] = new List<LayoutNode>(layering[layering.Length - 1 - r]);
            }

            if (!leftward)
            {
                for (var r = 0; r < adjustedLayering.Length; r++)
                    adjustedLayering[r].Reverse();
            }

            // Step 1: Vertical alignment - build blocks
            var (root, align) = VerticalAlignment(graph, adjustedLayering, pos, conflicts, upward);

            // Step 2: Horizontal compaction - assign positions to blocks
            var xs = HorizontalCompaction(graph, adjustedLayering, root, align, nodeSep, isHorizontal, !leftward);

            // Negate positions for right-to-left passes
            if (!leftward)
            {
                foreach (var key in xs.Keys.ToList())
                    xs[key] = -xs[key];
            }

            results[d] = xs;
        }

        // Find smallest width alignment and align all to it
        AlignCoordinates(results);

        // Balance: take median of 4 results for each node
        foreach (var node in graph.Nodes.Values)
        {
            var values = new double[4];
            for (var d = 0; d < 4; d++)
                values[d] = results[d].GetValueOrDefault(node.Id);
            Array.Sort(values);
            var balanced = (values[1] + values[2]) / 2; // median of 4

            if (isHorizontal)
                node.Y = balanced;
            else
                node.X = balanced;
        }

        NormalizePositions(graph);
    }

    /// <summary>
    /// Find type-1 conflicts: a non-inner segment that crosses an inner segment.
    /// Inner segments are edges between two dummy nodes (part of a long edge).
    /// </summary>
    static HashSet<(string, string)> FindType1Conflicts(LayoutGraph graph,
        List<LayoutNode>[] layering, Dictionary<string, int> pos)
    {
        var conflicts = new HashSet<(string, string)>();

        for (var r = 1; r < layering.Length; r++)
        {
            var prevLayer = layering[r - 1];
            var layer = layering[r];
            var k0 = 0;
            var scanPos = 0;
            var prevLayerLength = prevLayer.Count;

            for (var i = 0; i < layer.Count; i++)
            {
                var v = layer[i];
                // Find if v connects to a dummy in previous layer (inner segment)
                var w = FindOtherInnerSegmentNode(graph, v, true);
                var k1 = w is not null ? pos[w.Id] : prevLayerLength;

                if (w is not null || i == layer.Count - 1)
                {
                    for (var scanIdx = scanPos; scanIdx <= i; scanIdx++)
                    {
                        var scanNode = layer[scanIdx];
                        foreach (var pred in graph.GetPredecessors(scanNode.Id))
                        {
                            var uPos = pos.GetValueOrDefault(pred.Id, -1);
                            if ((uPos < k0 || k1 < uPos) &&
                                !(pred.IsDummy && scanNode.IsDummy))
                            {
                                AddConflict(conflicts, pred.Id, scanNode.Id);
                            }
                        }
                    }
                    scanPos = i + 1;
                    k0 = k1;
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Find the predecessor that forms an inner segment (both nodes are dummy).
    /// </summary>
    static LayoutNode? FindOtherInnerSegmentNode(LayoutGraph graph, LayoutNode node, bool usePredecessors)
    {
        if (!node.IsDummy) return null;

        var neighbors = usePredecessors
            ? graph.GetPredecessors(node.Id)
            : graph.GetSuccessors(node.Id);

        foreach (var n in neighbors)
        {
            if (n.IsDummy) return n;
        }
        return null;
    }

    static void AddConflict(HashSet<(string, string)> conflicts, string v, string w)
    {
        var key = string.CompareOrdinal(v, w) < 0 ? (v, w) : (w, v);
        conflicts.Add(key);
    }

    static bool HasConflict(HashSet<(string, string)> conflicts, string v, string w)
    {
        var key = string.CompareOrdinal(v, w) < 0 ? (v, w) : (w, v);
        return conflicts.Contains(key);
    }

    /// <summary>
    /// Build vertical alignment: group nodes into "blocks" that share the same X position.
    /// Each node aligns with its median neighbor in the previous layer, avoiding type-1 conflicts.
    /// Returns root[v] = the root node of v's block, align[v] = next node in v's block chain.
    /// </summary>
    static (Dictionary<string, string> Root, Dictionary<string, string> Align) VerticalAlignment(
        LayoutGraph graph,
        List<LayoutNode>[] layering,
        Dictionary<string, int> pos,
        HashSet<(string, string)> conflicts,
        bool usePredecessors)
    {
        var root = new Dictionary<string, string>();
        var align = new Dictionary<string, string>();

        // Initialize: each node is its own root and aligned to itself
        foreach (var layer in layering)
        {
            foreach (var node in layer)
            {
                root[node.Id] = node.Id;
                align[node.Id] = node.Id;
            }
        }

        // Process each layer
        foreach (var layer in layering)
        {
            var prevIdx = -1;
            foreach (var v in layer)
            {
                var neighbors = usePredecessors
                    ? graph.GetPredecessors(v.Id).ToList()
                    : graph.GetSuccessors(v.Id).ToList();

                if (neighbors.Count == 0) continue;

                // Sort neighbors by their position in their layer
                neighbors.Sort((a, b) => pos.GetValueOrDefault(a.Id).CompareTo(pos.GetValueOrDefault(b.Id)));

                // Find median neighbor(s)
                var mp = (neighbors.Count - 1) / 2.0;
                for (var i = (int)Math.Floor(mp); i <= (int)Math.Ceiling(mp); i++)
                {
                    var w = neighbors[i];
                    if (align[v.Id] == v.Id && // v not already aligned
                        prevIdx < pos.GetValueOrDefault(w.Id) && // maintains left-to-right order
                        !HasConflict(conflicts, v.Id, w.Id)) // no type-1 conflict
                    {
                        align[w.Id] = v.Id;
                        align[v.Id] = root[v.Id] = root[w.Id];
                        prevIdx = pos.GetValueOrDefault(w.Id);
                    }
                }
            }
        }

        return (root, align);
    }

    /// <summary>
    /// Horizontal compaction: assign X coordinates to blocks.
    /// Builds a "block graph" where edges represent minimum separation constraints
    /// between adjacent blocks, then does two-pass positioning.
    /// </summary>
    static Dictionary<string, double> HorizontalCompaction(
        LayoutGraph graph,
        List<LayoutNode>[] layering,
        Dictionary<string, string> root,
        Dictionary<string, string> align,
        double nodeSep,
        bool isHorizontal,
        bool reverseSep)
    {
        var xs = new Dictionary<string, double>();

        // Build block graph: for each pair of adjacent nodes in a layer,
        // create an edge between their block roots with minimum separation weight
        var blockEdges = new Dictionary<string, Dictionary<string, double>>(); // source -> target -> weight

        foreach (var layer in layering)
        {
            LayoutNode? prev = null;
            foreach (var v in layer)
            {
                var vRoot = root[v.Id];
                if (!blockEdges.ContainsKey(vRoot))
                    blockEdges[vRoot] = new Dictionary<string, double>();

                if (prev is not null)
                {
                    var uRoot = root[prev.Id];
                    if (uRoot != vRoot) // different blocks
                    {
                        var sep = GetSeparation(prev, v, nodeSep, isHorizontal);
                        if (reverseSep) sep = -sep;
                        var absSep = Math.Abs(sep);

                        if (!blockEdges.ContainsKey(uRoot))
                            blockEdges[uRoot] = new Dictionary<string, double>();

                        if (!blockEdges[uRoot].TryGetValue(vRoot, out var existing) || absSep > existing)
                            blockEdges[uRoot][vRoot] = absSep;
                    }
                }
                prev = v;
            }
        }

        // Collect all block roots
        var allRoots = new HashSet<string>();
        foreach (var layer in layering)
            foreach (var v in layer)
                allRoots.Add(root[v.Id]);

        // Pass 1: Left-to-right - assign minimum X based on predecessors
        // Topological order: process blocks whose predecessors are all done
        var inDegree = new Dictionary<string, int>();
        foreach (var r in allRoots) inDegree[r] = 0;
        foreach (var (src, targets) in blockEdges)
        {
            foreach (var tgt in targets.Keys)
            {
                if (allRoots.Contains(tgt))
                    inDegree[tgt] = inDegree.GetValueOrDefault(tgt) + 1;
            }
        }

        var queue = new Queue<string>();
        foreach (var r in allRoots)
            if (inDegree.GetValueOrDefault(r) == 0)
                queue.Enqueue(r);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();
            var maxPredX = 0.0;
            // Find max(predecessor position + separation)
            foreach (var (src, targets) in blockEdges)
            {
                if (targets.TryGetValue(block, out var weight))
                {
                    maxPredX = Math.Max(maxPredX, xs.GetValueOrDefault(src) + weight);
                }
            }
            xs[block] = maxPredX;

            if (blockEdges.TryGetValue(block, out var successors))
            {
                foreach (var succ in successors.Keys)
                {
                    if (!allRoots.Contains(succ)) continue;
                    inDegree[succ]--;
                    if (inDegree[succ] == 0)
                        queue.Enqueue(succ);
                }
            }
        }

        // Handle any blocks not reached (shouldn't happen in DAG but safety)
        foreach (var r in allRoots)
            if (!xs.ContainsKey(r))
                xs[r] = 0;

        // Pass 2: Right-to-left - tighten by pulling nodes right toward successors
        // Process in reverse topological order
        var reverseOrder = xs.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        foreach (var block in reverseOrder)
        {
            if (!blockEdges.TryGetValue(block, out var successors)) continue;
            var minSuccX = double.PositiveInfinity;
            foreach (var (succ, weight) in successors)
            {
                if (xs.TryGetValue(succ, out var succX))
                    minSuccX = Math.Min(minSuccX, succX - weight);
            }
            if (minSuccX != double.PositiveInfinity)
                xs[block] = Math.Max(xs[block], minSuccX);
        }

        // Map each node to its block root's position
        var result = new Dictionary<string, double>();
        foreach (var node in graph.Nodes.Values)
        {
            result[node.Id] = xs.GetValueOrDefault(root[node.Id]);
        }

        return result;
    }

    static double GetSeparation(LayoutNode u, LayoutNode v, double nodeSep, bool isHorizontal)
    {
        var uSize = isHorizontal ? u.Height : u.Width;
        var vSize = isHorizontal ? v.Height : v.Width;
        return (uSize + vSize) / 2 + nodeSep;
    }

    /// <summary>
    /// Align all 4 results to the smallest-width alignment's reference point.
    /// </summary>
    static void AlignCoordinates(Dictionary<string, double>[] results)
    {
        // Find the alignment with the smallest width
        var minWidth = double.PositiveInfinity;
        var bestIdx = 0;
        for (var d = 0; d < results.Length; d++)
        {
            if (results[d].Count == 0) continue;
            var min = results[d].Values.Min();
            var max = results[d].Values.Max();
            var width = max - min;
            if (width < minWidth)
            {
                minWidth = width;
                bestIdx = d;
            }
        }

        if (results[bestIdx].Count == 0) return;

        var bestMin = results[bestIdx].Values.Min();
        var bestMax = results[bestIdx].Values.Max();

        // Align each result so its center matches the best alignment's center
        for (var d = 0; d < results.Length; d++)
        {
            if (d == bestIdx || results[d].Count == 0) continue;

            var dMin = results[d].Values.Min();
            var dMax = results[d].Values.Max();

            // Shift to align left edges, then center
            var shiftLeft = bestMin - dMin;
            var shiftRight = bestMax - dMax;

            var shift = (dMin + shiftLeft < dMin + shiftRight)
                ? shiftLeft : shiftRight;

            // Actually: dagre shifts by min or max alignment
            // Use the shift that brings closest to best alignment center
            var bestCenter = (bestMin + bestMax) / 2;
            var dCenter = (dMin + dMax) / 2;
            shift = bestCenter - dCenter;

            foreach (var key in results[d].Keys.ToList())
                results[d][key] += shift;
        }
    }

    static void NormalizePositions(LayoutGraph graph)
    {
        if (graph.Nodes.Count == 0)
        {
            return;
        }

        var minX = graph.Nodes.Values.Min(n => n.X - n.Width / 2);
        var minY = graph.Nodes.Values.Min(n => n.Y - n.Height / 2);

        foreach (var node in graph.Nodes.Values)
        {
            node.X -= minX;
            node.Y -= minY;
        }
    }

    static void AdjustForDirection(LayoutGraph graph, Direction direction)
    {
        if (graph.Nodes.Count == 0)
        {
            return;
        }

        switch (direction)
        {
            case Direction.BottomToTop:
                var maxY = graph.Nodes.Values.Max(n => n.Y);
                foreach (var node in graph.Nodes.Values)
                {
                    node.Y = maxY - node.Y;
                }
                break;

            case Direction.RightToLeft:
                var maxX = graph.Nodes.Values.Max(n => n.X);
                foreach (var node in graph.Nodes.Values)
                {
                    node.X = maxX - node.X;
                }
                break;
        }
    }

    // Arrow marker size - the arrowhead extends this far past the line endpoint
    const double ArrowMarkerOffset = 5;

    public static void RouteEdges(LayoutGraph graph, Direction direction)
    {
        var isHorizontal = direction is Direction.LeftToRight or Direction.RightToLeft;

        foreach (var edge in graph.Edges)
        {
            var source = graph.GetNode(edge.SourceId);
            var target = graph.GetNode(edge.TargetId);

            if (source is null || target is null)
            {
                continue;
            }

            edge.Points.Clear();

            if (source.IsDummy || target.IsDummy)
            {
                // Part of a long edge - just add the node positions
                edge.Points.Add(new(source.X, source.Y));
                edge.Points.Add(new(target.X, target.Y));
            }
            else
            {
                // Regular edge - create path through dummy nodes if any
                // For horizontal layout: connect right edge of source
                // For vertical layout: connect bottom edge of source
                var sourceEdgeX = isHorizontal ? source.X + source.Width / 2 : source.X;
                var sourceEdgeY = isHorizontal ? source.Y : source.Y + source.Height / 2;
                edge.Points.Add(new(sourceEdgeX, sourceEdgeY));

                // Find dummy nodes for this edge - check both directions for reversed edges
                var dummies = graph.Nodes.Values
                    .Where(n => n.IsDummy &&
                                ((n.OriginalEdgeSource == edge.SourceId && n.OriginalEdgeTarget == edge.TargetId) ||
                                 (n.OriginalEdgeSource == edge.TargetId && n.OriginalEdgeTarget == edge.SourceId)))
                    .ToList();

                // Order by rank in edge direction
                if (source.Rank > target.Rank)
                    dummies = dummies.OrderByDescending(n => n.Rank).ToList();
                else
                    dummies = dummies.OrderBy(n => n.Rank).ToList();

                foreach (var dummy in dummies)
                {
                    edge.Points.Add(new(dummy.X, dummy.Y));
                }

                // Calculate the target endpoint, offset to account for arrow marker
                // For horizontal layout: connect left edge of target
                // For vertical layout: connect top edge of target
                var targetEdgeX = isHorizontal ? target.X - target.Width / 2 : target.X;
                var targetEdgeY = isHorizontal ? target.Y : target.Y - target.Height / 2;

                // Get the last point before target to determine edge direction
                var lastPoint = edge.Points[^1];
                var dx = targetEdgeX - lastPoint.X;
                var dy = targetEdgeY - lastPoint.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);

                if (length > ArrowMarkerOffset)
                {
                    // Shorten the endpoint by the arrow marker size
                    var ratio = (length - ArrowMarkerOffset) / length;
                    targetEdgeX = lastPoint.X + dx * ratio;
                    targetEdgeY = lastPoint.Y + dy * ratio;
                }

                edge.Points.Add(new(targetEdgeX, targetEdgeY));
            }
        }
    }
}
