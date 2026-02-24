using Avalonia.Controls;
using MarkdownViewer.Controls;
using MarkdownViewer.Plugins;
using MarkdownViewer.Services;

namespace MarkdownViewer.Tests;

public class AvaloniaNativeDiagramRendererPluginTests
{
    [Fact]
    public void ReplaceDiagramMarkers_ReplacesFlowchartMarkerInPanel()
    {
        var service = new MarkdownService();
        service.ProcessMarkdown("""
                               ```mermaid
                               flowchart TD
                                   A --> B
                               ```
                               """);

        var plugin = CreatePlugin(service);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "FLOWCHART:flowchart-0" });

        plugin.ReplaceDiagramMarkers(panel);

        Assert.IsType<FlowchartCanvas>(panel.Children[0]);
    }

    [Fact]
    public void ReplaceDiagramMarkers_ReplacesDiagramMarkerInPanel()
    {
        var service = new MarkdownService();
        service.ProcessMarkdown("""
                               ```mermaid
                               sequenceDiagram
                                   participant A
                                   participant B
                                   A->>B: Ping
                               ```
                               """);

        var plugin = CreatePlugin(service);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "DIAGRAM:diagram-0" });

        plugin.ReplaceDiagramMarkers(panel);

        Assert.IsType<DiagramCanvas>(panel.Children[0]);
    }

    static AvaloniaNativeDiagramRendererPlugin CreatePlugin(MarkdownService service) =>
        new(
            service,
            resolveDiagramTextBrush: () => null,
            saveDiagramAs: (_, _) => Task.CompletedTask);
}
