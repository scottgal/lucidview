public class FlowchartTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            flowchart LR
                A[Start] --> B[Process] --> C[End]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Complex()
    {
        const string input =
            """
            flowchart TD
                A[Christmas] -->|Get money| B(Go shopping)
                B --> C{Let me think}
                C -->|One| D[Laptop]
                C -->|Two| E[iPhone]
                C -->|Three| F[fa:fa-car Car]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Shapes()
    {
        const string input =
            """
            flowchart TD
                A[Rectangle]
                B(Rounded)
                C{Diamond}
                D((Circle))
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task EdgeLabels()
    {
        const string input =
            """
            flowchart LR
                A --> |Yes| B
                A --> |No| C
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task GraphKeyword()
    {
        const string input =
            """
            graph TD
                A --> B --> C
            """;

        return VerifySvg(input);
    }

    [Test]
    public void SemicolonSeparatedStatements()
    {
        const string input = "flowchart LR; A[Start] --> B[Process]; B --> C[End]";
        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("Start"));
        Assert.That(svg, Does.Contain("End"));
    }

    [Test]
    public void AmpersandChainDoesNotDuplicateEdges()
    {
        // "A & B --> C --> D" should create 3 edges: A→C, B→C, C→D
        // NOT 4 edges (duplicate C→D from each left-side node)
        const string input = "flowchart LR; A & B --> C --> D";
        var svg = Mermaid.Render(input);

        // Count edge paths — should be exactly 3
        var edgeCount = System.Text.RegularExpressions.Regex.Matches(
            svg, @"class=""flow-edge").Count;
        Assert.That(edgeCount, Is.EqualTo(3),
            $"Expected 3 edges (A→C, B→C, C→D) but found {edgeCount}");
    }

    [Test]
    public void AmpersandChainMultipleSegments()
    {
        // "A & B & C --> D --> E --> F" should create 5 edges:
        // A→D, B→D, C→D, D→E, E→F
        const string input = "flowchart LR; A & B & C --> D --> E --> F";
        var svg = Mermaid.Render(input);

        var edgeCount = System.Text.RegularExpressions.Regex.Matches(
            svg, @"class=""flow-edge").Count;
        Assert.That(edgeCount, Is.EqualTo(5),
            $"Expected 5 edges but found {edgeCount}");
    }
}
