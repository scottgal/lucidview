using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using MermaidSharp;
using MermaidSharp.Formats;
using System.Text;

namespace Naiad.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(Config))]
public class LargeDiagramBenchmarks
{
    sealed class Config : ManualConfig
    {
        public Config() => AddColumn(StatisticColumn.P95);
    }

    static readonly RenderOptions Options = new();

    static readonly string LargeTlpGraph = GenerateLargeTlp(1000, 5000);
    static readonly string TulipScaleTlp10K = GenerateLargeTlp(10000, 50000);
    static readonly string TulipScaleTlp50K = GenerateLargeTlp(50000, 250000);
    static readonly string TulipScaleTlp100K = GenerateLargeTlp(100000, 500000);

    static string GenerateLargeTlp(int nodeCount, int edgeCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"(tlp \"2.3\"");
        sb.AppendLine($"  (author \"Benchmark\")");
        sb.AppendLine($"  (nodes 0..{nodeCount - 1})");

        var rand = new Random(42);
        for (var i = 0; i < edgeCount; i++)
        {
            var src = rand.Next(nodeCount);
            var tgt = rand.Next(nodeCount);
            if (src != tgt)
                sb.AppendLine($"  (edge {i} {src} {tgt})");
        }

        sb.AppendLine(")");
        return sb.ToString();
    }

    static readonly string LargeVoronoi = GenerateLargeVoronoi(100);

    static string GenerateLargeVoronoi(int siteCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("voronoi");
        var rand = new Random(42);
        for (var i = 0; i < siteCount; i++)
        {
            var x = rand.Next(50, 550);
            var y = rand.Next(50, 350);
            sb.AppendLine($"    site \"S{i}\" at {x}, {y}");
        }
        return sb.ToString();
    }

    static readonly string LargeBubblePack = GenerateLargeBubblePack(50);

    static string GenerateLargeBubblePack(int nodeCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("bubblepack");
        var rand = new Random(42);
        var depth = 0;
        var indent = "";
        for (var i = 0; i < nodeCount; i++)
        {
            var value = rand.Next(100, 1000);
            sb.AppendLine($"{indent}\"Node{i}\": {value}");
            if (rand.NextDouble() > 0.7 && depth < 4)
            {
                depth++;
                indent = new string(' ', depth * 4);
            }
            else if (depth > 0 && rand.NextDouble() > 0.6)
            {
                depth--;
                indent = new string(' ', depth * 4);
            }
        }
        return sb.ToString();
    }

    static readonly string LargeParallelCoords = GenerateLargeParallelCoords(50, 20);

    static string GenerateLargeParallelCoords(int datasets, int axes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("parallelcoords");
        sb.Append("    axis ");
        for (var i = 0; i < axes; i++)
            sb.Append(i == 0 ? $"A{i}" : $", A{i}");
        sb.AppendLine();

        var rand = new Random(42);
        for (var d = 0; d < datasets; d++)
        {
            sb.Append($"    dataset \"Dataset{d}\"{{");
            for (var a = 0; a < axes; a++)
                sb.Append(a == 0 ? $"{rand.Next(100, 1000)}" : $", {rand.Next(100, 1000)}");
            sb.AppendLine("}");
        }
        return sb.ToString();
    }

    static readonly string LargeDendrogram = GenerateLargeDendrogram(64);

    static string GenerateLargeDendrogram(int leafCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("dendrogram");
        sb.Append("    leaf ");
        var leaves = new List<string>();
        for (var i = 0; i < leafCount; i++)
            leaves.Add($"\"L{i}\"");
        sb.AppendLine(string.Join(", ", leaves));

        var rand = new Random(42);
        var currentLevel = leaves.Select(l => l.Trim('"')).ToList();
        var height = 0.1;

        while (currentLevel.Count > 1)
        {
            var nextLevel = new List<string>();
            for (var i = 0; i < currentLevel.Count - 1; i += 2)
            {
                var merged = $"M{nextLevel.Count}";
                sb.AppendLine($"    merge \"{currentLevel[i]}\"-\"{currentLevel[i + 1]}\":{height:F2}");
                nextLevel.Add(merged);
            }
            if (currentLevel.Count % 2 == 1)
                nextLevel.Add(currentLevel[^1]);
            currentLevel = nextLevel;
            height += 0.1;
        }
        return sb.ToString();
    }

    // ── Large core diagram types ─────────────────────────────────
    static readonly string LargeFlowchart50 = GenerateLargeFlowchart(50);
    static readonly string LargeFlowchart100 = GenerateLargeFlowchart(100);
    static readonly string LargeFlowchart200 = GenerateLargeFlowchart(200);
    static readonly string LargeSequence50 = GenerateLargeSequence(50);
    static readonly string LargeClass30 = GenerateLargeClass(30);
    static readonly string LargeER20 = GenerateLargeER(20);
    static readonly string LargeMindmap = GenerateLargeMindmap(5, 4);
    static readonly string LargeGantt30 = GenerateLargeGantt(30);
    static readonly string LargeState20 = GenerateLargeState(20);

    static string GenerateLargeFlowchart(int nodes)
    {
        var sb = new StringBuilder("flowchart TD\n");
        for (var i = 0; i < nodes; i++)
        {
            sb.AppendLine($"    N{i}[Node {i}] --> N{(i + 1) % nodes}[Node {(i + 1) % nodes}]");
            if (i % 5 == 0 && i + 3 < nodes) sb.AppendLine($"    N{i} --> N{i + 3}");
        }
        return sb.ToString();
    }

    static string GenerateLargeSequence(int messages)
    {
        var sb = new StringBuilder("sequenceDiagram\n    participant A\n    participant B\n    participant C\n    participant D\n");
        var p = new[] { "A", "B", "C", "D" };
        for (var i = 0; i < messages; i++)
            sb.AppendLine($"    {p[i % 4]}->>{p[(i + 1) % 4]}: Message {i}");
        return sb.ToString();
    }

    static string GenerateLargeClass(int classes)
    {
        var sb = new StringBuilder("classDiagram\n");
        for (var i = 0; i < classes; i++)
        {
            sb.AppendLine($"    class C{i} {{\n        +String field{i}\n        +method{i}()\n        -int priv{i}\n    }}");
            if (i > 0) sb.AppendLine($"    C{i - 1} <|-- C{i}");
            if (i > 1 && i % 3 == 0) sb.AppendLine($"    C{i} --> C{i - 2} : uses");
        }
        return sb.ToString();
    }

    static string GenerateLargeER(int entities)
    {
        var sb = new StringBuilder("erDiagram\n");
        for (var i = 0; i < entities; i++)
        {
            sb.AppendLine($"    E{i} {{\n        string id PK\n        string name\n        int count\n    }}");
            if (i > 0) sb.AppendLine($"    E{i - 1} ||--o{{ E{i} : has");
        }
        return sb.ToString();
    }

    static string GenerateLargeMindmap(int breadth, int depth)
    {
        var sb = new StringBuilder("mindmap\n    root((Central))\n");
        void Add(int d, string indent)
        {
            if (d >= depth) return;
            for (var i = 0; i < breadth; i++)
            {
                sb.AppendLine($"{indent}Level{d}_Item{i}");
                Add(d + 1, indent + "    ");
            }
        }
        Add(0, "        ");
        return sb.ToString();
    }

    static string GenerateLargeGantt(int tasks)
    {
        var sb = new StringBuilder("gantt\n    title Large Project\n    dateFormat YYYY-MM-DD\n");
        for (var s = 0; s < 3; s++)
        {
            sb.AppendLine($"    section Phase {s + 1}");
            for (var i = 0; i < tasks / 3; i++)
            {
                var idx = s * (tasks / 3) + i;
                sb.AppendLine($"    Task{idx} :t{idx}, 2024-{(s + 1):D2}-{(i + 1):D2}, {5 + i}d");
            }
        }
        return sb.ToString();
    }

    static string GenerateLargeState(int states)
    {
        var sb = new StringBuilder("stateDiagram-v2\n    [*] --> S0\n");
        for (var i = 0; i < states - 1; i++)
        {
            sb.AppendLine($"    S{i} --> S{i + 1} : step{i}");
            if (i % 4 == 0 && i + 2 < states) sb.AppendLine($"    S{i} --> S{i + 2} : skip");
        }
        sb.AppendLine($"    S{states - 1} --> [*]");
        return sb.ToString();
    }

    [Benchmark(Description = "Flowchart 50 nodes")]
    public string Flowchart_50() => Mermaid.Render(LargeFlowchart50, Options);

    [Benchmark(Description = "Flowchart 100 nodes")]
    public string Flowchart_100() => Mermaid.Render(LargeFlowchart100, Options);

    [Benchmark(Description = "Flowchart 200 nodes")]
    public string Flowchart_200() => Mermaid.Render(LargeFlowchart200, Options);

    [Benchmark(Description = "Sequence 50 msgs")]
    public string Sequence_50() => Mermaid.Render(LargeSequence50, Options);

    [Benchmark(Description = "Class 30")]
    public string Class_30() => Mermaid.Render(LargeClass30, Options);

    [Benchmark(Description = "ER 20 entities")]
    public string ER_20() => Mermaid.Render(LargeER20, Options);

    [Benchmark(Description = "Mindmap 5x4 (780 nodes)")]
    public string Mindmap_5x4() => Mermaid.Render(LargeMindmap, Options);

    [Benchmark(Description = "Gantt 30 tasks")]
    public string Gantt_30() => Mermaid.Render(LargeGantt30, Options);

    [Benchmark(Description = "State 20")]
    public string State_20() => Mermaid.Render(LargeState20, Options);

    // ── TLP / exotic ─────────────────────────────────────────────
    [Benchmark(Description = "TLP Parse 1000 nodes")]
    public TlpGraph Tlp_Parse_1000Nodes() => TlpParser.Parse(LargeTlpGraph);

    [Benchmark(Description = "TLP Parse 10K nodes")]
    public TlpGraph Tlp_Parse_10KNodes() => TlpParser.Parse(TulipScaleTlp10K);

    [Benchmark(Description = "TLP Parse 50K nodes")]
    public TlpGraph Tlp_Parse_50KNodes() => TlpParser.Parse(TulipScaleTlp50K);

    [Benchmark(Description = "TLP Parse 100K nodes")]
    public TlpGraph Tlp_Parse_100KNodes() => TlpParser.Parse(TulipScaleTlp100K);

    [Benchmark(Description = "Voronoi 100 sites")]
    public string Voronoi_100Sites() => Mermaid.Render(LargeVoronoi, Options);

    [Benchmark(Description = "BubblePack 50 nodes")]
    public string BubblePack_50Nodes() => Mermaid.Render(LargeBubblePack, Options);

    [Benchmark(Description = "ParallelCoords 50x20")]
    public string ParallelCoords_50x20() => Mermaid.Render(LargeParallelCoords, Options);

    [Benchmark(Description = "Dendrogram 64 leaves")]
    public string Dendrogram_64Leaves() => Mermaid.Render(LargeDendrogram, Options);
}
