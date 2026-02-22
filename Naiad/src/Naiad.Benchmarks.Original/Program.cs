using BenchmarkDotNet.Running;
using Dagre;
using Naiad.Benchmarks.Original;
using System.Diagnostics;

if (args.Length > 0 && args[0] == "--quick")
{
    RunQuickBenchmarks();
    return;
}

var switcher = BenchmarkSwitcher.FromAssembly(typeof(DagreOriginalBenchmarks).Assembly);
switcher.Run(args);

static DagreInputGraph BuildGraph(int nodeCount, int edgeCount, int seed)
{
    var graph = new DagreInputGraph();
    var nodes = new DagreInputNode[nodeCount];
    for (var i = 0; i < nodeCount; i++)
        nodes[i] = graph.AddNode(tag: i, width: 80, height: 40);

    var added = new HashSet<(int, int)>();
    for (var i = 0; i < nodeCount - 1 && added.Count < edgeCount; i++)
    {
        graph.AddEdge(nodes[i], nodes[i + 1]);
        added.Add((i, i + 1));
    }

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

static void RunQuickBenchmarks()
{
    var tests = new (string Name, int Nodes, int Edges, int Seed, int Warmup, int Iterations)[]
    {
        ("Small (5N/8E)", 5, 8, 42, 5, 20),
        ("Medium (20N/30E)", 20, 30, 42, 3, 10),
        ("Large (50N/80E)", 50, 80, 42, 3, 10),
        ("Stress (200N/350E)", 200, 350, 99, 2, 5),
    };

    Console.WriteLine();
    Console.WriteLine("=== Original Dagre.NET NuGet Package ===");
    Console.WriteLine();
    Console.WriteLine($"{"Test",-25} {"Mean ms",-12} {"Min ms",-12} {"Max ms",-12}");
    Console.WriteLine(new string('-', 61));

    foreach (var (name, nodeCount, edgeCount, seed, warmup, iters) in tests)
    {
        try
        {
            for (var w = 0; w < warmup; w++)
            {
                var g = BuildGraph(nodeCount, edgeCount, seed);
                g.Layout();
            }

            var times = new double[iters];
            var sw = new Stopwatch();
            for (var i = 0; i < iters; i++)
            {
                var g = BuildGraph(nodeCount, edgeCount, seed);
                sw.Restart();
                g.Layout();
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
            }

            Console.WriteLine($"{name,-25} {times.Average(),-12:F2} {times.Min(),-12:F2} {times.Max(),-12:F2}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{name,-25} ERROR: {ex.Message}");
        }
    }
}
