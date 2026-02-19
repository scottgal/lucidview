namespace MermaidSharp.Tests.Flowchart;

public class FlowchartStylingTests
{
    [Test]
    public void ThemeDark_EmitsStructuredSkinVariables()
    {
        const string input =
            """
            flowchart LR
                A --> B
            """;

        var svg = Mermaid.Render(input, new RenderOptions { Theme = "dark" });

        Assert.That(svg, Does.Contain("Mermaid skin: dark"));
        Assert.That(svg, Does.Contain("--flow-node-fill"));
        Assert.That(svg, Does.Contain(".flow-edge-dotted"));
        Assert.That(svg, Does.Contain(".flow-node-shape"));
    }

    [Test]
    public void Subgraph_RendersClusterContainerClasses()
    {
        const string input =
            """
            flowchart TD
                subgraph core[Core]
                    A --> B
                end
                B --> C
            """;

        var svg = Mermaid.Render(input);

        Assert.That(svg, Does.Contain("flow-subgraph-box"));
        Assert.That(svg, Does.Contain("flow-subgraph-title"));
        Assert.That(svg, Does.Contain(">Core<"));
    }
}
