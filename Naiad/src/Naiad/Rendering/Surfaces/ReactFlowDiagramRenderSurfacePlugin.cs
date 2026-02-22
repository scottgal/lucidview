using System.Text.Json;
using System.Text.Json.Serialization;

namespace MermaidSharp.Rendering.Surfaces;

public sealed class ReactFlowDiagramRenderSurfacePlugin : IDiagramRenderSurfacePlugin
{
    static readonly IReadOnlyCollection<RenderSurfaceFormat> Formats =
        [RenderSurfaceFormat.ReactFlow];
    static readonly IReadOnlyCollection<DiagramType> DiagramTypes =
        [DiagramType.Flowchart];

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Name => "reactflow";
    public IReadOnlyCollection<RenderSurfaceFormat> SupportedFormats => Formats;
    public IReadOnlyCollection<DiagramType>? SupportedDiagramTypes => DiagramTypes;
    public bool Supports(RenderSurfaceFormat format) => format == RenderSurfaceFormat.ReactFlow;
    public bool Supports(RenderSurfaceContext context, RenderSurfaceRequest request) =>
        request.Format == RenderSurfaceFormat.ReactFlow && context.DiagramType == DiagramType.Flowchart;

    public RenderSurfaceOutput Render(RenderSurfaceContext context, RenderSurfaceRequest request)
    {
        if (context.DiagramType != DiagramType.Flowchart)
        {
            throw new MermaidException("ReactFlow surface currently supports flowchart diagrams only.");
        }

        var layout = Mermaid.ParseAndLayoutFlowchart(context.MermaidSource, context.RenderOptions);
        if (layout is null)
        {
            throw new MermaidException("ReactFlow surface failed to parse/layout flowchart.");
        }

        var nodes = layout.Model.Nodes.Select(ToReactFlowNode).ToList();
        var edges = layout.Model.Edges.Select((edge, index) => ToReactFlowEdge(edge, index)).ToList();
        var doc = new ReactFlowDocument(nodes, edges);
        var json = JsonSerializer.Serialize(doc, JsonOptions);
        return RenderSurfaceOutput.FromText(json, "application/json");
    }

    static ReactFlowNode ToReactFlowNode(Node node)
    {
        var style = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(node.Style.Fill))
        {
            style["background"] = node.Style.Fill;
        }

        if (!string.IsNullOrWhiteSpace(node.Style.Stroke))
        {
            style["borderColor"] = node.Style.Stroke;
        }

        if (node.Style.StrokeWidth.HasValue)
        {
            style["borderWidth"] = node.Style.StrokeWidth.Value;
        }

        if (!string.IsNullOrWhiteSpace(node.Style.TextColor))
        {
            style["color"] = node.Style.TextColor;
        }

        if (node.Style.FontSize.HasValue)
        {
            style["fontSize"] = node.Style.FontSize.Value;
        }

        var x = node.Position.X - (node.Width / 2d);
        var y = node.Position.Y - (node.Height / 2d);
        var label = string.IsNullOrWhiteSpace(node.Label) ? node.Id : node.DisplayLabel;
        return new ReactFlowNode(
            node.Id,
            new ReactFlowPosition(x, y),
            new ReactFlowNodeData(label),
            node.IsGroup ? "group" : "default",
            node.Width,
            node.Height,
            node.ParentId,
            node.ParentId is null ? null : "parent",
            style.Count == 0 ? null : style);
    }

    static ReactFlowEdge ToReactFlowEdge(Edge edge, int index)
    {
        var style = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(edge.Style.Stroke))
        {
            style["stroke"] = edge.Style.Stroke;
        }

        if (edge.Style.StrokeWidth.HasValue)
        {
            style["strokeWidth"] = edge.Style.StrokeWidth.Value;
        }

        if (!string.IsNullOrWhiteSpace(edge.Style.StrokeDasharray))
        {
            style["strokeDasharray"] = edge.Style.StrokeDasharray;
        }

        if (edge.LineStyle == EdgeStyle.Dotted && !style.ContainsKey("strokeDasharray"))
        {
            style["strokeDasharray"] = "3 3";
        }

        return new ReactFlowEdge(
            $"{edge.SourceId}__{edge.TargetId}__{index}",
            edge.SourceId,
            edge.TargetId,
            "default",
            edge.Label,
            edge.LineStyle == EdgeStyle.Dotted,
            style.Count == 0 ? null : style);
    }

    sealed record ReactFlowDocument(
        IReadOnlyList<ReactFlowNode> Nodes,
        IReadOnlyList<ReactFlowEdge> Edges);

    sealed record ReactFlowNode(
        string Id,
        ReactFlowPosition Position,
        ReactFlowNodeData Data,
        string Type,
        double Width,
        double Height,
        [property: JsonPropertyName("parentNode")] string? ParentNode,
        string? Extent,
        IReadOnlyDictionary<string, object?>? Style);

    sealed record ReactFlowNodeData(string Label);

    sealed record ReactFlowPosition(double X, double Y);

    sealed record ReactFlowEdge(
        string Id,
        string Source,
        string Target,
        string Type,
        string? Label,
        bool Animated,
        IReadOnlyDictionary<string, object?>? Style);
}
