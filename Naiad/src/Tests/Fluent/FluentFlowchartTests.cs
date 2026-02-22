using MermaidSharp.Fluent;
using MermaidSharp.Models;

namespace MermaidSharp.Tests.Fluent;

public class FluentFlowchartTests
{
    [Test]
    public void FlowchartBuilder_BuildsDeterministicMermaid()
    {
        var diagram = MermaidFluent.Flowchart(Direction.LeftToRight, flow => flow
            .Node("A", "Start")
            .Node("B", "Validate")
            .Subgraph("sg_workers", "Workers", sg => sg
                .Direction(Direction.TopToBottom)
                .Node("W1", "Worker 1")
                .Node("W2", "Worker 2")
                .Edge("W1", "W2"))
            .Edge("A", "B")
            .Edge("B", "sg_workers")
            .ClassDef("critical", c => c.Fill("#ffe6e6").Stroke("#cc0000"))
            .Class("B", "critical"))
            .Build();

        var mermaid = diagram.ToMermaid();

        Assert.That(mermaid, Does.Contain("flowchart LR"));
        Assert.That(mermaid, Does.Contain("subgraph sg_workers"));
        Assert.That(mermaid, Does.Contain("classDef critical fill:#ffe6e6,stroke:#cc0000"));
        Assert.That(mermaid, Does.Contain("class B critical"));
    }

    [Test]
    public void TryParseAny_Flowchart_ReturnsStructuredSuccess()
    {
        const string source =
            """
            flowchart LR
                A[Start] --> B[End]
            """;

        var result = MermaidFluent.TryParseAny(source);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Value, Is.InstanceOf<FlowchartDiagram>());
        Assert.That(result.Diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Export_MermaidAndSvg_Succeeds()
    {
        var diagram = MermaidFluent.Flowchart(Direction.LeftToRight, f => f
                .Node("A", "Start")
                .Node("B", "End")
                .Edge("A", "B"))
            .Build();

        var mermaidExport = MermaidFluent.Export(diagram, MermaidOutputFormat.Mermaid);
        var svgExport = MermaidFluent.Export(diagram, MermaidOutputFormat.Svg);

        Assert.That(mermaidExport.Success, Is.True);
        Assert.That(mermaidExport.Text, Does.Contain("flowchart LR"));

        Assert.That(svgExport.Success, Is.True);
        Assert.That(svgExport.Text, Does.Contain("<svg"));
    }

    [Test]
    public void Export_Png_WithoutPlugin_ReturnsStructuredError()
    {
        var diagram = MermaidFluent.Flowchart(Direction.LeftToRight, f => f
                .Node("A", "Start")
                .Node("B", "End")
                .Edge("A", "B"))
            .Build();

        var result = MermaidFluent.Export(diagram, MermaidOutputFormat.Png);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.HasErrors, Is.True);
        Assert.That(result.Diagnostics.Items[0].Code, Is.EqualTo("SER401"));
    }

    [Test]
    public void Export_Xaml_Succeeds()
    {
        var diagram = MermaidFluent.Flowchart(Direction.LeftToRight, f => f
                .Node("A", "Start")
                .Node("B", "End")
                .Edge("A", "B"))
            .Build();

        var result = MermaidFluent.Export(diagram, MermaidOutputFormat.Xaml);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Text, Does.Contain("<Canvas"));
        Assert.That(result.Text, Does.Contain("TextBlock"));
    }

    [Test]
    public void Export_ReactFlow_Succeeds()
    {
        var diagram = MermaidFluent.Flowchart(Direction.LeftToRight, f => f
                .Node("A", "Start")
                .Node("B", "End")
                .Edge("A", "B"))
            .Build();

        var result = MermaidFluent.Export(diagram, MermaidOutputFormat.ReactFlow);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Text, Does.Contain("\"nodes\""));
        Assert.That(result.Text, Does.Contain("\"edges\""));
        Assert.That(result.Text, Does.Contain("\"source\": \"A\""));
        Assert.That(result.Text, Does.Contain("\"target\": \"B\""));
    }

    [Test]
    public void OutputCapabilities_ExposeSurfaceTargets()
    {
        Assert.That(MermaidFluent.OutputCapabilities.Supports(MermaidOutputFormat.Svg), Is.True);
        Assert.That(MermaidFluent.OutputCapabilities.Supports(MermaidOutputFormat.Xaml), Is.True);
        Assert.That(MermaidFluent.OutputCapabilities.Supports(MermaidOutputFormat.ReactFlow), Is.True);
        Assert.That(MermaidFluent.OutputCapabilities.Supports(MermaidOutputFormat.Png), Is.False);
    }

    [Test]
    public void OutputCapabilitiesFor_RespectsDiagramTypeSpecificPlugins()
    {
        var flowchartCapabilities = MermaidFluent.OutputCapabilitiesFor(DiagramType.Flowchart);
        var sequenceCapabilities = MermaidFluent.OutputCapabilitiesFor(DiagramType.Sequence);

        Assert.That(flowchartCapabilities.Supports(MermaidOutputFormat.ReactFlow), Is.True);
        Assert.That(sequenceCapabilities.Supports(MermaidOutputFormat.ReactFlow), Is.False);
        Assert.That(sequenceCapabilities.Supports(MermaidOutputFormat.Svg), Is.True);
    }
}
