using Avalonia;

namespace Mostlylucid.LucidView.Markdown.Plugins;

public sealed class DiagramRendererPluginHost(IEnumerable<IDiagramRendererPlugin> plugins)
{
    readonly List<IDiagramRendererPlugin> _plugins = plugins.ToList();

    public IReadOnlyList<IDiagramRendererPlugin> Plugins => _plugins;

    /// <summary>Runs every plugin over the tree and returns the total number of markers replaced.</summary>
    public int ReplaceDiagramMarkers(Visual root)
    {
        var replaced = 0;
        foreach (var plugin in _plugins)
        {
            replaced += plugin.ReplaceDiagramMarkers(root);
        }

        return replaced;
    }
}
