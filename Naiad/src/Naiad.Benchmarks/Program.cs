using BenchmarkDotNet.Running;
using MermaidSharp;
using Mostlylucid.Dagre;
using Naiad.Benchmarks;
using System.Diagnostics;
using System.Text;

if (args.Length > 0 && args[0] == "--quick")
{
    RunQuickBenchmarks();
    return;
}

if (args.Length > 0 && args[0] == "--compare")
{
    RunDagreInputGraphBenchmark();
    return;
}

if (args.Length > 0 && args[0] == "--visual")
{
    RunVisualComparison();
    return;
}

if (args.Length > 0 && args[0] == "--profile")
{
    Mostlylucid.Dagre.Indexed.IndexedDagreLayout.TraceTiming = true;
    Console.WriteLine("=== Profile: Indexed Layout Phase Timing (200N/350E) ===");
    Console.WriteLine();
    // Warmup
    var gw = BuildDagreInputGraph(200, 350, 99);
    gw.Layout();
    // Profiled run
    Console.Error.WriteLine("--- Run 1 ---");
    var g1 = BuildDagreInputGraph(200, 350, 99);
    var sw = new Stopwatch();
    sw.Start();
    g1.Layout();
    sw.Stop();
    Console.WriteLine($"Total: {sw.Elapsed.TotalMilliseconds:F2}ms");
    Console.Error.WriteLine("--- Run 2 ---");
    var g2 = BuildDagreInputGraph(200, 350, 99);
    sw.Restart();
    g2.Layout();
    sw.Stop();
    Console.WriteLine($"Total: {sw.Elapsed.TotalMilliseconds:F2}ms");
    return;
}

var switcher = BenchmarkSwitcher.FromAssembly(typeof(FlowchartBenchmarks).Assembly);
switcher.Run(args);

static void RunQuickBenchmarks()
{
    var options = new RenderOptions();
    var tests = new (string Name, string Source, int Warmup, int Iterations)[]
    {
        ("Flowchart 50", GenFlowchart(50), 3, 10),
        ("Flowchart 100", GenFlowchart(100), 2, 5),
        ("Flowchart 200", GenFlowchart(200), 1, 3),
        ("Flowchart 500", GenFlowchart(500), 1, 2),
        ("Sequence 50", GenSequence(50), 3, 10),
        ("Class 30", GenClass(30), 3, 10),
        ("ER 20", GenER(20), 3, 10),
        ("Mindmap 5x4", GenMindmap(5, 4), 2, 5),
        ("Gantt 30", GenGantt(30), 3, 10),
        ("State 20", GenState(20), 3, 10),
        ("Pie 5", "pie\n    \"A\" : 30\n    \"B\" : 25\n    \"C\" : 20\n    \"D\" : 15\n    \"E\" : 10", 5, 20),
        ("Timeline 10", GenTimeline(10), 3, 10),
        ("Kanban 4x5", GenKanban(4, 5), 3, 10),
        ("Sankey 10", GenSankey(10), 3, 10),
        ("XYChart", GenXYChart(), 3, 10),
        ("Quadrant 8", GenQuadrant(8), 3, 10),
        ("Requirement 10", GenRequirement(10), 3, 10),
        ("GitGraph 20", GenGitGraph(20), 3, 10),
        ("Radar 5", GenRadar(5), 3, 10),
        ("Treemap 15", GenTreemap(15), 3, 10),
        ("UserJourney 5", GenUserJourney(5), 3, 10),
        ("Packet", GenPacket(), 3, 10),
        ("Block 12", GenBlock(12), 3, 10),
        ("C4 5", GenC4(5), 3, 10),
    };

    Console.WriteLine($"{"Test",-25} {"Warmup",-8} {"Iters",-8} {"Mean ms",-12} {"Min ms",-12} {"Max ms",-12} {"SVG KB",-10}");
    Console.WriteLine(new string('-', 87));

    foreach (var (name, source, warmup, iters) in tests)
    {
        string? result = null;
        try
        {
            // Warmup
            for (var w = 0; w < warmup; w++)
                result = Mermaid.Render(source, options);

            // Timed runs
            var times = new double[iters];
            var sw = new Stopwatch();
            for (var i = 0; i < iters; i++)
            {
                sw.Restart();
                result = Mermaid.Render(source, options);
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
            }

            var mean = times.Average();
            var min = times.Min();
            var max = times.Max();
            var sizeKb = result != null ? result.Length / 1024.0 : 0;

            Console.WriteLine($"{name,-25} {warmup,-8} {iters,-8} {mean,-12:F2} {min,-12:F2} {max,-12:F2} {sizeKb,-10:F1}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{name,-25} ERROR: {ex.Message}");
        }
    }
}

static string GenFlowchart(int nodes)
{
    var sb = new StringBuilder("flowchart TD\n");
    for (var i = 0; i < nodes; i++)
    {
        sb.AppendLine($"    N{i}[Node {i}] --> N{(i + 1) % nodes}[Node {(i + 1) % nodes}]");
        if (i % 5 == 0 && i + 3 < nodes) sb.AppendLine($"    N{i} --> N{i + 3}");
    }
    return sb.ToString();
}

static string GenSequence(int messages)
{
    var sb = new StringBuilder("sequenceDiagram\n    participant A\n    participant B\n    participant C\n    participant D\n");
    var p = new[] { "A", "B", "C", "D" };
    for (var i = 0; i < messages; i++)
        sb.AppendLine($"    {p[i % 4]}->>{p[(i + 1) % 4]}: Message {i}");
    return sb.ToString();
}

static string GenClass(int classes)
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

static string GenER(int entities)
{
    var sb = new StringBuilder("erDiagram\n");
    for (var i = 0; i < entities; i++)
    {
        sb.AppendLine($"    E{i} {{\n        string id PK\n        string name\n        int count\n    }}");
        if (i > 0) sb.AppendLine($"    E{i - 1} ||--o{{ E{i} : has");
    }
    return sb.ToString();
}

static string GenMindmap(int breadth, int depth)
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

static string GenGantt(int tasks)
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

static string GenState(int states)
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

static string GenTimeline(int events)
{
    var sb = new StringBuilder("timeline\n    title Project Timeline\n");
    for (var i = 0; i < events; i++)
        sb.AppendLine($"    2024-Q{(i % 4) + 1} : Event {i}");
    return sb.ToString();
}

static string GenKanban(int cols, int tasksPerCol)
{
    var sb = new StringBuilder("kanban\n");
    var colNames = new[] { "Backlog", "In Progress", "Review", "Done", "Deployed" };
    for (var c = 0; c < cols; c++)
    {
        sb.AppendLine($"  {colNames[c % colNames.Length]}");
        for (var t = 0; t < tasksPerCol; t++)
            sb.AppendLine($"    task{c * tasksPerCol + t}");
    }
    return sb.ToString();
}

static string GenSankey(int flows)
{
    var sb = new StringBuilder("sankey-beta\n");
    var sources = new[] { "Solar", "Wind", "Hydro", "Nuclear" };
    var targets = new[] { "Grid", "Battery", "Export" };
    var rand = new Random(42);
    for (var i = 0; i < flows; i++)
        sb.AppendLine($"{sources[i % sources.Length]},{targets[i % targets.Length]},{rand.Next(10, 100)}");
    return sb.ToString();
}

static string GenXYChart()
{
    var sb = new StringBuilder("xychart-beta\n    title \"Sales Data\"\n    x-axis [Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec]\n");
    sb.AppendLine("    y-axis \"Revenue\" 0 --> 1000");
    sb.AppendLine("    bar [120, 230, 310, 280, 450, 520, 490, 610, 580, 720, 690, 810]");
    sb.AppendLine("    line [100, 200, 290, 260, 420, 500, 470, 590, 560, 700, 670, 790]");
    return sb.ToString();
}

static string GenQuadrant(int points)
{
    var sb = new StringBuilder("quadrantChart\n    title Tech Assessment\n    x-axis Low Impact --> High Impact\n    y-axis Low Effort --> High Effort\n");
    var rand = new Random(42);
    for (var i = 0; i < points; i++)
        sb.AppendLine($"    Item{i}: [{rand.NextDouble():F2}, {rand.NextDouble():F2}]");
    return sb.ToString();
}

static string GenRequirement(int reqs)
{
    var sb = new StringBuilder("requirementDiagram\n");
    for (var i = 0; i < reqs; i++)
    {
        sb.AppendLine($"    requirement R{i} {{");
        sb.AppendLine($"        id: REQ-{i:D3}");
        sb.AppendLine($"        text: Requirement {i}");
        sb.AppendLine($"        risk: medium");
        sb.AppendLine($"        verifymethod: test");
        sb.AppendLine("    }");
        if (i > 0) sb.AppendLine($"    R{i - 1} - derives -> R{i}");
    }
    return sb.ToString();
}

static string GenGitGraph(int commits)
{
    var sb = new StringBuilder("gitGraph\n");
    for (var i = 0; i < commits; i++)
    {
        if (i == 5) sb.AppendLine("    branch develop");
        if (i == 10) sb.AppendLine("    checkout main");
        if (i == 12) sb.AppendLine("    merge develop");
        if (i == 15) sb.AppendLine("    branch feature");
        if (i == 18) sb.AppendLine("    checkout main");
        if (i == 19) sb.AppendLine("    merge feature");
        sb.AppendLine($"    commit id: \"c{i}\"");
    }
    return sb.ToString();
}

static string GenRadar(int axes)
{
    var sb = new StringBuilder("radar-beta\n    title Skills\n    axis");
    for (var i = 0; i < axes; i++)
        sb.Append(i == 0 ? $" Skill{i}" : $", Skill{i}");
    sb.AppendLine();

    var rand = new Random(42);
    for (var d = 0; d < 3; d++)
    {
        sb.Append($"    \"Team {d}\" -->");
        for (var a = 0; a < axes; a++)
            sb.Append(a == 0 ? $" {rand.Next(1, 10)}" : $", {rand.Next(1, 10)}");
        sb.AppendLine();
    }
    return sb.ToString();
}

static string GenTreemap(int nodes)
{
    var sb = new StringBuilder("treemap-beta\n    root\n");
    var rand = new Random(42);
    for (var i = 0; i < nodes; i++)
    {
        var depth = i < 3 ? 1 : (i < 8 ? 2 : 3);
        var indent = new string(' ', (depth + 1) * 4);
        sb.AppendLine($"{indent}Node{i}: {rand.Next(10, 100)}");
    }
    return sb.ToString();
}

static string GenUserJourney(int sections)
{
    var sb = new StringBuilder("journey\n    title User Flow\n");
    for (var s = 0; s < sections; s++)
    {
        sb.AppendLine($"    section Step {s + 1}");
        sb.AppendLine($"        Task {s}a: {3 + (s % 3)}: User");
        sb.AppendLine($"        Task {s}b: {2 + (s % 4)}: System");
    }
    return sb.ToString();
}

static string GenPacket() =>
    """
    packet-beta
        0-3: "Version"
        4-7: "IHL"
        8-15: "Type of Service"
        16-31: "Total Length"
        32-47: "Identification"
        48-50: "Flags"
        51-63: "Fragment Offset"
        64-71: "TTL"
        72-79: "Protocol"
        80-95: "Header Checksum"
        96-127: "Source Address"
        128-159: "Destination Address"
    """;

static string GenBlock(int nodes)
{
    var sb = new StringBuilder("block-beta\n    columns 3\n");
    for (var i = 0; i < nodes; i++)
        sb.AppendLine($"    B{i}[\"Block {i}\"]");
    return sb.ToString();
}

static string GenC4(int components)
{
    var sb = new StringBuilder("C4Context\n    title System Context\n");
    sb.AppendLine("    Person(user, \"User\", \"End user\")");
    for (var i = 0; i < components; i++)
    {
        sb.AppendLine($"    System(sys{i}, \"System {i}\", \"Description {i}\")");
        if (i == 0) sb.AppendLine($"    Rel(user, sys0, \"Uses\")");
        if (i > 0) sb.AppendLine($"    Rel(sys{i - 1}, sys{i}, \"Calls\")");
    }
    return sb.ToString();
}

static DagreInputGraph BuildDagreInputGraph(int nodeCount, int edgeCount, int seed)
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

    // Fill remaining edges randomly (forward only to avoid reverse-duplicate exceptions)
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

static void RunDagreInputGraphBenchmark()
{
    var tests = new (string Name, int Nodes, int Edges, int Seed, int Warmup, int Iterations)[]
    {
        ("Small (5N/8E)", 5, 8, 42, 5, 20),
        ("Medium (20N/30E)", 20, 30, 42, 3, 10),
        ("Large (50N/80E)", 50, 80, 42, 3, 10),
        ("Stress (200N/350E)", 200, 350, 99, 2, 5),
    };

    Console.WriteLine();
    Console.WriteLine("=== DagreInputGraph.Layout() (Indexed Engine) ===");
    Console.WriteLine();
    Console.WriteLine($"{"Test",-25} {"Mean ms",-12} {"Min ms",-12} {"Max ms",-12}");
    Console.WriteLine(new string('-', 61));

    foreach (var (name, nodeCount, edgeCount, seed, warmup, iters) in tests)
    {
        try
        {
            for (var w = 0; w < warmup; w++)
            {
                var g = BuildDagreInputGraph(nodeCount, edgeCount, seed);
                g.Layout();
            }
            var times = new double[iters];
            var sw = new Stopwatch();
            for (var i = 0; i < iters; i++)
            {
                var g = BuildDagreInputGraph(nodeCount, edgeCount, seed);
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

static void RunVisualComparison()
{
    Console.WriteLine("=== Visual Output: Indexed Engine Rendering ===");
    Console.WriteLine();

    var diagrams = new (string Name, string Source)[]
    {
        ("Simple LR", "flowchart LR\n    A[Start] --> B[Process] --> C[End]"),
        ("Simple TD", "flowchart TD\n    A[Christmas] -->|Get money| B(Go shopping)\n    B --> C{Let me think}\n    C -->|One| D[Laptop]\n    C -->|Two| E[iPhone]\n    C -->|Three| F[Car]"),
        ("Shapes", "flowchart TD\n    A[Rectangle]\n    B(Rounded)\n    C{Diamond}\n    D((Circle))"),
        ("Edge Labels", "flowchart LR\n    A --> |Yes| B\n    A --> |No| C"),
        ("Graph Keyword", "graph TD\n    A --> B --> C"),
        ("Flowchart 20", GenFlowchart(20)),
        ("Flowchart 50", GenFlowchart(50)),
        ("Flowchart 100", GenFlowchart(100)),
        ("State 10", GenState(10)),
        ("State 20", GenState(20)),
        ("Class 15", GenClass(15)),
        ("GitGraph 15", GenGitGraph(15)),
        ("C4 3", GenC4(3)),
        ("Timeline 6", GenTimeline(6)),
    };

    var options = new RenderOptions();
    int passed = 0, errors = 0;

    foreach (var (name, source) in diagrams)
    {
        try
        {
            var svg = Mermaid.Render(source, options);
            var sizeKb = svg.Length / 1024.0;
            Console.WriteLine($"  OK  {name,-25} ({sizeKb:F1} KB)");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ERR {name,-25} {ex.Message}");
            errors++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Results: {passed} rendered, {errors} errors");
}
