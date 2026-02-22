using Avalonia;

namespace MarkdownViewer.Plugins;

public interface IDiagramRendererPlugin
{
    string Name { get; }
    void ReplaceDiagramMarkers(Visual root);
}
