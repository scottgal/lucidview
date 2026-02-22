using Dagre;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class GraphLayoutApiTests
{
    static (int, int)[] BuildEdges(int nodeCount, int edgeCount, int seed)
    {
        var rng = new Random(seed);
        HashSet<(int, int)> added = [];
        List<(int, int)> edges = [];
        while (edges.Count < edgeCount)
        {
            var src = rng.Next(nodeCount - 1);
            var tgt = rng.Next(src + 1, nodeCount);
            if (added.Add((src, tgt)))
                edges.Add((src, tgt));
        }
        return edges.ToArray();
    }

    static LayoutResultData RunLayout(int nodeCount, (int, int)[] edges)
    {
        var layout = new GraphLayout(new GraphLayoutOptions
        {
            RankSeparation = 50,
            EdgeSeparation = 20,
            NodeSeparation = 50,
            Direction = LayoutDirection.TopToBottom
        });

        for (var i = 0; i < nodeCount; i++)
            layout.AddNode(i.ToString(), 80, 40);

        foreach (var (src, tgt) in edges)
            layout.AddEdge(src.ToString(), tgt.ToString());

        return layout.Run();
    }

    [Test]
    public void Small() => RunLayout(5, BuildEdges(5, 4, 1));

    [Test]
    public void Medium() => RunLayout(20, BuildEdges(20, 25, 2));

    [Test]
    public void Large() => RunLayout(50, BuildEdges(50, 60, 3));

    [Test]
    public void Stress() => RunLayout(200, BuildEdges(200, 300, 4));
}
