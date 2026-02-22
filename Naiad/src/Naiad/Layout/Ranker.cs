namespace MermaidSharp.Layout;

static class Ranker
{
    public static void Run(LayoutGraph graph, RankerType rankerType)
    {
        switch (rankerType)
        {
            case RankerType.LongestPath:
                LongestPath(graph);
                break;
            case RankerType.TightTree:
                TightTree(graph);
                break;
            case RankerType.NetworkSimplex:
                NetworkSimplex(graph);
                break;
        }

        NormalizeRanks(graph);
        InsertDummyNodes(graph);
    }

    static void LongestPath(LayoutGraph graph)
    {
        var visited = new HashSet<string>();

        foreach (var node in graph.Nodes.Values)
        {
            if (!visited.Contains(node.Id))
            {
                DfsLongestPath(graph, node, visited);
            }
        }
    }

    static int DfsLongestPath(LayoutGraph graph, LayoutNode startNode, HashSet<string> visited)
    {
        var resultRanks = new Dictionary<string, int>();
        var dfsStack = new Stack<string>();
        var processed = new HashSet<string>();
        
        dfsStack.Push(startNode.Id);
        
        while (dfsStack.Count > 0)
        {
            var nodeId = dfsStack.Peek();
            
            if (processed.Contains(nodeId))
            {
                dfsStack.Pop();
                continue;
            }
            
            if (resultRanks.ContainsKey(nodeId))
            {
                dfsStack.Pop();
                continue;
            }
            
            var node = graph.GetNode(nodeId);
            if (node is null)
            {
                dfsStack.Pop();
                continue;
            }
            
            var predecessors = graph.GetPredecessors(nodeId).ToList();
            var allPredsReady = true;
            var maxPredRank = -1;
            
            foreach (var pred in predecessors)
            {
                if (!resultRanks.TryGetValue(pred.Id, out var predRank))
                {
                    allPredsReady = false;
                    if (!processed.Contains(pred.Id))
                    {
                        dfsStack.Push(pred.Id);
                    }
                }
                else
                {
                    maxPredRank = Math.Max(maxPredRank, predRank);
                }
            }
            
            if (allPredsReady)
            {
                node.Rank = maxPredRank + 1;
                resultRanks[nodeId] = node.Rank;
                visited.Add(nodeId);
                processed.Add(nodeId);
                dfsStack.Pop();
            }
            else
            {
                processed.Add(nodeId);
            }
        }
        
        return resultRanks.TryGetValue(startNode.Id, out var result) ? result : startNode.Rank;
    }

    static void TightTree(LayoutGraph graph)
    {
        // Tight tree is similar to longest path but considers edge weights
        // For simplicity, we'll use longest path with slight optimization
        LongestPath(graph);

        // Pull nodes down to minimize edge length where possible
        bool changed;
        do
        {
            changed = false;
            foreach (var node in graph.Nodes.Values.OrderByDescending(n => n.Rank))
            {
                var successors = graph.GetSuccessors(node.Id).ToList();
                if (successors.Count > 0)
                {
                    var minSuccRank = successors.Min(s => s.Rank);
                    var targetRank = minSuccRank - 1;
                    if (targetRank > node.Rank)
                    {
                        // Can we move this node down?
                        var predecessors = graph.GetPredecessors(node.Id).ToList();
                        var minAllowedRank = predecessors.Count > 0
                            ? predecessors.Max(p => p.Rank) + 1
                            : 0;

                        if (targetRank >= minAllowedRank)
                        {
                            node.Rank = targetRank;
                            changed = true;
                        }
                    }
                }
            }
        } while (changed);
    }

    static void NetworkSimplex(LayoutGraph graph) =>
        // Network simplex is complex - fall back to tight tree for now
        // Full implementation would use linear programming approach
        TightTree(graph);

    static void NormalizeRanks(LayoutGraph graph)
    {
        if (graph.Nodes.Count == 0)
        {
            return;
        }

        var minRank = graph.Nodes.Values.Min(n => n.Rank);
        foreach (var node in graph.Nodes.Values)
        {
            node.Rank -= minRank;
        }
    }

    static void InsertDummyNodes(LayoutGraph graph)
    {
        var edgesToProcess = graph.Edges.ToList();
        var dummyCount = 0;

        foreach (var edge in edgesToProcess)
        {
            var source = graph.GetNode(edge.SourceId);
            var target = graph.GetNode(edge.TargetId);
            if (source is null || target is null)
            {
                continue;
            }

            var rankDiff = target.Rank - source.Rank;
            if (rankDiff > 1)
            {
                // Need dummy nodes
                var prevNodeId = edge.SourceId;
                for (var r = source.Rank + 1; r < target.Rank; r++)
                {
                    var dummyId = $"_dummy_{dummyCount++}";
                    var dummy = new LayoutNode
                    {
                        Id = dummyId,
                        Width = 0,
                        Height = 0,
                        Rank = r,
                        IsDummy = true,
                        OriginalEdgeSource = edge.SourceId,
                        OriginalEdgeTarget = edge.TargetId
                    };
                    graph.AddNode(dummy);

                    var newEdge = new LayoutEdge
                    {
                        SourceId = prevNodeId,
                        TargetId = dummyId
                    };
                    graph.AddEdge(newEdge);

                    prevNodeId = dummyId;
                }

                // Connect last dummy to target
                var finalEdge = new LayoutEdge
                {
                    SourceId = prevNodeId,
                    TargetId = edge.TargetId
                };
                graph.AddEdge(finalEdge);

                // Remove original edge connections
                source.OutEdges.Remove(edge);
                target.InEdges.Remove(edge);
            }
        }
    }
}
