#!/usr/bin/env dotnet run
#:package Microsoft.Diagnostics.Tracing.TraceEvent@3.1.23

// Analyze .nettrace files from dotnet-trace
// Usage: dotnet run analyze-trace.cs [path-to.nettrace]
//
// Collect a trace (profile pre-built binary for best results):
//   dotnet build Naiad/src/Naiad.Benchmarks -c Release
//   dotnet-trace collect --duration 00:00:15 --output trace.nettrace \
//     -- dotnet exec Naiad/src/Naiad.Benchmarks/bin/Release/net10.0/Naiad.Benchmarks.dll --compare

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using System.Text.RegularExpressions;

var tracePath = args.Length > 0 ? args[0] : @"C:\Users\scott\AppData\Local\Temp\dagre-profile.nettrace";

if (!File.Exists(tracePath))
{
    Console.WriteLine($"Trace file not found: {tracePath}");
    Console.WriteLine("Usage: dotnet run analyze-trace.cs [path-to.nettrace]");
    return;
}

Console.WriteLine($"Analyzing: {tracePath} ({new FileInfo(tracePath).Length / 1024}KB)");
Console.WriteLine(new string('=', 90));

// Convert .nettrace → .etlx for resolved stacks
var etlxPath = TraceLog.CreateFromEventPipeDataFile(tracePath);
using var traceLog = TraceLog.OpenOrConvert(etlxPath);

Console.WriteLine($"Session duration: {traceLog.SessionDuration.TotalSeconds:F2}s");
Console.WriteLine($"Total events: {traceLog.EventCount:N0}");
Console.WriteLine();

// ─── CPU Sample Analysis via CallStack walking ─────────────────────
var inclusive = new Dictionary<string, double>();
var exclusive = new Dictionary<string, double>();
var callCount = new Dictionary<string, int>();

// Walk all events that have call stacks (CPU samples)
int eventsWithStacks = 0;
int totalEvents = 0;

foreach (var evt in traceLog.Events)
{
    totalEvents++;
    var cs = evt.CallStack();
    if (cs == null) continue;
    eventsWithStacks++;

    bool isLeaf = true;
    var frame = cs;
    while (frame != null)
    {
        var method = frame.CodeAddress.Method;
        string name = method?.FullMethodName ?? frame.CodeAddress.ModuleName ?? "?";

        if (name != "?" && !name.StartsWith("UNMANAGED"))
        {
            inclusive[name] = inclusive.GetValueOrDefault(name) + 1.0;
            if (isLeaf)
            {
                exclusive[name] = exclusive.GetValueOrDefault(name) + 1.0;
                callCount[name] = callCount.GetValueOrDefault(name) + 1;
            }
        }
        isLeaf = false;
        frame = frame.Caller;
    }
}

Console.WriteLine($"Total events: {totalEvents}, Events with call stacks: {eventsWithStacks}");
Console.WriteLine($"Unique methods: {inclusive.Count}");
Console.WriteLine();

// Filter for Dagre/Naiad code
var pattern = new Regex(
    @"Mostlylucid|Indexed|DagreLayout|NetworkSimplex|RunLayout|BrandesKopf|" +
    @"Normalize|Acyclic|NestingGraph|Order|Position|CrossCount|" +
    @"Barycenter|Benchmark|Mermaid|SvgBuilder|Flowchart|IndexedGraph|" +
    @"AddBorderSegments|ParentDummy|CoordinateSystem|DagreGraph",
    RegexOptions.IgnoreCase);

var dagre = inclusive
    .Where(kv => pattern.IsMatch(kv.Key))
    .OrderByDescending(kv => kv.Value)
    .Take(40)
    .ToList();

if (dagre.Count > 0)
{
    PrintHeader("DAGRE/NAIAD HOTSPOTS (inclusive, by sample count)");
    PrintRow("Method", "Incl", "Self", "Leaf Hits");
    PrintSep();

    foreach (var (name, inc) in dagre)
    {
        var exc = exclusive.GetValueOrDefault(name);
        var cnt = callCount.GetValueOrDefault(name);
        PrintRow(Shorten(name, 55), $"{inc:F0}", $"{exc:F0}", $"{cnt}");
    }
    Console.WriteLine();
}

if (inclusive.Count > 0)
{
    PrintHeader("TOP 30 SELF-TIME HOTSPOTS (all code, by sample count)");
    PrintRow("Method", "Self", "Incl", "Leaf Hits");
    PrintSep();

    var topSelf = exclusive
        .OrderByDescending(kv => kv.Value)
        .Take(30);

    foreach (var (name, exc) in topSelf)
    {
        var inc = inclusive.GetValueOrDefault(name);
        var cnt = callCount.GetValueOrDefault(name);
        PrintRow(Shorten(name, 55), $"{exc:F0}", $"{inc:F0}", $"{cnt}");
    }
    Console.WriteLine();
}

// ─── GC Events ─────────────────────────────────────────────────────
var gcStarts = traceLog.Events.Where(e => e.EventName.Contains("GC/Start")).ToList();
var gcAllocs = traceLog.Events.Where(e => e.EventName.Contains("AllocationTick")).ToList();

if (gcStarts.Count > 0 || gcAllocs.Count > 0)
{
    PrintHeader("GC ACTIVITY");
    Console.WriteLine($"  GC collections: {gcStarts.Count}");
    Console.WriteLine($"  Allocation ticks: {gcAllocs.Count}");
    Console.WriteLine();
}

// ─── JIT Compilation of our methods ────────────────────────────────
var jitPattern = new Regex(@"Mostlylucid|Indexed|Dagre|Benchmark|Naiad", RegexOptions.IgnoreCase);
var jitEvents = traceLog.Events
    .Where(e => (e.EventName.Contains("MethodLoad") || e.EventName.Contains("Method/Load"))
        && jitPattern.IsMatch(e.ToString()))
    .ToList();

if (jitEvents.Count > 0)
{
    PrintHeader($"JIT-COMPILED DAGRE METHODS ({jitEvents.Count})");
    foreach (var e in jitEvents.Take(50))
        Console.WriteLine($"  {e.TimeStampRelativeMSec,9:F1}ms  {Shorten(e.ToString(), 70)}");
    Console.WriteLine();
}

// ─── Event name summary ────────────────────────────────────────────
PrintHeader("EVENT TYPES SUMMARY");
var eventGroups = traceLog.Events
    .GroupBy(e => e.EventName)
    .OrderByDescending(g => g.Count())
    .Take(20);

foreach (var g in eventGroups)
    Console.WriteLine($"  {g.Key,-55} {g.Count(),8}");

// ─── Thread Activity ───────────────────────────────────────────────
Console.WriteLine();
PrintHeader("THREAD ACTIVITY");
var threads = traceLog.Threads.OrderByDescending(t => t.CPUMSec);
foreach (var t in threads.Take(10))
    Console.WriteLine($"  TID {t.ThreadID,-8} CPU: {t.CPUMSec,8:F1}ms  Name: {t.ThreadInfo ?? "(unnamed)"}");

// Cleanup
try { File.Delete(etlxPath); } catch { }

Console.WriteLine();
Console.WriteLine("Done.");

// ─── Helpers ───────────────────────────────────────────────────────
static string Shorten(string s, int max) =>
    s.Length <= max ? s : s[..(max - 3)] + "...";

static void PrintHeader(string title)
{
    Console.WriteLine($"┌{"".PadRight(88, '─')}┐");
    Console.WriteLine($"│  {title,-86}│");
    Console.WriteLine($"├{"".PadRight(88, '─')}┤");
}

static void PrintRow(string col1, string col2, string col3, string col4) =>
    Console.WriteLine($"  {col1,-55} {col2,9} {col3,9} {col4,8}");

static void PrintSep() =>
    Console.WriteLine("  " + new string('─', 83));
