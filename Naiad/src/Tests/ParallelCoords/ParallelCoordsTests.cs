public class ParallelCoordsTests
{
    [Test]
    public void Simple()
    {
        var input =
            """
            parallelcoords
                axis Price, MPG, Horsepower
                dataset "Car1"{22000, 32, 180}
                dataset "Car2"{35000, 22, 260}
            """;

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("<svg"));
        Assert.That(svg, Does.Contain("Price"));
        Assert.That(svg, Does.Contain("MPG"));
        Assert.That(svg, Does.Contain("Horsepower"));
        Assert.That(svg, Does.Contain("Car1"));
        Assert.That(svg, Does.Contain("Car2"));
    }

    [Test]
    public void WithTitle()
    {
        var input =
            """
            parallelcoords
                title "Car Comparison"
                axis A, B, C
                dataset "X"{1, 2, 3}
            """;

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("Car Comparison"));
    }

    [Test]
    public void MultipleDatasets()
    {
        var input =
            """
            parallel-coords
                axis Alpha, Beta, Gamma
                dataset "First"{10, 20, 30}
                dataset "Second"{40, 50, 60}
                dataset "Third"{70, 80, 90}
            """;

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("First"));
        Assert.That(svg, Does.Contain("Second"));
        Assert.That(svg, Does.Contain("Third"));
    }

    [Test]
    public void ParsesWithoutError()
    {
        var input =
            """
            parallelcoords
                axis X, Y
                dataset "Test"{1, 2}
            """;

        Assert.DoesNotThrow(() => Mermaid.Render(input));
    }
}
