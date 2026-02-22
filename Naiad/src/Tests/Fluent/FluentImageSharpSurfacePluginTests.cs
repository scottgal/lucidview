using MermaidSharp.Fluent;
using MermaidSharp.Models;
using MermaidSharp.Rendering.Surfaces;

namespace MermaidSharp.Tests.Fluent;

[NonParallelizable]
public class FluentImageSharpSurfacePluginTests
{
    [TestCase(MermaidOutputFormat.Png, "image/png")]
    [TestCase(MermaidOutputFormat.Jpeg, "image/jpeg")]
    [TestCase(MermaidOutputFormat.Webp, "image/webp")]
    public void Export_WithRegisteredImageSharpPlugin_Succeeds(MermaidOutputFormat format, string mimeType)
    {
        MermaidRenderSurfaces.Unregister("imagesharp");
        MermaidRenderSurfacesImageSharpExtensions.RegisterImageSharpSurface();

        try
        {
            var diagram = MermaidFluent.Flowchart(Direction.LeftToRight, f => f
                    .Node("A", "Start")
                    .Node("B", "End")
                    .Edge("A", "B"))
                .Build();

            var result = MermaidFluent.Export(diagram, format);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Bytes, Is.Not.Null);
            Assert.That(result.Bytes!.Length, Is.GreaterThan(0));
            Assert.That(result.MimeType, Is.EqualTo(mimeType));
        }
        finally
        {
            MermaidRenderSurfaces.Unregister("imagesharp");
        }
    }

    [Test]
    public void OutputCapabilitiesFor_WithRegisteredImageSharpPlugin_ExposesRasterFormats()
    {
        MermaidRenderSurfaces.Unregister("imagesharp");
        MermaidRenderSurfacesImageSharpExtensions.RegisterImageSharpSurface();

        try
        {
            var capabilities = MermaidFluent.OutputCapabilitiesFor(DiagramType.Flowchart);

            Assert.That(capabilities.Supports(MermaidOutputFormat.Png), Is.True);
            Assert.That(capabilities.Supports(MermaidOutputFormat.Jpeg), Is.True);
            Assert.That(capabilities.Supports(MermaidOutputFormat.Webp), Is.True);
        }
        finally
        {
            MermaidRenderSurfaces.Unregister("imagesharp");
        }
    }
}
