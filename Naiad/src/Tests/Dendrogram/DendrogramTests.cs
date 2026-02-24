public class DendrogramTests
{
    [Test]
    public void Simple()
    {
        var input =
            """
            dendrogram
                leaf "A", "B", "C", "D"
                merge "A"-"B":0.3
                merge "C"-"D":0.5
            """;

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("<svg"));
        Assert.That(svg, Does.Contain("A"));
        Assert.That(svg, Does.Contain("B"));
        Assert.That(svg, Does.Contain("C"));
        Assert.That(svg, Does.Contain("D"));
    }

    [Test]
    public void WithTitle()
    {
        var input =
            """
            dendrogram
                title "Species Clustering"
                leaf "Dog", "Cat"
                merge "Dog"-"Cat":0.2
            """;

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("Species Clustering"));
    }

    [Test]
    public void HierarchicalMerge()
    {
        var input =
            """
            dendrogram
                leaf "W", "X", "Y", "Z"
                merge "W"-"X":0.1
                merge "Y"-"Z":0.2
                merge "WX"-"YZ":0.5
            """;

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("W"));
        Assert.That(svg, Does.Contain("X"));
        Assert.That(svg, Does.Contain("Y"));
        Assert.That(svg, Does.Contain("Z"));
    }

    [Test]
    public void ParsesWithoutError()
    {
        var input =
            """
            dendrogram
                leaf "A", "B"
                merge "A"-"B":1.0
            """;

        Assert.DoesNotThrow(() => Mermaid.Render(input));
    }
}
