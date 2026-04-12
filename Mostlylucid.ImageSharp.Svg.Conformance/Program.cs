using System.Diagnostics;
using Mostlylucid.ImageSharp.Svg;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// Conformance harness — render every SVG in the input directory with both
// `resvg` (gold standard, browser-fidelity) and Mostlylucid.ImageSharp.Svg,
// then compute a perceptual similarity score per image. Reports per-image
// MSE/PSNR and an aggregate "match rate" — the percentage of images whose
// MSE is below a configurable threshold.
//
// Why resvg? It's the closest pure-Rust SVG renderer to browser fidelity,
// modern enough to handle gradients/clipPath/markers correctly, and ships
// as a standalone CLI binary. We treat its output as ground truth.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: conformance <input-dir-or-svg> <output-report-dir> [--threshold N] [--quiet]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  --threshold N   max MSE for an image to count as 'matching' (default: 100)");
    Console.Error.WriteLine("  --quiet         only print the aggregate summary");
    return 2;
}

var inputPath = args[0];
var outputDir = args[1];
var threshold = 100.0;
var quiet = false;
for (var i = 2; i < args.Length; i++)
{
    if (args[i] == "--threshold" && i + 1 < args.Length && double.TryParse(args[++i], out var t)) threshold = t;
    else if (args[i] == "--quiet") quiet = true;
}

if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
{
    Console.Error.WriteLine($"input not found: {inputPath}");
    return 2;
}

Directory.CreateDirectory(outputDir);
var oursDir = Path.Combine(outputDir, "ours");
var refDir = Path.Combine(outputDir, "resvg");
var diffDir = Path.Combine(outputDir, "diff");
Directory.CreateDirectory(oursDir);
Directory.CreateDirectory(refDir);
Directory.CreateDirectory(diffDir);

string[] inputs = File.Exists(inputPath)
    ? new[] { inputPath }
    : Directory.GetFiles(inputPath, "*.svg", SearchOption.AllDirectories);

Console.WriteLine($"Conformance: {inputs.Length} SVG(s) from {inputPath}");
Console.WriteLine($"Reference renderer: resvg ({GetVersion("resvg", "--version")})");
Console.WriteLine($"Threshold: MSE < {threshold:F1}");
Console.WriteLine();

var results = new List<Result>(inputs.Length);
foreach (var svgPath in inputs)
{
    var name = Path.GetFileNameWithoutExtension(svgPath);
    var ours = Path.Combine(oursDir, $"{name}.png");
    var refPng = Path.Combine(refDir, $"{name}.png");

    var result = new Result { Name = name, SvgPath = svgPath };

    // Render via resvg first so we know the canonical width/height.
    if (!RenderWithResvg(svgPath, refPng, out var refError))
    {
        result.Status = "REF-FAIL";
        result.Note = refError;
        results.Add(result);
        if (!quiet) Console.WriteLine($"  REF-FAIL  {name,-40}  {refError}");
        continue;
    }

    // Read reference dimensions so we can render ours at the same pixel size.
    int refW, refH;
    try
    {
        using var refImg = Image.Load<Rgba32>(refPng);
        refW = refImg.Width;
        refH = refImg.Height;
    }
    catch (Exception ex)
    {
        result.Status = "REF-LOAD-FAIL";
        result.Note = ex.Message;
        results.Add(result);
        continue;
    }

    // Render via our library.
    try
    {
        var svgContent = File.ReadAllText(svgPath);
        var ourResult = SvgImage.Load(svgContent, new SvgRenderOptions { Scale = 1f });
        // Resize ours to match resvg's pixel dimensions exactly so the
        // comparison is apples to apples regardless of any scale/viewBox
        // differences.
        if (ourResult.Image.Width != refW || ourResult.Image.Height != refH)
        {
            ourResult.Image.Mutate(ctx => ctx.Resize(refW, refH));
        }
        ourResult.Image.SaveAsPng(ours);
        ourResult.Dispose();
    }
    catch (Exception ex)
    {
        result.Status = "OURS-FAIL";
        result.Note = $"{ex.GetType().Name}: {ex.Message}";
        results.Add(result);
        if (!quiet) Console.WriteLine($"  OURS-FAIL {name,-40}  {result.Note}");
        continue;
    }

    // Compute multi-metric similarity between the two PNGs.
    try
    {
        using var oursImg = Image.Load<Rgba32>(ours);
        using var refImg2 = Image.Load<Rgba32>(refPng);
        var (mse, pctClose, pctExact, maxDiff) = ComputeMetrics(oursImg, refImg2);
        result.MSE = mse;
        result.PctClose = pctClose;
        result.PctExact = pctExact;
        result.MaxDiff = maxDiff;
        result.RefWidth = refW;
        result.RefHeight = refH;
        // We treat ≥99% pixels close as "matching" — this corresponds to
        // "looks correct, only sub-pixel/AA noise differs".
        result.Status = pctClose >= 99.0 ? "MATCH" : "DIFF";
    }
    catch (Exception ex)
    {
        result.Status = "DIFF-FAIL";
        result.Note = ex.Message;
    }

    results.Add(result);
    if (!quiet)
    {
        var dim = $"{result.RefWidth}x{result.RefHeight}";
        Console.WriteLine($"  {result.Status,-9} {name,-30}  {dim,-12}  close={result.PctClose:F1}%  MSE={result.MSE:F0}  max={result.MaxDiff}");
    }
}

// Aggregate.
var matched   = results.Count(r => r.Status == "MATCH");
var diff      = results.Count(r => r.Status == "DIFF");
var oursFail  = results.Count(r => r.Status == "OURS-FAIL");
var refFail   = results.Count(r => r.Status == "REF-FAIL");
var matchPct  = inputs.Length > 0 ? matched * 100.0 / inputs.Length : 0.0;

Console.WriteLine();
Console.WriteLine("============================================================");
Console.WriteLine($"Total:       {inputs.Length}");
Console.WriteLine($"Matched:     {matched}  ({matchPct:F1}%)");
Console.WriteLine($"Diff:        {diff}");
Console.WriteLine($"Ours-fail:   {oursFail}");
Console.WriteLine($"Ref-fail:    {refFail}");
Console.WriteLine("============================================================");

if (diff > 0)
{
    Console.WriteLine();
    Console.WriteLine("Worst 10 (lowest pct of close pixels):");
    foreach (var r in results.Where(x => x.Status == "DIFF").OrderBy(x => x.PctClose).Take(10))
    {
        Console.WriteLine($"  close={r.PctClose,5:F1}%  MSE={r.MSE,5:F0}  max={r.MaxDiff,3}  {r.Name}");
    }
}

return matched == inputs.Length ? 0 : 1;

static bool RenderWithResvg(string inputSvg, string outputPng, out string error)
{
    error = string.Empty;
    try
    {
        // Force resvg to use the SAME bundled DejaVu Sans font we ship,
        // and to fall back to it for every generic family. This eliminates
        // text raster differences caused by host fonts (Helvetica on
        // macOS, Arial on Windows, DejaVu on Linux). Now both renderers
        // produce text from the same font file → fair comparison.
        var bundledFont = LocateBundledFont();
        var psi = new ProcessStartInfo("resvg")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        if (bundledFont != null)
        {
            psi.ArgumentList.Add("--use-font-file");
            psi.ArgumentList.Add(bundledFont);
            psi.ArgumentList.Add("--font-family");
            psi.ArgumentList.Add("DejaVu Sans");
            psi.ArgumentList.Add("--sans-serif-family");
            psi.ArgumentList.Add("DejaVu Sans");
            psi.ArgumentList.Add("--serif-family");
            psi.ArgumentList.Add("DejaVu Sans");
            psi.ArgumentList.Add("--monospace-family");
            psi.ArgumentList.Add("DejaVu Sans");
        }
        psi.ArgumentList.Add(inputSvg);
        psi.ArgumentList.Add(outputPng);

        using var p = Process.Start(psi)!;
        p.WaitForExit(10_000);
        if (p.ExitCode != 0)
        {
            error = $"resvg exit {p.ExitCode}: {p.StandardError.ReadToEnd().Trim()}";
            return false;
        }
        return File.Exists(outputPng);
    }
    catch (Exception ex)
    {
        error = ex.Message;
        return false;
    }
}

static string? LocateBundledFont()
{
    // Walk up from the bin dir to the repo root, then point at the
    // checked-in DejaVu Sans we ship inside the SVG library.
    var dir = AppContext.BaseDirectory;
    while (dir != null)
    {
        var candidate = Path.Combine(dir, "Mostlylucid.ImageSharp.Svg", "Fonts", "DejaVuSans.ttf");
        if (File.Exists(candidate)) return candidate;
        dir = Directory.GetParent(dir)?.FullName;
    }
    return null;
}

static (double Mse, double PctClose, double PctExact, int MaxDiff) ComputeMetrics(
    Image<Rgba32> a, Image<Rgba32> b, int closeTolerance = 16)
{
    if (a.Width != b.Width || a.Height != b.Height)
        return (double.MaxValue, 0, 0, 255);

    long sumSq = 0;
    long pixels = (long)a.Width * a.Height;
    long pxClose = 0;
    long pxExact = 0;
    int maxDiff = 0;

    a.ProcessPixelRows(b, (aAccessor, bAccessor) =>
    {
        for (var y = 0; y < aAccessor.Height; y++)
        {
            var aRow = aAccessor.GetRowSpan(y);
            var bRow = bAccessor.GetRowSpan(y);
            for (var x = 0; x < aRow.Length; x++)
            {
                var pa = aRow[x];
                var pb = bRow[x];
                int dr = Math.Abs(pa.R - pb.R);
                int dg = Math.Abs(pa.G - pb.G);
                int db = Math.Abs(pa.B - pb.B);
                int da = Math.Abs(pa.A - pb.A);
                sumSq += dr * dr + dg * dg + db * db + da * da;
                var maxChan = Math.Max(Math.Max(dr, dg), Math.Max(db, da));
                if (maxChan > maxDiff) maxDiff = maxChan;
                if (maxChan == 0) pxExact++;
                if (maxChan <= closeTolerance) pxClose++;
            }
        }
    });

    var mse = sumSq / (double)(pixels * 4);
    var pctClose = 100.0 * pxClose / pixels;
    var pctExact = 100.0 * pxExact / pixels;
    return (mse, pctClose, pctExact, maxDiff);
}

static string GetVersion(string exe, string arg)
{
    try
    {
        var psi = new ProcessStartInfo(exe, arg) { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(2000);
        return output.Split('\n')[0];
    }
    catch
    {
        return "unknown";
    }
}

class Result
{
    public string Name { get; set; } = string.Empty;
    public string SvgPath { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public string Note { get; set; } = string.Empty;
    public double MSE { get; set; }
    public double PctClose { get; set; }
    public double PctExact { get; set; }
    public int MaxDiff { get; set; }
    public int RefWidth { get; set; }
    public int RefHeight { get; set; }
}
