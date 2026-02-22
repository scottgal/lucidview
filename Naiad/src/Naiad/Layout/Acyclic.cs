namespace MermaidSharp.Layout;

static class Acyclic
{
    public static void Run(LayoutGraph graph)
    {
        var visited = new HashSet<string>();
        var stack = new HashSet<string>();
        var edgesToReverse = new List<LayoutEdge>();

        foreach (var node in graph.Nodes.Values)
        {
            if (!visited.Contains(node.Id))
            {
                Dfs(graph, node.Id, visited, stack, edgesToReverse);
            }
        }

        // Reverse back edges to break cycles
        foreach (var edge in edgesToReverse)
        {
            edge.IsReversed = true;
            var source = graph.GetNode(edge.SourceId);
            var target = graph.GetNode(edge.TargetId);
            if (source is not null && target is not null)
            {
                // Remove from old adjacency lists
                source.OutEdges.Remove(edge);
                target.InEdges.Remove(edge);
                // Swap source/target IDs so GetSuccessors/GetPredecessors work correctly
                (edge.SourceId, edge.TargetId) = (edge.TargetId, edge.SourceId);
                // Add to new adjacency lists (source and target are still the original nodes)
                target.OutEdges.Add(edge);  // old target is new source
                source.InEdges.Add(edge);   // old source is new target
            }
        }
    }

    static void Dfs(LayoutGraph graph, string nodeId, HashSet<string> visited,
        HashSet<string> stack, List<LayoutEdge> edgesToReverse)
    {
        visited.Add(nodeId);
        stack.Add(nodeId);

        var node = graph.GetNode(nodeId);
        if (node is null)
        {
            return;
        }

        foreach (var edge in node.OutEdges.ToList())
        {
            if (stack.Contains(edge.TargetId))
            {
                // Back edge found - this creates a cycle
                edgesToReverse.Add(edge);
            }
            else if (!visited.Contains(edge.TargetId))
            {
                Dfs(graph, edge.TargetId, visited, stack, edgesToReverse);
            }
        }

        stack.Remove(nodeId);
    }

    public static void Undo(LayoutGraph graph)
    {
        foreach (var edge in graph.Edges.Where(e => e.IsReversed))
        {
            var source = graph.GetNode(edge.SourceId);
            var target = graph.GetNode(edge.TargetId);
            if (source is not null && target is not null)
            {
                // Remove from current adjacency lists
                source.OutEdges.Remove(edge);
                target.InEdges.Remove(edge);
                // Swap IDs back to original direction
                (edge.SourceId, edge.TargetId) = (edge.TargetId, edge.SourceId);
                // Add to original adjacency lists
                var newSource = graph.GetNode(edge.SourceId);
                var newTarget = graph.GetNode(edge.TargetId);
                newSource?.OutEdges.Add(edge);
                newTarget?.InEdges.Add(edge);
            }
            edge.IsReversed = false;
        }
    }
}
