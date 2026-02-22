using MermaidSharp.Fluent;
using MermaidSharp.Models;
using MermaidSharp.Rendering.Surfaces;

namespace MermaidSharp.Tests.Fluent;

[NonParallelizable]
public class FluentSkiaSurfacePluginTests
{
    [Test]
    public void Export_Png_WithRegisteredSkiaPlugin_Succeeds()
    {
        // Ensure idempotent state for this test.
        MermaidRenderSurfaces.Unregister("skia");
        MermaidRenderSurfacesSkiaExtensions.RegisterSkiaSurface();

        try
        {
            var diagram = MermaidFluent.Flowchart(Direction.LeftToRight, f => f
                    .Node("A", "Start")
                    .Node("B", "End")
                    .Edge("A", "B"))
                .Build();

            var result = MermaidFluent.Export(diagram, MermaidOutputFormat.Png);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Bytes, Is.Not.Null);
            Assert.That(result.Bytes!.Length, Is.GreaterThan(0));
        }
        finally
        {
            MermaidRenderSurfaces.Unregister("skia");
        }
    }
}
