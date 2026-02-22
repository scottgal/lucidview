using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using MermaidSharp;

namespace Naiad.Benchmarks;

/// <summary>
/// Benchmarks that measure Dagre.NET layout performance via Mermaid.Render.
/// Uses end-to-end rendering to exercise the full layout pipeline: parsing,
/// network simplex ranking, barycenter ordering, Brandes-Kopf coordinate
/// assignment, and SVG output.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(Config))]
public class DagreLayoutBenchmarks
{
    sealed class Config : ManualConfig
    {
        public Config()
        {
            AddColumn(StatisticColumn.P95);
            AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
        }
    }

    static readonly RenderOptions Options = new();

    // Small: 5 nodes, 4 edges — direct definition
    const string SmallFlowchart = """
        flowchart TD
            A --> B
            B --> C
            C --> D
            D --> E
        """;

    // Medium: ~20 nodes, ~25 edges with subgraphs
    const string MediumFlowchart = FlowchartBenchmarks.MediumFlowchartInput;

    // Large: ~50 nodes, ~60 edges
    static readonly string LargeFlowchart = GenerateFlowchart(50, 60, seed: 42);

    // Stress: ~200 nodes, ~300 edges
    static readonly string StressFlowchart = GenerateFlowchart(200, 300, seed: 99);

    static string GenerateFlowchart(int nodeCount, int edgeCount, int seed)
    {
        var lines = new List<string> { "flowchart TD" };
        for (var i = 0; i < nodeCount; i++)
            lines.Add($"    N{i}[Node {i}]");

        var rng = new Random(seed);
        HashSet<(int, int)> added = [];
        var count = 0;
        while (count < edgeCount)
        {
            var src = rng.Next(nodeCount - 1);
            var tgt = rng.Next(src + 1, nodeCount);
            if (added.Add((src, tgt)))
            {
                lines.Add($"    N{src} --> N{tgt}");
                count++;
            }
        }

        return string.Join("\n", lines);
    }

    [Benchmark(Description = "Layout Small (5N/4E)")]
    public string LayoutSmall() => Mermaid.Render(SmallFlowchart, Options);

    [Benchmark(Description = "Layout Medium (20N/25E)")]
    public string LayoutMedium() => Mermaid.Render(MediumFlowchart, Options);

    [Benchmark(Description = "Layout Large (50N/60E)")]
    public string LayoutLarge() => Mermaid.Render(LargeFlowchart, Options);

    [Benchmark(Description = "Layout Stress (200N/300E)")]
    public string LayoutStress() => Mermaid.Render(StressFlowchart, Options);
}
