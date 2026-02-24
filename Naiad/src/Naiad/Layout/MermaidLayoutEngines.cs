namespace MermaidSharp.Layout;

public static class LayoutEngineNames
{
    public const string Dagre = "dagre";
}

public interface ILayoutEnginePlugin
{
    string Name { get; }
    IReadOnlyCollection<string> Aliases { get; }
    IReadOnlyCollection<DiagramType>? SupportedDiagramTypes { get; }
    ILayoutEngine Create();

    bool Supports(DiagramType diagramType)
    {
        var supportedDiagramTypes = SupportedDiagramTypes;
        return supportedDiagramTypes is null || supportedDiagramTypes.Contains(diagramType);
    }
}

public sealed class StaticLayoutEnginePlugin(
    string name,
    Func<ILayoutEngine> factory,
    IReadOnlyCollection<string>? aliases = null,
    IReadOnlyCollection<DiagramType>? supportedDiagramTypes = null) : ILayoutEnginePlugin
{
    public string Name { get; } = name;
    public IReadOnlyCollection<string> Aliases { get; } = aliases ?? [];
    public IReadOnlyCollection<DiagramType>? SupportedDiagramTypes { get; } = supportedDiagramTypes;

    public ILayoutEngine Create() => factory();
}

public sealed class LayoutEngineRegistry
{
    readonly object _sync = new();
    readonly List<ILayoutEnginePlugin> _plugins = [];

    public IReadOnlyCollection<ILayoutEnginePlugin> Plugins
    {
        get
        {
            lock (_sync)
            {
                return [.. _plugins];
            }
        }
    }

    public bool TryRegister(ILayoutEnginePlugin plugin, out string? error)
    {
        try
        {
            Register(plugin);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Register(ILayoutEnginePlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (string.IsNullOrWhiteSpace(plugin.Name))
            throw new ArgumentException("Layout engine plugin name cannot be empty.", nameof(plugin));

        var pluginIdentifiers = GetIdentifiers(plugin)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (_sync)
        {
            _plugins.RemoveAll(existing =>
                GetIdentifiers(existing).Any(id => pluginIdentifiers.Contains(id)));
            _plugins.Insert(0, plugin);
        }
    }

    public bool Unregister(string pluginName)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
            return false;

        lock (_sync)
        {
            return _plugins.RemoveAll(x => MatchesIdentifier(x, pluginName)) > 0;
        }
    }

    public IReadOnlyList<string> GetAvailableEngineNames() =>
        GetAvailableEngineNames(null);

    public IReadOnlyList<string> GetAvailableEngineNames(DiagramType? diagramType)
    {
        lock (_sync)
        {
            return
            [
                .. _plugins
                    .Where(x => diagramType is null || x.Supports(diagramType.Value))
                    .Select(x => x.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];
        }
    }

    public ILayoutEngine Resolve(DiagramType diagramType, string? preferredEngine = null)
    {
        ILayoutEnginePlugin? plugin;
        IReadOnlyList<string> availableEngineNames;

        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(preferredEngine))
            {
                plugin = _plugins.FirstOrDefault(x =>
                    MatchesIdentifier(x, preferredEngine) &&
                    x.Supports(diagramType));

                if (plugin is null)
                {
                    availableEngineNames =
                    [
                        .. _plugins
                            .Where(x => x.Supports(diagramType))
                            .Select(x => x.Name)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                    ];

                    var availableText = availableEngineNames.Count == 0
                        ? "none"
                        : string.Join(", ", availableEngineNames);
                    throw new MermaidException(
                        $"Layout engine '{preferredEngine}' is not available for diagram type '{diagramType}'. Available: {availableText}.");
                }
            }
            else
            {
                plugin = _plugins.FirstOrDefault(x => x.Supports(diagramType));
                if (plugin is null)
                    throw new MermaidException(
                        $"No layout engine is registered for diagram type '{diagramType}'.");
            }
        }

        return plugin.Create();
    }

    static IEnumerable<string> GetIdentifiers(ILayoutEnginePlugin plugin)
    {
        yield return plugin.Name;
        foreach (var alias in plugin.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
                yield return alias;
        }
    }

    static bool MatchesIdentifier(ILayoutEnginePlugin plugin, string id) =>
        string.Equals(plugin.Name, id, StringComparison.OrdinalIgnoreCase) ||
        plugin.Aliases.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
}

public static class MermaidLayoutEngines
{
    static readonly LayoutEngineRegistry Registry = CreateDefaultRegistry();
    static readonly IReadOnlyDictionary<DiagramType, string> RecommendedByDiagramType =
        new Dictionary<DiagramType, string>
        {
            [DiagramType.Flowchart] = LayoutEngineNames.Dagre,
            [DiagramType.Bpmn] = LayoutEngineNames.Dagre,
            [DiagramType.Class] = LayoutEngineNames.Dagre,
            [DiagramType.State] = LayoutEngineNames.Dagre,
            [DiagramType.EntityRelationship] = LayoutEngineNames.Dagre
        };

    public static IReadOnlyCollection<ILayoutEnginePlugin> Plugins => Registry.Plugins;

    public static IReadOnlyList<string> GetAvailableEngineNames() =>
        Registry.GetAvailableEngineNames();

    public static IReadOnlyList<string> GetAvailableEngineNames(DiagramType diagramType) =>
        Registry.GetAvailableEngineNames(diagramType);

    public static bool TryRegister(ILayoutEnginePlugin plugin, out string? error) =>
        Registry.TryRegister(plugin, out error);

    public static void Register(ILayoutEnginePlugin plugin) => Registry.Register(plugin);

    public static bool Unregister(string pluginName) => Registry.Unregister(pluginName);

    public static ILayoutEngine Resolve(DiagramType diagramType, string? preferredEngine = null) =>
        Registry.Resolve(diagramType, preferredEngine);

    public static string? GetRecommendedEngineName(DiagramType diagramType) =>
        RecommendedByDiagramType.GetValueOrDefault(diagramType);

    public static ILayoutEngine ResolveBest(DiagramType diagramType)
    {
        var recommended = GetRecommendedEngineName(diagramType);
        if (!string.IsNullOrWhiteSpace(recommended))
        {
            try
            {
                return Registry.Resolve(diagramType, recommended);
            }
            catch (MermaidException)
            {
                // Recommended engine is unavailable; fall back to first compatible plugin.
            }
        }

        return Registry.Resolve(diagramType);
    }

    static LayoutEngineRegistry CreateDefaultRegistry()
    {
        var registry = new LayoutEngineRegistry();
        // MostlylucidDagreLayoutEngine uses mostlylucid.dagre (our .NET dagre port)
        // Registered as "dagre" - the primary and only dagre engine
        registry.Register(new StaticLayoutEnginePlugin(
            LayoutEngineNames.Dagre,
            static () => new MostlylucidDagreLayoutEngine(),
            aliases: ["dagre-net", "dagrenet", "dagrejs", "dagre-js"]));
        return registry;
    }
}
