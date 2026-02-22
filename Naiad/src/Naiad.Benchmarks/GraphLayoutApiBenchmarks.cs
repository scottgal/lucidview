using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Dagre;

namespace Naiad.Benchmarks;

/// <summary>
/// Benchmarks the clean GraphLayout API, measuring pure layout time without
/// Mermaid parsing or SVG rendering overhead.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(Config))]
public class GraphLayoutApiBenchmarks
{
    sealed class Config : ManualConfig
    {
        public Config()
        {
            AddColumn(StatisticColumn.P95);
            AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
        }
    }

    // Pre-built edge lists (DAG-only: src < tgt)
    static readonly (int, int)[] SmallEdges = BuildEdges(5, 4, 1);
    static readonly (int, int)[] MediumEdges = BuildEdges(20, 25, 2);
    static readonly (int, int)[] LargeEdges = BuildEdges(50, 60, 3);
    static readonly (int, int)[] StressEdges = BuildEdges(200, 300, 4);

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

    [Benchmark(Description = "API Small (5N/4E)")]
    public LayoutResultData ApiSmall() => RunLayout(5, SmallEdges);

    [Benchmark(Description = "API Medium (20N/25E)")]
    public LayoutResultData ApiMedium() => RunLayout(20, MediumEdges);

    [Benchmark(Description = "API Large (50N/60E)")]
    public LayoutResultData ApiLarge() => RunLayout(50, LargeEdges);

    [Benchmark(Description = "API Stress (200N/300E)")]
    public LayoutResultData ApiStress() => RunLayout(200, StressEdges);
}
