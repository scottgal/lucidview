using Mostlylucid.ImageSharp.Svg;
using Mostlylucid.ImageSharp.Svg.Internal;

namespace Mostlylucid.ImageSharp.Svg.Benchmarks;

/// <summary>
/// Lightweight allocation profiler — measures GC allocation deltas around
/// each major stage of the render pipeline by querying
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> before and after.
/// Run with <c>dotnet run --project Mostlylucid.ImageSharp.Svg.Benchmarks
/// --no-build -c Release alloc</c>.
/// </summary>
internal static class AllocationProfile
{
    public static int Run()
    {
        var repoRoot = LocateRepoRoot();
        var shieldSvg = File.ReadAllText(System.IO.Path.Combine(repoRoot, "Mostlylucid.ImageSharp.Svg.Benchmarks/Fixtures/shield.svg"));
        var mermaidSvg = File.ReadAllText(System.IO.Path.Combine(repoRoot, "Naiad/docs/diagrams/flowchart.svg"));

        Console.WriteLine("=== Per-stage allocation breakdown ===");
        Console.WriteLine();

        // Warm up everything once.
        WarmUp(shieldSvg);

        Console.WriteLine("Shield (88x20, ~1KB SVG):");
        ProfileShield(shieldSvg);
        Console.WriteLine();

        Console.WriteLine("Mermaid flowchart (636x174, ~6KB SVG):");
        ProfileMermaid(mermaidSvg);
        Console.WriteLine();

        return 0;
    }

    private static void WarmUp(string svg)
    {
        for (var i = 0; i < 10; i++)
        {
            var r = SvgImage.LoadAsPng(svg, new SvgRenderOptions { Scale = 1f });
            _ = r.Bytes.Length;
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void ProfileShield(string svg)
    {
        ProfileStage("XML parse only", svg, content =>
        {
            var root = SvgXmlParser.Parse(content);
            return root.Children.Count;
        });

        ProfileStage("Parse + render to bitmap (no encode)", svg, content =>
        {
            var root = SvgXmlParser.Parse(content);
            var renderer = new SvgRenderer(new SvgRenderOptions { Scale = 1f });
            using var img = renderer.Render(root, out _, out _);
            return img.Width;
        });

        ProfileStage("Full LoadAsPng (parse + render + encode)", svg, content =>
        {
            var r = SvgImage.LoadAsPng(content, new SvgRenderOptions { Scale = 1f });
            return r.Bytes.Length;
        });
    }

    private static void ProfileMermaid(string svg)
    {
        ProfileStage("XML parse only", svg, content =>
        {
            var root = SvgXmlParser.Parse(content);
            return root.Children.Count;
        });

        ProfileStage("Parse + render to bitmap (no encode)", svg, content =>
        {
            var root = SvgXmlParser.Parse(content);
            var renderer = new SvgRenderer(new SvgRenderOptions { Scale = 1f });
            using var img = renderer.Render(root, out _, out _);
            return img.Width;
        });

        ProfileStage("Full LoadAsPng (parse + render + encode)", svg, content =>
        {
            var r = SvgImage.LoadAsPng(content, new SvgRenderOptions { Scale = 1f });
            return r.Bytes.Length;
        });
    }

    private static void ProfileStage(string name, string svg, Func<string, int> action)
    {
        // Measure as the average of N iterations to smooth one-off jitter.
        const int iterations = 10;
        long totalBytes = 0;
        long totalTicks = 0;

        for (var i = 0; i < iterations; i++)
        {
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _ = action(svg);
            sw.Stop();
            var afterBytes = GC.GetAllocatedBytesForCurrentThread();
            totalBytes += (afterBytes - beforeBytes);
            totalTicks += sw.ElapsedTicks;
        }

        var avgBytes = totalBytes / iterations;
        var avgUs = (totalTicks / iterations) * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency;
        Console.WriteLine($"  {name,-50}  {avgUs,8:F1} us  {avgBytes,10:N0} bytes");
    }

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(System.IO.Path.Combine(dir, "Naiad")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new DirectoryNotFoundException();
    }
}
