using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Mostlylucid.Dagre;

namespace Naiad.Benchmarks;

/// <summary>
/// Benchmarks using DagreInputGraph.Layout() - same API surface as the
/// original NuGet package, for direct comparison.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(Config))]
public class DagreInputGraphBenchmarks
{
    sealed class Config : ManualConfig
    {
        public Config()
        {
            AddColumn(StatisticColumn.P95);
            AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
        }
    }

    /// <summary>
    /// Builds a connected DAG where every node has at least one edge.
    /// First creates a chain (0->1->2->...->N-1) to ensure connectivity,
    /// then adds random forward edges to reach the target edge count.
    /// </summary>
    static DagreInputGraph BuildGraph(int nodeCount, int edgeCount, int seed)
    {
        var graph = new DagreInputGraph();
        var nodes = new DagreInputNode[nodeCount];
        for (var i = 0; i < nodeCount; i++)
            nodes[i] = graph.AddNode(tag: i, width: 80, height: 40);

        var added = new HashSet<(int, int)>();

        // Chain ensures every node has at least one edge
        for (var i = 0; i < nodeCount - 1 && added.Count < edgeCount; i++)
        {
            graph.AddEdge(nodes[i], nodes[i + 1]);
            added.Add((i, i + 1));
        }

        // Fill remaining edges randomly
        var rng = new Random(seed);
        while (added.Count < edgeCount)
        {
            var src = rng.Next(nodeCount - 1);
            var tgt = rng.Next(src + 1, nodeCount);
            if (added.Add((src, tgt)))
                graph.AddEdge(nodes[src], nodes[tgt]);
        }

        return graph;
    }

    [Benchmark(Description = "Optimized Small (5N/8E)")]
    public void LayoutSmall()
    {
        var g = BuildGraph(5, 8, seed: 42);
        g.Layout();
    }

    [Benchmark(Description = "Optimized Medium (20N/30E)")]
    public void LayoutMedium()
    {
        var g = BuildGraph(20, 30, seed: 42);
        g.Layout();
    }

    [Benchmark(Description = "Optimized Large (50N/80E)")]
    public void LayoutLarge()
    {
        var g = BuildGraph(50, 80, seed: 42);
        g.Layout();
    }

    [Benchmark(Description = "Optimized Stress (200N/350E)")]
    public void LayoutStress()
    {
        var g = BuildGraph(200, 350, seed: 99);
        g.Layout();
    }
}
