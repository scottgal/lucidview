using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using MermaidSharp;

namespace Naiad.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(Config))]
public class FlowchartBenchmarks
{
    sealed class Config : ManualConfig
    {
        public Config()
        {
            AddColumn(StatisticColumn.P95);
        }
    }

    static readonly RenderOptions DefaultOptions = new();
    static readonly RenderOptions CurvedOptions = new() { CurvedEdges = true };
    static readonly RenderOptions StraightOptions = new() { CurvedEdges = false };
    static readonly RenderOptions ThemedOptions = new()
    {
        Theme = "dark",
        ThemeColors = new ThemeColorOverrides
        {
            TextColor = "#e0e0e0",
            BackgroundColor = "#1e1e1e",
            NodeFill = "#2d2d2d",
            NodeStroke = "#4a9eff",
            EdgeStroke = "#888",
            SubgraphFill = "#252525",
            SubgraphStroke = "#555"
        }
    };

    // Small: 5 nodes, 4 edges
    const string SmallFlowchart = """
        flowchart TD
            A[Start] --> B{Decision}
            B -->|Yes| C[Process]
            B -->|No| D[Skip]
            C --> E[End]
        """;

    // Medium: ~20 nodes, ~25 edges with subgraphs and styles (internal for cross-benchmark use)
    internal const string MediumFlowchartInput = """
        flowchart TD
            A[Input] --> B{Validate}
            B -->|Valid| C[Parse]
            B -->|Invalid| D[Error]
            C --> E[Transform]
            E --> F{Check Type}
            F -->|Type A| G[Process A]
            F -->|Type B| H[Process B]
            F -->|Type C| I[Process C]
            G --> J[Merge]
            H --> J
            I --> J
            J --> K[Format]
            K --> L{Output Type}
            L -->|JSON| M[JSON Out]
            L -->|XML| N[XML Out]
            L -->|CSV| O[CSV Out]
            M --> P[Write]
            N --> P
            O --> P
            P --> Q[Done]
            D --> R[Log Error]
            R --> Q
            subgraph Processing
                E
                F
                G
                H
                I
                J
            end
            subgraph Output
                K
                L
                M
                N
                O
                P
            end
            style A fill:#4CAF50,stroke:#333
            style D fill:#f44336,stroke:#333
            style Q fill:#2196F3,stroke:#333
        """;

    // Large: ~50 nodes, ~60 edges with nested subgraphs
    static readonly string LargeFlowchart = GenerateLargeFlowchart(50, 60);

    // Stress: ~200 nodes, ~300 edges
    static readonly string StressFlowchart = GenerateLargeFlowchart(200, 300);

    static string GenerateLargeFlowchart(int nodeCount, int edgeCount)
    {
        var lines = new List<string> { "flowchart TD" };

        // Generate nodes with various shapes
        string[] shapes = ["[{0}]", "({0})", "{{{0}}}", "[/{0}/]", "[[{0}]]"];
        for (var i = 0; i < nodeCount; i++)
        {
            var shape = shapes[i % shapes.Length];
            lines.Add($"    N{i}{string.Format(shape, $"Node {i}")}");
        }

        // Generate edges
        var rng = new Random(42); // deterministic
        for (var i = 0; i < edgeCount; i++)
        {
            var src = rng.Next(nodeCount);
            var tgt = rng.Next(nodeCount);
            if (src == tgt) tgt = (tgt + 1) % nodeCount;
            var label = i % 3 == 0 ? $"|label {i}|" : "";
            lines.Add($"    N{src} -->{label} N{tgt}");
        }

        // Add some subgraphs
        var sgSize = Math.Max(5, nodeCount / 5);
        for (var sg = 0; sg < 4 && sg * sgSize < nodeCount; sg++)
        {
            lines.Add($"    subgraph Group{sg}[Group {sg}]");
            for (var j = sg * sgSize; j < Math.Min((sg + 1) * sgSize, nodeCount); j++)
            {
                lines.Add($"        N{j}");
            }
            lines.Add("    end");
        }

        // Add style directives
        for (var i = 0; i < Math.Min(10, nodeCount); i++)
        {
            lines.Add($"    style N{i} fill:#4CAF50,stroke:#333,stroke-width:2px");
        }

        return string.Join("\n", lines);
    }

    // --- End-to-end rendering ---

    [Benchmark(Description = "Small (5N/4E)")]
    public string RenderSmall() => Mermaid.Render(SmallFlowchart, DefaultOptions);

    [Benchmark(Description = "Medium (20N/25E)")]
    public string RenderMedium() => Mermaid.Render(MediumFlowchartInput, DefaultOptions);

    [Benchmark(Description = "Large (50N/60E)")]
    public string RenderLarge() => Mermaid.Render(LargeFlowchart, DefaultOptions);

    [Benchmark(Description = "Stress (200N/300E)")]
    public string RenderStress() => Mermaid.Render(StressFlowchart, DefaultOptions);

    // --- Curved vs straight edges ---

    [Benchmark(Description = "Medium Curved")]
    public string RenderMediumCurved() => Mermaid.Render(MediumFlowchartInput, CurvedOptions);

    [Benchmark(Description = "Medium Straight")]
    public string RenderMediumStraight() => Mermaid.Render(MediumFlowchartInput, StraightOptions);

    // --- Theme color overrides ---

    [Benchmark(Description = "Medium Themed")]
    public string RenderMediumThemed() => Mermaid.Render(MediumFlowchartInput, ThemedOptions);

    // --- Large with theme ---

    [Benchmark(Description = "Large Themed")]
    public string RenderLargeThemed() => Mermaid.Render(LargeFlowchart, ThemedOptions);
}
