public class VoronoiTests
{
    [Test]
    public void Simple()
    {
        var input =
            """
            voronoi
                site "A" at 100, 100
                site "B" at 200, 100
                site "C" at 150, 200
            """;

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("<svg"));
        Assert.That(svg, Does.Contain("A"));
        Assert.That(svg, Does.Contain("B"));
        Assert.That(svg, Does.Contain("C"));
    }

    [Test]
    public void WithTitle()
    {
        var input =
            """
            voronoi
                title "Territory Map"
                site "North" at 150, 100
                site "South" at 150, 300
            """;

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("Territory Map"));
    }

    [Test]
    public void ParsesWithoutError()
    {
        var input =
            """
            voronoi
                site "X" at 50, 50
                site "Y" at 100, 100
            """;

        Assert.DoesNotThrow(() => Mermaid.Render(input));
    }
}
