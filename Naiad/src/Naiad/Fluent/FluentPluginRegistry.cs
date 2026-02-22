namespace MermaidSharp.Fluent;

public sealed class FluentPluginRegistry : IFluentPluginRegistry
{
    readonly Dictionary<DiagramType, IFluentDiagramPlugin> _plugins;

    public FluentPluginRegistry(IEnumerable<IFluentDiagramPlugin> plugins) =>
        _plugins = plugins.ToDictionary(x => x.DiagramType);

    public IReadOnlyCollection<IFluentDiagramPlugin> Plugins => _plugins.Values;

    public bool TryGet(DiagramType type, out IFluentDiagramPlugin plugin) =>
        _plugins.TryGetValue(type, out plugin!);

    public static FluentPluginRegistry CreateDefault()
    {
        var plugins = new List<IFluentDiagramPlugin>
        {
            new FlowchartFluentDiagramPlugin()
        };

        foreach (var diagramType in System.Enum.GetValues<DiagramType>())
        {
            if (diagramType == DiagramType.Flowchart)
            {
                continue;
            }

            plugins.Add(new UnsupportedFluentDiagramPlugin(diagramType));
        }

        return new FluentPluginRegistry(plugins);
    }
}
