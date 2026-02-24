public class BubblePackTests
{
    [Test]
    public void Simple()
    {
        var input =
            """
            bubblepack
                "Root"
                    "A": 100
                    "B": 50
            """;

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("<svg"));
        Assert.That(svg, Does.Contain("Root"));
        Assert.That(svg, Does.Contain("A"));
        Assert.That(svg, Does.Contain("B"));
    }

    [Test]
    public void NestedHierarchy()
    {
        var input =
            """
            bubblepack
                "Market"
                    "Tech": 1000
                        "Software": 600
                        "Hardware": 400
                    "Finance": 800
            """;

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("Market"));
        Assert.That(svg, Does.Contain("Tech"));
        Assert.That(svg, Does.Contain("Software"));
        Assert.That(svg, Does.Contain("Finance"));
    }

    [Test]
    public void ParsesWithoutError()
    {
        var input =
            """
            bubble
                "X": 10
            """;

        Assert.DoesNotThrow(() => Mermaid.Render(input));
    }
}
