namespace MermaidSharp.Fluent;

public sealed class FlowchartBuilder
{
    readonly Dictionary<string, FlowchartNode> _nodes = new(StringComparer.Ordinal);
    readonly List<string> _nodeOrder = [];
    readonly List<FlowchartEdge> _edges = [];
    readonly List<FlowchartSubgraph> _subgraphs = [];
    readonly Dictionary<string, FlowchartClassDefinition> _classDefinitions = new(StringComparer.Ordinal);
    readonly List<string> _classDefinitionOrder = [];
    readonly List<FlowchartClassAssignment> _classAssignments = [];

    Direction _direction;

    public FlowchartBuilder(Direction direction = MermaidSharp.Models.Direction.TopToBottom) => _direction = direction;

    public FlowchartBuilder Direction(Direction direction)
    {
        _direction = direction;
        return this;
    }

    public FlowchartBuilder Node(string id, string? text = null, NodeShape shape = NodeShape.Rectangle)
    {
        if (!_nodes.ContainsKey(id))
        {
            _nodeOrder.Add(id);
        }

        _nodes[id] = new FlowchartNode(id, text, shape);
        return this;
    }

    public FlowchartBuilder Edge(string from, string to, EdgeType type = EdgeType.Arrow, string? label = null)
    {
        _edges.Add(new FlowchartEdge(from, to, type, label));
        return this;
    }

    public FlowchartBuilder Subgraph(string id, string title, Action<SubgraphBuilder> configure)
    {
        var builder = new SubgraphBuilder(id, title);
        configure(builder);
        _subgraphs.Add(builder.Build());
        return this;
    }

    public FlowchartBuilder ClassDef(string name, Action<ClassDefBuilder> configure)
    {
        var builder = new ClassDefBuilder();
        configure(builder);
        var classDef = builder.Build(name);
        if (!_classDefinitions.ContainsKey(name))
        {
            _classDefinitionOrder.Add(name);
        }

        _classDefinitions[name] = classDef;
        return this;
    }

    public FlowchartBuilder Class(string nodeOrSubgraphId, params string[] classNames)
    {
        foreach (var className in classNames.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            _classAssignments.Add(new FlowchartClassAssignment(nodeOrSubgraphId, className));
        }

        return this;
    }

    public FlowchartDiagram Build()
    {
        var nodes = _nodeOrder.Select(id => _nodes[id]).ToList();
        var classDefs = _classDefinitionOrder.Select(name => _classDefinitions[name]).ToList();
        return new FlowchartDiagram(
            _direction,
            nodes,
            _edges,
            _subgraphs,
            classDefs,
            _classAssignments);
    }
}

public sealed class SubgraphBuilder
{
    readonly string _id;
    readonly string _title;
    readonly Dictionary<string, FlowchartNode> _nodes = new(StringComparer.Ordinal);
    readonly List<string> _nodeOrder = [];
    readonly List<FlowchartEdge> _edges = [];
    readonly List<FlowchartSubgraph> _subgraphs = [];

    Direction? _direction;

    internal SubgraphBuilder(string id, string title)
    {
        _id = id;
        _title = title;
    }

    public SubgraphBuilder Direction(Direction direction)
    {
        _direction = direction;
        return this;
    }

    public SubgraphBuilder Node(string id, string? text = null, NodeShape shape = NodeShape.Rectangle)
    {
        if (!_nodes.ContainsKey(id))
        {
            _nodeOrder.Add(id);
        }

        _nodes[id] = new FlowchartNode(id, text, shape);
        return this;
    }

    public SubgraphBuilder Edge(string from, string to, EdgeType type = EdgeType.Arrow, string? label = null)
    {
        _edges.Add(new FlowchartEdge(from, to, type, label));
        return this;
    }

    public SubgraphBuilder Subgraph(string id, string title, Action<SubgraphBuilder> configure)
    {
        var builder = new SubgraphBuilder(id, title);
        configure(builder);
        _subgraphs.Add(builder.Build());
        return this;
    }

    internal FlowchartSubgraph Build()
    {
        var nodes = _nodeOrder.Select(id => _nodes[id]).ToList();
        return new FlowchartSubgraph(
            _id,
            _title,
            _direction,
            nodes,
            _edges,
            _subgraphs);
    }
}

public sealed class ClassDefBuilder
{
    readonly Dictionary<string, string> _properties = new(StringComparer.Ordinal);
    readonly List<string> _propertyOrder = [];

    public ClassDefBuilder Set(string propertyName, string value)
    {
        if (!_properties.ContainsKey(propertyName))
        {
            _propertyOrder.Add(propertyName);
        }

        _properties[propertyName] = value;
        return this;
    }

    public ClassDefBuilder Fill(string color) => Set("fill", color);
    public ClassDefBuilder Stroke(string color) => Set("stroke", color);
    public ClassDefBuilder StrokeWidth(double width) => Set("stroke-width", width.ToString(CultureInfo.InvariantCulture));
    public ClassDefBuilder StrokeDasharray(string value) => Set("stroke-dasharray", value);
    public ClassDefBuilder Color(string color) => Set("color", color);
    public ClassDefBuilder FontFamily(string family) => Set("font-family", family);
    public ClassDefBuilder FontSize(double sizePx) => Set("font-size", sizePx.ToString(CultureInfo.InvariantCulture));
    public ClassDefBuilder FontWeight(string value) => Set("font-weight", value);

    internal FlowchartClassDefinition Build(string name)
    {
        var props = _propertyOrder
            .Select(key => new KeyValuePair<string, string>(key, _properties[key]))
            .ToList();
        return new FlowchartClassDefinition(name, props);
    }
}
