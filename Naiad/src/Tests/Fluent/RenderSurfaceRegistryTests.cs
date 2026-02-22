using MermaidSharp.Rendering.Surfaces;

namespace MermaidSharp.Tests.Fluent;

public class RenderSurfaceRegistryTests
{
    [Test]
    public void TryRender_ReactFlow_WithSequenceDiagram_ReturnsStructuredIncompatibleFailure()
    {
        const string source =
            """
            sequenceDiagram
                Alice->>Bob: Hello
            """;

        var request = new RenderSurfaceRequest(RenderSurfaceFormat.ReactFlow);
        var success = MermaidRenderSurfaces.TryRender(source, request, out var output, out RenderSurfaceFailure? failure);

        Assert.That(success, Is.False);
        Assert.That(output, Is.Null);
        Assert.That(failure, Is.Not.Null);
        Assert.That(failure!.Code, Is.EqualTo("RS002"));
        Assert.That(failure.Metadata?["requestedFormat"]?.ToString(), Is.EqualTo("ReactFlow"));
        Assert.That(failure.Metadata?["diagramType"]?.ToString(), Is.EqualTo("Sequence"));
    }

    [Test]
    public void RegisteringSamePluginName_ReplacesExistingRegistration()
    {
        var source = "flowchart LR\nA[Start] --> B[End]";
        var document = Mermaid.RenderToDocument(source);
        Assert.That(document, Is.Not.Null);

        var context = new RenderSurfaceContext(source, DiagramType.Flowchart, document!, null);
        var request = new RenderSurfaceRequest(RenderSurfaceFormat.Svg);

        var registry = new DiagramRenderSurfaceRegistry();
        registry.Register(new StubSurfacePlugin("dup", "first"));
        registry.Register(new StubSurfacePlugin("dup", "second"));

        var success = registry.TryRender(context, request, out var output, out var failure);

        Assert.That(success, Is.True);
        Assert.That(failure, Is.Null);
        Assert.That(output?.Text, Is.EqualTo("second"));
        Assert.That(registry.Plugins.Count(x => x.Name == "dup"), Is.EqualTo(1));
    }

    sealed class StubSurfacePlugin(string name, string payload) : IDiagramRenderSurfacePlugin
    {
        static readonly IReadOnlyCollection<RenderSurfaceFormat> Formats = [RenderSurfaceFormat.Svg];

        public string Name => name;
        public IReadOnlyCollection<RenderSurfaceFormat> SupportedFormats => Formats;
        public bool Supports(RenderSurfaceFormat format) => format == RenderSurfaceFormat.Svg;
        public RenderSurfaceOutput Render(RenderSurfaceContext context, RenderSurfaceRequest request) =>
            RenderSurfaceOutput.FromText(payload, "text/plain");
    }
}
