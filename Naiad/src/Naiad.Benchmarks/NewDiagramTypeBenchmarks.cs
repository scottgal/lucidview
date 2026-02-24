using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using MermaidSharp;
using MermaidSharp.Formats;

namespace Naiad.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(Config))]
public class NewDiagramTypeBenchmarks
{
    sealed class Config : ManualConfig
    {
        public Config() => AddColumn(StatisticColumn.P95);
    }

    static readonly RenderOptions Options = new();

    const string ParallelCoordsSmall = """
parallelcoords
    axis Price, MPG, Horsepower
    dataset "Car1"{22000, 32, 180}
    dataset "Car2"{35000, 22, 260}
""";

    const string ParallelCoordsLarge = """
parallelcoords
    title "Large Dataset Comparison"
    axis A, B, C, D, E, F, G, H
    dataset "Series1"{10, 20, 30, 40, 50, 60, 70, 80}
    dataset "Series2"{80, 70, 60, 50, 40, 30, 20, 10}
    dataset "Series3"{50, 50, 50, 50, 50, 50, 50, 50}
    dataset "Series4"{10, 30, 50, 70, 90, 70, 50, 30}
    dataset "Series5"{90, 70, 50, 30, 10, 30, 50, 70}
    dataset "Series6"{25, 35, 45, 55, 65, 75, 85, 95}
    dataset "Series7"{95, 85, 75, 65, 55, 45, 35, 25}
    dataset "Series8"{40, 60, 20, 80, 30, 90, 10, 70}
    dataset "Series9"{15, 85, 25, 75, 35, 65, 45, 55}
    dataset "Series10"{55, 45, 65, 35, 75, 25, 85, 15}
""";

    const string DendrogramSmall = """
dendrogram
    leaf "A", "B", "C", "D"
    merge "A"-"B":0.3
    merge "C"-"D":0.5
    merge "AB"-"CD":0.8
""";

    const string DendrogramLarge = """
dendrogram
    title "Large Clustering"
    leaf "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P"
    merge "A"-"B":0.1
    merge "C"-"D":0.15
    merge "E"-"F":0.2
    merge "G"-"H":0.25
    merge "I"-"J":0.12
    merge "K"-"L":0.18
    merge "M"-"N":0.22
    merge "O"-"P":0.28
    merge "AB"-"CD":0.4
    merge "EF"-"GH":0.45
    merge "IJ"-"KL":0.35
    merge "MN"-"OP":0.5
    merge "ABCD"-"EFGH":0.7
    merge "IJKL"-"MNOP":0.75
    merge "ABCDEFGH"-"IJKLMNOP":1.0
""";

    const string BubblePackSmall = """
bubblepack
    "Root"
        "A": 100
        "B": 50
""";

    const string BubblePackLarge = """
bubblepack
    "Market"
        "Tech": 1000
            "Software": 600
                "SaaS": 400
                "Enterprise": 200
            "Hardware": 400
                "Consumer": 250
                "Enterprise": 150
        "Finance": 800
            "Banking": 500
                "Retail": 300
                "Commercial": 200
            "Insurance": 300
                "Life": 180
                "Property": 120
        "Healthcare": 600
            "Pharma": 350
            "Devices": 250
        "Energy": 400
            "Oil": 250
            "Renewable": 150
""";

    const string VoronoiSmall = """
voronoi
    site "A" at 100, 100
    site "B" at 200, 100
    site "C" at 150, 200
""";

    const string VoronoiLarge = """
voronoi
    title "Large Territory Map"
    site "N1" at 100, 100
    site "N2" at 200, 150
    site "N3" at 150, 250
    site "N4" at 300, 100
    site "N5" at 350, 200
    site "N6" at 250, 300
    site "N7" at 400, 150
    site "N8" at 450, 250
    site "N9" at 100, 350
    site "N10" at 200, 400
    site "N11" at 300, 350
    site "N12" at 400, 400
""";

    const string TlpSmall = """
(tlp "2.3"
  (nodes 0..4)
  (edge 0 0 1)
  (edge 1 1 2)
  (property 0 string "viewLabel"
    (node 0 "A")
    (node 1 "B")
  )
)
""";

    const string TlpLarge = """
(tlp "2.3"
  (author "Benchmark")
  (nodes 0..49)
  (edge 0 0 1)
  (edge 1 1 2)
  (edge 2 2 3)
  (edge 3 3 4)
  (edge 4 4 5)
  (edge 5 5 6)
  (edge 6 6 7)
  (edge 7 7 8)
  (edge 8 8 9)
  (edge 9 0 10)
  (edge 10 10 11)
  (edge 11 11 12)
  (edge 12 12 13)
  (edge 13 13 14)
  (edge 14 1 15)
  (edge 15 15 16)
  (edge 16 16 17)
  (edge 17 17 18)
  (edge 18 18 19)
  (edge 19 2 20)
  (property 0 string "viewLabel"
    (default "" "")
    (node 0 "Node0")
    (node 1 "Node1")
    (node 2 "Node2")
    (node 3 "Node3")
    (node 4 "Node4")
    (node 5 "Node5")
    (node 6 "Node6")
    (node 7 "Node7")
    (node 8 "Node8")
    (node 9 "Node9")
  )
  (property 1 color "viewColor"
    (default "(128,128,128,255)" "(0,0,0,0)")
    (node 0 "(255,0,0,255)")
    (node 1 "(0,255,0,255)")
    (node 2 "(0,0,255,255)")
  )
  (cluster 1
    (nodes 0 1 2 3 4)
  )
  (cluster 2
    (nodes 5 6 7 8 9)
  )
)
""";

    [Benchmark(Description = "ParallelCoords Small")]
    public string ParallelCoords_Small() => Mermaid.Render(ParallelCoordsSmall, Options);

    [Benchmark(Description = "ParallelCoords Large")]
    public string ParallelCoords_Large() => Mermaid.Render(ParallelCoordsLarge, Options);

    [Benchmark(Description = "Dendrogram Small")]
    public string Dendrogram_Small() => Mermaid.Render(DendrogramSmall, Options);

    [Benchmark(Description = "Dendrogram Large")]
    public string Dendrogram_Large() => Mermaid.Render(DendrogramLarge, Options);

    [Benchmark(Description = "BubblePack Small")]
    public string BubblePack_Small() => Mermaid.Render(BubblePackSmall, Options);

    [Benchmark(Description = "BubblePack Large")]
    public string BubblePack_Large() => Mermaid.Render(BubblePackLarge, Options);

    [Benchmark(Description = "Voronoi Small")]
    public string Voronoi_Small() => Mermaid.Render(VoronoiSmall, Options);

    [Benchmark(Description = "Voronoi Large")]
    public string Voronoi_Large() => Mermaid.Render(VoronoiLarge, Options);

    [Benchmark(Description = "TLP Parse Small")]
    public TlpGraph Tlp_Parse_Small() => TlpParser.Parse(TlpSmall);

    [Benchmark(Description = "TLP Parse Large")]
    public TlpGraph Tlp_Parse_Large() => TlpParser.Parse(TlpLarge);
}
