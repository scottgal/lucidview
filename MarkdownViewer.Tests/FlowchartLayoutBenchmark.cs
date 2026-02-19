using MermaidSharp;
using MermaidSharp.Diagrams.Flowchart;
using SkiaSharp;
using Svg.Skia;
using Xunit;
using Xunit.Abstractions;

namespace MarkdownViewer.Tests;

public class FlowchartLayoutBenchmark(ITestOutputHelper output)
{
    const int Warmup = 3;
    const int Iterations = 20;

    static readonly RenderOptions Options = new()
    {
        Theme = "dark",
        ThemeColors = new ThemeColorOverrides
        {
            TextColor = "#e6edf3",
            BackgroundColor = "#0d1117"
        }
    };

    [Fact]
    public void Benchmark_Small_Flowchart()
    {
        const string input = """
            flowchart LR
                A[Start] --> B{Decision}
                B -->|Yes| C[OK]
                B -->|No| D[Cancel]
            """;

        RunBenchmark("Small (4 nodes)", input);
    }

    [Fact]
    public void Benchmark_Medium_Flowchart()
    {
        const string input = """
            flowchart TD
                A[User Request] --> B{Authenticated?}
                B -->|Yes| C[Load Dashboard]
                B -->|No| D[Login Page]
                D --> E[Enter Credentials]
                E --> F{Valid?}
                F -->|Yes| C
                F -->|No| G[Show Error]
                G --> D
                C --> H[Show Data]
                C --> I[Show Charts]
                C --> J[Show Settings]
                H --> K[Export]
                I --> L[Filter]
                J --> M[Save]
            """;

        RunBenchmark("Medium (13 nodes)", input);
    }

    [Fact]
    public void Benchmark_Large_Flowchart()
    {
        // Generate a 50-node chain
        var lines = new System.Text.StringBuilder();
        lines.AppendLine("flowchart TD");
        for (var i = 0; i < 50; i++)
        {
            lines.AppendLine($"    N{i}[Node {i}] --> N{i + 1}[Node {i + 1}]");
        }

        RunBenchmark("Large (51 nodes, chain)", lines.ToString());
    }

    [Fact]
    public void Benchmark_Wide_Flowchart()
    {
        // Fan-out: 1 root -> 20 children -> 20 leaves
        var lines = new System.Text.StringBuilder();
        lines.AppendLine("flowchart TD");
        lines.AppendLine("    Root[Root]");
        for (var i = 0; i < 20; i++)
        {
            lines.AppendLine($"    Root --> C{i}[Child {i}]");
            lines.AppendLine($"    C{i} --> L{i}[Leaf {i}]");
        }

        RunBenchmark("Wide (41 nodes, fan-out)", lines.ToString());
    }

    [Fact]
    public void Benchmark_Subgraph_Flowchart()
    {
        const string input = """
            flowchart TD
                subgraph Frontend
                    A[React App] --> B[API Client]
                end
                subgraph Backend
                    C[API Gateway] --> D[Auth Service]
                    C --> E[Data Service]
                    D --> F[User DB]
                    E --> G[Main DB]
                end
                subgraph Infrastructure
                    H[Load Balancer] --> C
                    I[CDN] --> A
                end
                B --> H
            """;

        RunBenchmark("Subgraph (10 nodes, 3 subgraphs)", input);
    }

    private void RunBenchmark(string label, string input)
    {
        // Warmup
        for (var i = 0; i < Warmup; i++)
        {
            Mermaid.ParseAndLayoutFlowchart(input, Options);
            RenderFullPipeline(input);
        }

        // Benchmark layout-only (new native path)
        var layoutTimes = new List<double>();
        for (var i = 0; i < Iterations; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = Mermaid.ParseAndLayoutFlowchart(input, Options);
            sw.Stop();
            layoutTimes.Add(sw.Elapsed.TotalMilliseconds);
            Assert.NotNull(result);
        }

        // Benchmark full SVG render only
        var svgTimes = new List<double>();
        for (var i = 0; i < Iterations; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var svg = Mermaid.Render(input, Options);
            sw.Stop();
            svgTimes.Add(sw.Elapsed.TotalMilliseconds);
            Assert.False(string.IsNullOrEmpty(svg));
        }

        // Benchmark full pipeline: SVG → SkiaSharp rasterize → PNG bytes (old path)
        var pipelineTimes = new List<double>();
        for (var i = 0; i < Iterations; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var pngBytes = RenderFullPipeline(input);
            sw.Stop();
            pipelineTimes.Add(sw.Elapsed.TotalMilliseconds);
            Assert.True(pngBytes.Length > 0);
        }

        layoutTimes.Sort();
        svgTimes.Sort();
        pipelineTimes.Sort();

        var layoutMedian = layoutTimes[Iterations / 2];
        var svgMedian = svgTimes[Iterations / 2];
        var pipelineMedian = pipelineTimes[Iterations / 2];

        output.WriteLine($"=== {label} ===");
        output.WriteLine($"  Layout-only (native):     {layoutMedian:F2}ms median  (min {layoutTimes[0]:F2}, max {layoutTimes[^1]:F2})");
        output.WriteLine($"  SVG generation:           {svgMedian:F2}ms median  (min {svgTimes[0]:F2}, max {svgTimes[^1]:F2})");
        output.WriteLine($"  Full pipeline (SVG→PNG):  {pipelineMedian:F2}ms median  (min {pipelineTimes[0]:F2}, max {pipelineTimes[^1]:F2})");
        output.WriteLine($"  Native vs SVG→PNG:        {pipelineMedian / layoutMedian:F1}x faster");
        output.WriteLine("");
    }

    /// <summary>
    /// Simulates the old pipeline: Naiad SVG render → SkiaSharp rasterize → PNG bytes
    /// </summary>
    static byte[] RenderFullPipeline(string input)
    {
        var svgContent = Mermaid.Render(input, Options);

        using var svg = new SKSvg();
        svg.FromSvg(svgContent);
        if (svg.Picture is null) return [];

        var bounds = svg.Picture.CullRect;
        var scale = 2f;
        var width = (int)(bounds.Width * scale);
        var height = (int)(bounds.Height * scale);

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.DrawPicture(svg.Picture);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
