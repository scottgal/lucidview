namespace MermaidSharp.Layout;

internal static class Ordering
{
    const int MaxIterations = 24;

    public static void Run(LayoutGraph graph)
    {
        graph.BuildRanks();
        InitializeOrder(graph);

        var bestCrossings = CountCrossings(graph);
        var bestOrders = SaveOrders(graph);

        for (var i = 0; i < MaxIterations && bestCrossings > 0; i++)
        {
            if (i % 2 == 0)
                SweepDown(graph);
            else
                SweepUp(graph);

            var crossings = CountCrossings(graph);
            if (crossings < bestCrossings)
            {
                bestCrossings = crossings;
                bestOrders = SaveOrders(graph);
            }
        }

        RestoreOrders(graph, bestOrders);
        graph.UpdateOrderInRanks();
    }

    static void InitializeOrder(LayoutGraph graph)
    {
        var visited = new HashSet<string>(graph.Nodes.Count);
        var rankCounters = new int[graph.Ranks.Length];
        var queue = new Queue<LayoutNode>();

        foreach (var node in graph.Nodes.Values)
        {
            if (node.InEdges.Count == 0 && visited.Add(node.Id))
            {
                node.Order = rankCounters[node.Rank]++;
                queue.Enqueue(node);
            }
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var edge in node.OutEdges)
            {
                var target = graph.GetNode(edge.TargetId);
                if (target is not null && visited.Add(target.Id))
                {
                    target.Order = rankCounters[target.Rank]++;
                    queue.Enqueue(target);
                }
            }
        }

        foreach (var node in graph.Nodes.Values)
        {
            if (visited.Add(node.Id))
                node.Order = rankCounters[node.Rank]++;
        }

        for (var r = 0; r < graph.Ranks.Length; r++)
            graph.Ranks[r].Sort(static (a, b) => a.Order.CompareTo(b.Order));
    }

    static void SweepDown(LayoutGraph graph)
    {
        for (var r = 1; r < graph.Ranks.Length; r++)
            OrderByMedian(graph, r, true);
    }

    static void SweepUp(LayoutGraph graph)
    {
        for (var r = graph.Ranks.Length - 2; r >= 0; r--)
            OrderByMedian(graph, r, false);
    }

    static void OrderByMedian(LayoutGraph graph, int rank, bool useInEdges)
    {
        var nodesInRank = graph.Ranks[rank];
        var count = nodesInRank.Count;
        if (count == 0) return;

        // Compute median positions using a dictionary for O(1) lookup in sort
        var positions = new Dictionary<string, double>(count);
        var neighborBuf = new int[Math.Max(count * 4, 16)];

        for (var idx = 0; idx < count; idx++)
        {
            var node = nodesInRank[idx];
            var edges = useInEdges ? node.InEdges : node.OutEdges;

            if (edges.Count == 0)
            {
                positions[node.Id] = node.Order;
                continue;
            }

            var nCount = 0;
            foreach (var edge in edges)
            {
                var neighborId = useInEdges ? edge.SourceId : edge.TargetId;
                var neighbor = graph.GetNode(neighborId);
                if (neighbor is not null && nCount < neighborBuf.Length)
                    neighborBuf[nCount++] = neighbor.Order;
            }

            if (nCount == 0)
            {
                positions[node.Id] = node.Order;
            }
            else
            {
                Array.Sort(neighborBuf, 0, nCount);
                var mid = nCount / 2;
                positions[node.Id] = (nCount % 2 == 1)
                    ? neighborBuf[mid]
                    : (neighborBuf[mid - 1] + neighborBuf[mid]) / 2.0;
            }
        }

        // Sort by median position, stable via original order tiebreaker
        nodesInRank.Sort((a, b) =>
        {
            var cmp = positions[a.Id].CompareTo(positions[b.Id]);
            return cmp != 0 ? cmp : a.Order.CompareTo(b.Order);
        });

        for (var i = 0; i < count; i++)
            nodesInRank[i].Order = i;
    }

    static int CountCrossings(LayoutGraph graph)
    {
        var total = 0;
        for (var r = 0; r < graph.Ranks.Length - 1; r++)
            total += CountCrossingsBetweenRanks(graph, r, r + 1);
        return total;
    }

    /// <summary>
    /// Count edge crossings between two adjacent ranks using O(n log n)
    /// merge-sort inversion count instead of O(n²) brute force.
    /// Edges are sorted by source order, then inversions in target
    /// orders are counted - each inversion = one crossing.
    /// </summary>
    static int CountCrossingsBetweenRanks(LayoutGraph graph, int rank1, int rank2)
    {
        // Collect edges sorted by source order (primary), target order (secondary)
        var rank1Nodes = graph.Ranks[rank1];
        var edgeCount = 0;

        // First pass: count edges to pre-allocate
        foreach (var node in rank1Nodes)
        {
            foreach (var edge in node.OutEdges)
            {
                var target = graph.GetNode(edge.TargetId);
                if (target is not null && target.Rank == rank2)
                    edgeCount++;
            }
        }

        if (edgeCount <= 1) return 0;

        // Collect target orders sorted by source order
        var targetOrders = new int[edgeCount];
        var idx = 0;
        foreach (var node in rank1Nodes) // already in order within rank
        {
            foreach (var edge in node.OutEdges)
            {
                var target = graph.GetNode(edge.TargetId);
                if (target is not null && target.Rank == rank2)
                    targetOrders[idx++] = target.Order;
            }
        }

        // Count inversions in targetOrders using merge sort
        var temp = new int[edgeCount];
        return MergeSortCount(targetOrders, temp, 0, edgeCount - 1);
    }

    /// <summary>
    /// Merge-sort based inversion count. O(n log n) instead of O(n²).
    /// Each inversion in the target-order array = one edge crossing.
    /// </summary>
    static int MergeSortCount(int[] arr, int[] temp, int left, int right)
    {
        if (left >= right) return 0;

        var mid = left + (right - left) / 2;
        var count = 0;
        count += MergeSortCount(arr, temp, left, mid);
        count += MergeSortCount(arr, temp, mid + 1, right);
        count += MergeCount(arr, temp, left, mid, right);
        return count;
    }

    static int MergeCount(int[] arr, int[] temp, int left, int mid, int right)
    {
        var i = left;
        var j = mid + 1;
        var k = left;
        var count = 0;

        while (i <= mid && j <= right)
        {
            if (arr[i] <= arr[j])
            {
                temp[k++] = arr[i++];
            }
            else
            {
                // All remaining elements in left half (i..mid) form inversions with arr[j]
                count += (mid - i + 1);
                temp[k++] = arr[j++];
            }
        }

        while (i <= mid) temp[k++] = arr[i++];
        while (j <= right) temp[k++] = arr[j++];

        // Copy back
        Array.Copy(temp, left, arr, left, right - left + 1);
        return count;
    }

    static Dictionary<string, int> SaveOrders(LayoutGraph graph) =>
        graph.Nodes.Values.ToDictionary(n => n.Id, n => n.Order);

    static void RestoreOrders(LayoutGraph graph, Dictionary<string, int> orders)
    {
        foreach (var (id, order) in orders)
        {
            graph.GetNode(id)?.SetOrder(order);
        }
    }
}
