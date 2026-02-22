using MermaidSharp.Fluent;
using MermaidSharp.Models;
using MermaidSharp.Rendering.Surfaces;

namespace MermaidSharp.Tests.Fluent;

[NonParallelizable]
public class FluentOutputMatrixTests
{
    [SetUp]
    public void SetUp()
    {
        MermaidRenderSurfaces.Unregister("skia");
        MermaidRenderSurfaces.Unregister("imagesharp");
    }

    [TearDown]
    public void TearDown()
    {
        MermaidRenderSurfaces.Unregister("skia");
        MermaidRenderSurfaces.Unregister("imagesharp");
    }

    [Test]
    public void CoreOnly_OutputMatrix_IsExpected()
    {
        var diagram = CreateFlowchart();

        var mermaid = MermaidFluent.Export(diagram, MermaidOutputFormat.Mermaid);
        Assert.That(mermaid.Success, Is.True);
        Assert.That(mermaid.Text, Does.Contain("flowchart LR"));
        Assert.That(mermaid.MimeType, Is.EqualTo("text/vnd.mermaid"));

        var svg = MermaidFluent.Export(diagram, MermaidOutputFormat.Svg);
        Assert.That(svg.Success, Is.True);
        Assert.That(svg.Text, Does.Contain("<svg"));
        Assert.That(svg.MimeType, Is.EqualTo("image/svg+xml"));

        var xaml = MermaidFluent.Export(diagram, MermaidOutputFormat.Xaml);
        Assert.That(xaml.Success, Is.True);
        Assert.That(xaml.Text, Does.Contain("<Canvas"));
        Assert.That(xaml.MimeType, Is.EqualTo("application/xaml+xml"));

        var reactFlow = MermaidFluent.Export(diagram, MermaidOutputFormat.ReactFlow);
        Assert.That(reactFlow.Success, Is.True);
        Assert.That(reactFlow.Text, Does.Contain("\"nodes\""));
        Assert.That(reactFlow.Text, Does.Contain("\"edges\""));
        Assert.That(reactFlow.MimeType, Is.EqualTo("application/json"));

        AssertUnsupported(MermaidFluent.Export(diagram, MermaidOutputFormat.Png), "SER401");
        AssertUnsupported(MermaidFluent.Export(diagram, MermaidOutputFormat.Jpeg), "SER401");
        AssertUnsupported(MermaidFluent.Export(diagram, MermaidOutputFormat.Webp), "SER401");
        AssertUnsupported(MermaidFluent.Export(diagram, MermaidOutputFormat.Pdf), "SER401");
        AssertUnsupported(MermaidFluent.Export(diagram, MermaidOutputFormat.Xps), "SER401");
    }

    [Test]
    public void ImageSharpPlugin_OutputMatrix_IsExpected()
    {
        MermaidRenderSurfacesImageSharpExtensions.RegisterImageSharpSurface();
        var diagram = CreateFlowchart();

        AssertBinarySuccess(MermaidFluent.Export(diagram, MermaidOutputFormat.Png), "image/png");
        AssertBinarySuccess(MermaidFluent.Export(diagram, MermaidOutputFormat.Jpeg), "image/jpeg");
        AssertBinarySuccess(MermaidFluent.Export(diagram, MermaidOutputFormat.Webp), "image/webp");

        AssertUnsupported(MermaidFluent.Export(diagram, MermaidOutputFormat.Pdf), "SER401");
        AssertUnsupported(MermaidFluent.Export(diagram, MermaidOutputFormat.Xps), "SER401");
    }

    [Test]
    public void SkiaPlugin_OutputMatrix_IsExpected()
    {
        MermaidRenderSurfacesSkiaExtensions.RegisterSkiaSurface();
        var diagram = CreateFlowchart();

        AssertBinarySuccess(MermaidFluent.Export(diagram, MermaidOutputFormat.Png), "image/png");
        AssertBinarySuccess(MermaidFluent.Export(diagram, MermaidOutputFormat.Jpeg), "image/jpeg");
        AssertBinarySuccess(MermaidFluent.Export(diagram, MermaidOutputFormat.Webp), "image/webp");
        AssertBinarySuccess(MermaidFluent.Export(diagram, MermaidOutputFormat.Pdf), "application/pdf");

        AssertUnsupported(MermaidFluent.Export(diagram, MermaidOutputFormat.Xps), "SER401");
    }

    [Test]
    public void CombinedPlugins_OutputCapabilities_ExposeAllAvailableFormatsExceptXps()
    {
        MermaidRenderSurfacesImageSharpExtensions.RegisterImageSharpSurface();
        MermaidRenderSurfacesSkiaExtensions.RegisterSkiaSurface();

        var capabilities = MermaidFluent.OutputCapabilitiesFor(DiagramType.Flowchart);
        Assert.That(capabilities.Supports(MermaidOutputFormat.Mermaid), Is.True);
        Assert.That(capabilities.Supports(MermaidOutputFormat.Svg), Is.True);
        Assert.That(capabilities.Supports(MermaidOutputFormat.Xaml), Is.True);
        Assert.That(capabilities.Supports(MermaidOutputFormat.ReactFlow), Is.True);
        Assert.That(capabilities.Supports(MermaidOutputFormat.Png), Is.True);
        Assert.That(capabilities.Supports(MermaidOutputFormat.Jpeg), Is.True);
        Assert.That(capabilities.Supports(MermaidOutputFormat.Webp), Is.True);
        Assert.That(capabilities.Supports(MermaidOutputFormat.Pdf), Is.True);
        Assert.That(capabilities.Supports(MermaidOutputFormat.Xps), Is.False);
    }

    static FlowchartDiagram CreateFlowchart() =>
        MermaidFluent.Flowchart(Direction.LeftToRight, f => f
                .Node("A", "Start")
                .Node("B", "Process")
                .Node("C", "End")
                .Edge("A", "B")
                .Edge("B", "C"))
            .Build();

    static void AssertBinarySuccess(ExportResult result, string mimeType)
    {
        Assert.That(result.Success, Is.True);
        Assert.That(result.Bytes, Is.Not.Null);
        Assert.That(result.Bytes!.Length, Is.GreaterThan(0));
        Assert.That(result.MimeType, Is.EqualTo(mimeType));
    }

    static void AssertUnsupported(ExportResult result, string code)
    {
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.HasErrors, Is.True);
        Assert.That(result.Diagnostics.Items.Any(x => x.Code == code), Is.True);
    }
}
