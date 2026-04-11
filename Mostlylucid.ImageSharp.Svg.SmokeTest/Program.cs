using Mostlylucid.ImageSharp.Svg;

// Tiny end-to-end harness for Mostlylucid.ImageSharp.Svg. Renders every
// .svg file under the input directory to a sibling output directory,
// reporting natural dimensions and PNG byte size for each. Exits non-zero
// if any file fails to render so this is CI-checkable.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: Mostlylucid.ImageSharp.Svg.SmokeTest <input-dir> <output-dir> [--scale N]");
    return 2;
}

var inputDir  = args[0];
var outputDir = args[1];
var scale     = 2f;
for (var i = 2; i < args.Length - 1; i++)
{
    if (args[i] == "--scale" && float.TryParse(args[i + 1], out var s)) scale = s;
}

if (!Directory.Exists(inputDir))
{
    Console.Error.WriteLine($"input directory not found: {inputDir}");
    return 2;
}
Directory.CreateDirectory(outputDir);

var files = Directory.GetFiles(inputDir, "*.svg", SearchOption.AllDirectories);
Console.WriteLine($"Rendering {files.Length} SVG(s) from {inputDir} to {outputDir} at scale {scale}x");

var successes = 0;
var failures  = 0;
var failureMessages = new List<string>();

foreach (var file in files)
{
    var name = Path.GetFileNameWithoutExtension(file);
    try
    {
        var svg = File.ReadAllText(file);
        var result = SvgImage.LoadAsPng(svg, new SvgRenderOptions { Scale = scale });
        var outPath = Path.Combine(outputDir, $"{name}.png");
        File.WriteAllBytes(outPath, result.Bytes);
        Console.WriteLine($"  OK   {name,-40} {result.NaturalWidth,5}x{result.NaturalHeight,-5}  {result.Bytes.Length,8} bytes");
        successes++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAIL {name,-40} {ex.GetType().Name}: {ex.Message}");
        failureMessages.Add($"{name}: {ex.Message}");
        failures++;
    }
}

Console.WriteLine();
Console.WriteLine($"Summary: {successes}/{files.Length} rendered successfully, {failures} failed");
return failures == 0 ? 0 : 1;
