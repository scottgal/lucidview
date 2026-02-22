namespace MermaidSharp.Rendering.Skins;

/// <summary>
/// Public shape template contract used by skin-pack plugins.
/// </summary>
public readonly record struct SkinPathLayerTemplate(
    string PathData,
    string? InlineStyle = null);

public readonly record struct SkinShapeTemplate(
    string PathData,
    double ViewBoxX,
    double ViewBoxY,
    double ViewBoxWidth,
    double ViewBoxHeight,
    string? InlineStyle = null,
    IReadOnlyList<SkinPathLayerTemplate>? Layers = null,
    string? DefsContent = null);

public interface IDiagramSkinPackPlugin
{
    string Name { get; }
    IReadOnlyCollection<string> Aliases { get; }
    IReadOnlyDictionary<string, SkinShapeTemplate> Shapes { get; }
}

/// <summary>
/// Simple immutable plugin implementation for static packs.
/// </summary>
public sealed class StaticDiagramSkinPackPlugin(
    string name,
    IReadOnlyDictionary<string, SkinShapeTemplate> shapes,
    IReadOnlyCollection<string>? aliases = null) : IDiagramSkinPackPlugin
{
    public string Name { get; } = name;
    public IReadOnlyCollection<string> Aliases { get; } = aliases ?? [];
    public IReadOnlyDictionary<string, SkinShapeTemplate> Shapes { get; } = shapes;
}

public static class MermaidSkinPacks
{
    public static IReadOnlyCollection<IDiagramSkinPackPlugin> Plugins => ShapeSkinCatalog.GetRegisteredPlugins();

    public static IReadOnlyList<string> GetRegisteredPackNames() => ShapeSkinCatalog.GetRegisteredPackNames();

    public static IReadOnlyList<string> GetAvailablePackNames() => ShapeSkinCatalog.GetAvailablePackNames();

    public static bool TryRegister(IDiagramSkinPackPlugin plugin, out string? error) =>
        ShapeSkinCatalog.TryRegisterPlugin(plugin, out error);

    public static void Register(IDiagramSkinPackPlugin plugin) => ShapeSkinCatalog.RegisterPlugin(plugin);

    public static bool Unregister(string pluginName) => ShapeSkinCatalog.UnregisterPlugin(pluginName);
}
