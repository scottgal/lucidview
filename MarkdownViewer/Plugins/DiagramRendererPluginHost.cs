using Avalonia;

namespace MarkdownViewer.Plugins;

public sealed class DiagramRendererPluginHost(IEnumerable<IDiagramRendererPlugin> plugins)
{
    readonly List<IDiagramRendererPlugin> _plugins = plugins.ToList();

    public IReadOnlyList<IDiagramRendererPlugin> Plugins => _plugins;

    public void ReplaceDiagramMarkers(Visual root)
    {
        foreach (var plugin in _plugins)
        {
            plugin.ReplaceDiagramMarkers(root);
        }
    }
}
