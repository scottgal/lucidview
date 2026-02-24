using MermaidSharp.Formats;

public class TlpFormatTests
{
    [Test]
    public void ParseSimpleGraph()
    {
        var input =
            """
            (tlp "2.3"
              (nodes 0..4)
              (edge 0 0 1)
              (edge 1 1 2)
            )
            """;

        var graph = TlpParser.Parse(input);

        Assert.That(graph, Is.Not.Null);
        Assert.That(graph.Nodes.Count, Is.EqualTo(5));
        Assert.That(graph.Edges.Count, Is.EqualTo(2));
    }

    [Test]
    public void ParseWithProperties()
    {
        var input =
            """
            (tlp "2.3"
              (nodes 0 1 2)
              (edge 0 0 1)
              (property 0 string "viewLabel"
                (default "" "")
                (node 0 "Alpha")
                (node 1 "Beta")
              )
            )
            """;

        var graph = TlpParser.Parse(input);

        Assert.That(graph.Properties, Contains.Key("viewLabel"));
        var labelProp = graph.Properties["viewLabel"];
        Assert.That(labelProp.NodeValues[0], Is.EqualTo("Alpha"));
        Assert.That(labelProp.NodeValues[1], Is.EqualTo("Beta"));
    }

    [Test]
    public void ParseWithClusters()
    {
        var input =
            """
            (tlp "2.3"
              (nodes 0..5)
              (edge 0 0 1)
              (edge 1 2 3)
              (cluster 1
                (nodes 0 1)
                (edges 0)
              )
            )
            """;

        var graph = TlpParser.Parse(input);

        Assert.That(graph.Clusters.Count, Is.EqualTo(1));
        Assert.That(graph.Clusters[0].NodeIds, Does.Contain(0));
        Assert.That(graph.Clusters[0].NodeIds, Does.Contain(1));
    }

    [Test]
    public void ParseWithColors()
    {
        var input =
            """
            (tlp "2.3"
              (nodes 0 1)
              (property 0 color "viewColor"
                (default "(0,0,0,255)" "(0,0,0,0)")
                (node 0 "(255,0,0,255)")
              )
            )
            """;

        var graph = TlpParser.Parse(input);

        Assert.That(graph.Properties, Contains.Key("viewColor"));
        var colorProp = graph.Properties["viewColor"];
        var color = colorProp.NodeValues[0];
        Assert.That(color, Is.InstanceOf<(double, double, double, double)>());
    }
}
