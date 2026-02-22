using System.Collections.ObjectModel;

namespace MermaidSharp.Fluent;

public sealed record FlowchartNode(
    string Id,
    string? Text = null,
    NodeShape Shape = NodeShape.Rectangle);

public sealed record FlowchartEdge(
    string From,
    string To,
    EdgeType Type = EdgeType.Arrow,
    string? Label = null);

public sealed record FlowchartClassDefinition(
    string Name,
    IReadOnlyList<KeyValuePair<string, string>> Properties);

public sealed record FlowchartClassAssignment(
    string TargetId,
    string ClassName);

public sealed record FlowchartSubgraph(
    string Id,
    string Title,
    Direction? Direction,
    IReadOnlyList<FlowchartNode> Nodes,
    IReadOnlyList<FlowchartEdge> Edges,
    IReadOnlyList<FlowchartSubgraph> Subgraphs);

public sealed class FlowchartDiagram : MermaidDiagram
{
    public FlowchartDiagram(
        Direction direction,
        IEnumerable<FlowchartNode> nodes,
        IEnumerable<FlowchartEdge> edges,
        IEnumerable<FlowchartSubgraph> subgraphs,
        IEnumerable<FlowchartClassDefinition> classDefinitions,
        IEnumerable<FlowchartClassAssignment> classAssignments)
    {
        Direction = direction;
        Nodes = new ReadOnlyCollection<FlowchartNode>([.. nodes]);
        Edges = new ReadOnlyCollection<FlowchartEdge>([.. edges]);
        Subgraphs = new ReadOnlyCollection<FlowchartSubgraph>([.. subgraphs]);
        ClassDefinitions = new ReadOnlyCollection<FlowchartClassDefinition>([.. classDefinitions]);
        ClassAssignments = new ReadOnlyCollection<FlowchartClassAssignment>([.. classAssignments]);
    }

    public override DiagramType DiagramType => DiagramType.Flowchart;
    public Direction Direction { get; }
    public IReadOnlyList<FlowchartNode> Nodes { get; }
    public IReadOnlyList<FlowchartEdge> Edges { get; }
    public IReadOnlyList<FlowchartSubgraph> Subgraphs { get; }
    public IReadOnlyList<FlowchartClassDefinition> ClassDefinitions { get; }
    public IReadOnlyList<FlowchartClassAssignment> ClassAssignments { get; }

    public override string ToMermaid(SerializeOptions? options = null) =>
        FlowchartMermaidSerializer.Serialize(this, options);

    public static FlowchartDiagram FromModel(FlowchartModel model)
    {
        var nodeById = model.Nodes.ToDictionary(x => x.Id, StringComparer.Ordinal);

        var subgraphRoots = model.Subgraphs
            .Select(x => ConvertSubgraph(x, nodeById))
            .ToList();

        var rootNodes = model.Nodes
            .Where(x => string.IsNullOrWhiteSpace(x.ParentId))
            .Select(x => new FlowchartNode(x.Id, x.Label, x.Shape))
            .ToList();

        var edges = model.Edges
            .Select(x => new FlowchartEdge(x.SourceId, x.TargetId, x.Type, x.Label))
            .ToList();

        var classAssignments = model.Classes
            .Select(x => new FlowchartClassAssignment(x.Key, x.Value))
            .ToList();

        return new FlowchartDiagram(
            model.Direction,
            rootNodes,
            edges,
            subgraphRoots,
            [],
            classAssignments);
    }

    static FlowchartSubgraph ConvertSubgraph(Subgraph subgraph, IReadOnlyDictionary<string, Node> nodeById)
    {
        var nodes = subgraph.NodeIds
            .Select(id =>
            {
                if (nodeById.TryGetValue(id, out var node))
                {
                    return new FlowchartNode(node.Id, node.Label, node.Shape);
                }

                return new FlowchartNode(id);
            })
            .ToList();

        var nested = subgraph.NestedSubgraphs
            .Select(x => ConvertSubgraph(x, nodeById))
            .ToList();

        var title = string.IsNullOrWhiteSpace(subgraph.Title) ? subgraph.Id : subgraph.Title;
        return new FlowchartSubgraph(subgraph.Id, title!, subgraph.Direction, nodes, [], nested);
    }
}

static class FlowchartMermaidSerializer
{
    public static string Serialize(FlowchartDiagram diagram, SerializeOptions? options = null)
    {
        options ??= new SerializeOptions();
        var sb = new StringBuilder();
        var nl = options.NewLine;

        sb.Append("flowchart ").Append(DirectionToToken(diagram.Direction)).Append(nl);

        foreach (var node in diagram.Nodes)
        {
            sb.Append(options.Indent).Append(NodeToToken(node)).Append(nl);
        }

        foreach (var subgraph in diagram.Subgraphs)
        {
            AppendSubgraph(sb, subgraph, 1, options);
        }

        foreach (var edge in diagram.Edges)
        {
            sb.Append(options.Indent).Append(EdgeToToken(edge)).Append(nl);
        }

        foreach (var classDef in diagram.ClassDefinitions)
        {
            var css = string.Join(
                ",",
                classDef.Properties.Select(x => $"{x.Key}:{x.Value}"));
            sb.Append(options.Indent)
                .Append("classDef ")
                .Append(classDef.Name)
                .Append(' ')
                .Append(css)
                .Append(nl);
        }

        foreach (var assignment in diagram.ClassAssignments)
        {
            sb.Append(options.Indent)
                .Append("class ")
                .Append(assignment.TargetId)
                .Append(' ')
                .Append(assignment.ClassName)
                .Append(nl);
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    static void AppendSubgraph(
        StringBuilder sb,
        FlowchartSubgraph subgraph,
        int level,
        SerializeOptions options)
    {
        var indent = string.Concat(Enumerable.Repeat(options.Indent, level));
        var childIndent = string.Concat(Enumerable.Repeat(options.Indent, level + 1));

        sb.Append(indent)
            .Append("subgraph ")
            .Append(SubgraphToHeader(subgraph))
            .Append(options.NewLine);

        if (subgraph.Direction is not null)
        {
            sb.Append(childIndent)
                .Append("direction ")
                .Append(DirectionToToken(subgraph.Direction.Value))
                .Append(options.NewLine);
        }

        foreach (var node in subgraph.Nodes)
        {
            sb.Append(childIndent).Append(NodeToToken(node)).Append(options.NewLine);
        }

        foreach (var nested in subgraph.Subgraphs)
        {
            AppendSubgraph(sb, nested, level + 1, options);
        }

        foreach (var edge in subgraph.Edges)
        {
            sb.Append(childIndent).Append(EdgeToToken(edge)).Append(options.NewLine);
        }

        sb.Append(indent).Append("end").Append(options.NewLine);
    }

    static string SubgraphToHeader(FlowchartSubgraph subgraph)
    {
        if (string.Equals(subgraph.Id, subgraph.Title, StringComparison.Ordinal))
        {
            return subgraph.Id;
        }

        return $"{subgraph.Id}[{Quote(subgraph.Title)}]";
    }

    static string NodeToToken(FlowchartNode node)
    {
        if (string.IsNullOrWhiteSpace(node.Text))
        {
            return node.Id;
        }

        var label = Quote(node.Text!);
        return node.Shape switch
        {
            NodeShape.RoundedRectangle => $"{node.Id}({label})",
            NodeShape.Stadium => $"{node.Id}([{label}])",
            NodeShape.Subroutine => $"{node.Id}[[{label}]]",
            NodeShape.Cylinder => $"{node.Id}[({label})]",
            NodeShape.Circle => $"{node.Id}(({label}))",
            NodeShape.DoubleCircle => $"{node.Id}((({label})))",
            NodeShape.Asymmetric => $"{node.Id}>{label}]",
            NodeShape.Diamond => $"{node.Id}{{{label}}}",
            NodeShape.Hexagon => $"{node.Id}{{{{{label}}}}}",
            NodeShape.Parallelogram => $"{node.Id}[/{label}/]",
            NodeShape.ParallelogramAlt => $"{node.Id}[\\{label}\\]",
            NodeShape.Trapezoid => $"{node.Id}[/{label}/]",
            NodeShape.TrapezoidAlt => $"{node.Id}[\\{label}\\]",
            _ => $"{node.Id}[{label}]"
        };
    }

    static string EdgeToToken(FlowchartEdge edge)
    {
        var arrow = edge.Type switch
        {
            EdgeType.Arrow => "-->",
            EdgeType.Open => "---",
            EdgeType.DottedArrow => "-.->",
            EdgeType.Dotted => "-.-",
            EdgeType.ThickArrow => "==>",
            EdgeType.Thick => "===",
            EdgeType.CircleEnd => "--o",
            EdgeType.CrossEnd => "--x",
            EdgeType.BiDirectional => "<-->",
            EdgeType.BiDirectionalCircle => "o--o",
            EdgeType.BiDirectionalCross => "x--x",
            _ => "-->"
        };

        if (string.IsNullOrWhiteSpace(edge.Label))
        {
            return $"{edge.From} {arrow} {edge.To}";
        }

        return $"{edge.From} {arrow}|{EscapeEdgeLabel(edge.Label!)}| {edge.To}";
    }

    static string Quote(string text) =>
        $"\"{text.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    static string EscapeEdgeLabel(string label) =>
        label.Replace("|", "\\|", StringComparison.Ordinal);

    static string DirectionToToken(Direction direction) =>
        direction switch
        {
            Direction.TopToBottom => "TB",
            Direction.BottomToTop => "BT",
            Direction.LeftToRight => "LR",
            Direction.RightToLeft => "RL",
            _ => "TB"
        };
}
