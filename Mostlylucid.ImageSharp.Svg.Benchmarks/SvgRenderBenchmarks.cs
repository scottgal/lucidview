using BenchmarkDotNet.Attributes;
using Mostlylucid.ImageSharp.Svg;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IOPath = System.IO.Path;

namespace Mostlylucid.ImageSharp.Svg.Benchmarks;

/// <summary>
/// Three-tier benchmark covering the SVG sizes the renderer actually sees:
/// shields (tiny, ~1KB, simple), mermaid flowchart (medium, ~10KB, paths +
/// text + CSS classes), and a complex Naiad diagram (~50KB, gradients +
/// many shapes). Measures wall time and allocations per render.
/// </summary>
[MemoryDiagnoser]
public class SvgRenderBenchmarks
{
    private string _shieldSvg = string.Empty;
    private string _mermaidFlowchartSvg = string.Empty;
    private string _largeC4Svg = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        // Walk up from the bin folder to find the repo root and then load
        // checked-in fixtures. The fixture set must match what svg2png and
        // lucidview actually render in production.
        var repoRoot = LocateRepoRoot();
        _shieldSvg = File.ReadAllText(IOPath.Combine(repoRoot, "Mostlylucid.ImageSharp.Svg.Benchmarks/Fixtures/shield.svg"));
        _mermaidFlowchartSvg = File.ReadAllText(IOPath.Combine(repoRoot, "Naiad/docs/diagrams/flowchart.svg"));
        _largeC4Svg = File.ReadAllText(IOPath.Combine(repoRoot, "Naiad/docs/diagrams/c4container.svg"));
    }

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(IOPath.Combine(dir, "lucidview.sln")) &&
               !Directory.Exists(IOPath.Combine(dir, "Naiad")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        return dir ?? throw new DirectoryNotFoundException("Could not locate repo root");
    }

    [Benchmark(Description = "Shield (88×20, ~1KB)")]
    public int RenderShield()
    {
        var result = SvgImage.LoadAsPng(_shieldSvg, new SvgRenderOptions { Scale = 2f });
        return result.Bytes.Length;
    }

    [Benchmark(Description = "Mermaid flowchart (636×174, ~10KB)")]
    public int RenderMermaidFlowchart()
    {
        var result = SvgImage.LoadAsPng(_mermaidFlowchartSvg, new SvgRenderOptions { Scale = 2f });
        return result.Bytes.Length;
    }

    [Benchmark(Description = "C4 container diagram (~766×430, ~30KB)")]
    public int RenderC4Container()
    {
        var result = SvgImage.LoadAsPng(_largeC4Svg, new SvgRenderOptions { Scale = 2f });
        return result.Bytes.Length;
    }

    // ─── Baseline: pure ImageSharp work without any SVG parsing/rendering. ───
    // Measures the floor — what ImageSharp itself costs for a 176×40 bitmap
    // (the shield's 2× output size). Anything our renderer adds on top is
    // the SVG parsing + draw orchestration cost.

    [Benchmark(Description = "Parse-only: shield SVG XML → AST (no rendering)")]
    public int ParseShieldOnly()
    {
        // True parse-only: just build the AST, no ImageSharp work at all.
        var root = Mostlylucid.ImageSharp.Svg.Internal.SvgXmlParser.Parse(_shieldSvg);
        return root.Children.Count;
    }

    [Benchmark(Description = "Render-only: pre-parsed shield AST → bitmap (no encode)")]
    public int RenderShieldFromAst()
    {
        var root = Mostlylucid.ImageSharp.Svg.Internal.SvgXmlParser.Parse(_shieldSvg);
        var renderer = new Mostlylucid.ImageSharp.Svg.Internal.SvgRenderer(new SvgRenderOptions { Scale = 1f });
        using var image = renderer.Render(root, out _, out _);
        return image.Width;
    }

#pragma warning disable CA1822 // benchmark method must be instance
    [Benchmark(Description = "Baseline: Image<Rgba32>(176×40) + clear + 3 fills + PNG encode")]
    public int BaselineImageSharp()
    {
        using var image = new Image<Rgba32>(176, 40);
        image.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);
            // Three filled rects to simulate the shield's three colour bands.
            ctx.Fill(Color.DimGray,   new RectangularPolygon(0,    0, 74, 40));
            ctx.Fill(Color.LimeGreen, new RectangularPolygon(74,   0, 102, 40));
            ctx.Fill(Color.Black,     new RectangularPolygon(0,    0, 176, 40));
        });
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return (int)ms.Length;
    }

    [Benchmark(Description = "Baseline: Image<Rgba32>(176×40) + 16 fills (matches shield element count)")]
    public int BaselineSixteenFills()
    {
        using var image = new Image<Rgba32>(176, 40);
        image.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);
            for (var i = 0; i < 16; i++)
                ctx.Fill(Color.DimGray, new RectangularPolygon(i * 10, 0, 8, 40));
        });
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return (int)ms.Length;
    }
#pragma warning restore CA1822
}
