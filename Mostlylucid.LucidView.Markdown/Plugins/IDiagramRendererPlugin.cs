using Avalonia;

namespace Mostlylucid.LucidView.Markdown.Plugins;

public interface IDiagramRendererPlugin
{
    string Name { get; }
    void ReplaceDiagramMarkers(Visual root);
}
