using Avalonia;
using MarkdownViewer.Plugins;

namespace MarkdownViewer.Tests;

public class DiagramRendererPluginHostTests
{
    [Fact]
    public void ReplaceDiagramMarkers_InvokesAllRegisteredPlugins()
    {
        var pluginA = new StubPlugin("a");
        var pluginB = new StubPlugin("b");
        var host = new DiagramRendererPluginHost([pluginA, pluginB]);
        var root = new TestRootVisual();

        host.ReplaceDiagramMarkers(root);

        Assert.Equal(1, pluginA.CallCount);
        Assert.Equal(1, pluginB.CallCount);
        Assert.Same(root, pluginA.LastRoot);
        Assert.Same(root, pluginB.LastRoot);
    }

    sealed class StubPlugin(string name) : IDiagramRendererPlugin
    {
        public string Name { get; } = name;
        public int CallCount { get; private set; }
        public Visual? LastRoot { get; private set; }

        public void ReplaceDiagramMarkers(Visual root)
        {
            CallCount++;
            LastRoot = root;
        }
    }

    sealed class TestRootVisual : Visual
    {
    }
}
