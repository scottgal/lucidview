using Mostlylucid.ImageSharp.Svg;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

// svg2png — tiny NativeAOT command-line wrapper around
// Mostlylucid.ImageSharp.Svg. Reads an SVG file from disk (or stdin) and
// writes a PNG to disk (or stdout). Designed to publish as a single ~5 MB
// native binary with no .NET runtime dependency.

const string Usage = """
    svg2png — convert SVG to PNG via Mostlylucid.ImageSharp.Svg

    USAGE
        svg2png <input.svg> <output.png> [options]
        svg2png - <output.png>           # read SVG from stdin
        svg2png <input.svg> -            # write PNG to stdout

    OPTIONS
        --scale <N>            Output scale multiplier (default: 1.0)
        --background <color>   Fill colour, e.g. "white" or "#ffffff"
        --quiet                Suppress informational output
        -h, --help             Show this help

    EXAMPLES
        svg2png shield.svg shield.png
        svg2png shield.svg shield.png --scale 2 --background white
        cat shield.svg | svg2png - shield.png --scale 2
    """;

if (args.Length == 0 || args is ["-h"] or ["--help"])
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 2 : 0;
}

// Two positional args: input and output. Either can be "-" for stdio.
string? input = null;
string? output = null;
var scale = 1f;
string? background = null;
var quiet = false;

for (var i = 0; i < args.Length; i++)
{
    var a = args[i];
    switch (a)
    {
        case "--scale":
            if (i + 1 >= args.Length) return Fail("--scale requires a value");
            if (!float.TryParse(args[++i], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out scale))
                return Fail($"--scale: '{args[i]}' is not a number");
            if (scale <= 0) return Fail("--scale must be positive");
            break;
        case "--background":
            if (i + 1 >= args.Length) return Fail("--background requires a value");
            background = args[++i];
            break;
        case "--quiet":
            quiet = true;
            break;
        case "-h":
        case "--help":
            Console.WriteLine(Usage);
            return 0;
        default:
            if (input == null) input = a;
            else if (output == null) output = a;
            else return Fail($"unexpected argument: {a}");
            break;
    }
}

if (input == null || output == null)
{
    Console.Error.WriteLine("svg2png: missing input/output paths");
    Console.Error.WriteLine();
    Console.Error.WriteLine(Usage);
    return 2;
}

// Resolve options.
Color? bgColor = null;
if (background != null)
{
    if (!Color.TryParse(background, out var parsed))
        return Fail($"--background: '{background}' is not a recognised colour");
    bgColor = parsed;
}

// Build a minimal ImageSharp configuration that registers only the PNG
// codec. This is the load-bearing trick that lets the AOT trimmer drop the
// JPEG/WebP/TIFF/BMP/GIF/PBM/QOI encoder and decoder modules from the
// final native binary — saves ~1.5 MB without changing functionality.
var pngOnlyConfig = new Configuration(new PngConfigurationModule());

var options = new SvgRenderOptions
{
    Scale = scale,
    Background = bgColor,
    Configuration = pngOnlyConfig,
};

// Read input.
string svgContent;
try
{
    if (input == "-")
    {
        using var stdin = Console.OpenStandardInput();
        using var reader = new StreamReader(stdin);
        svgContent = reader.ReadToEnd();
    }
    else
    {
        if (!File.Exists(input)) return Fail($"input file not found: {input}");
        svgContent = File.ReadAllText(input);
    }
}
catch (Exception ex)
{
    return Fail($"failed to read input: {ex.Message}");
}

// Render.
SvgPngResult result;
try
{
    result = SvgImage.LoadAsPng(svgContent, options);
}
catch (Exception ex)
{
    return Fail($"render failed: {ex.GetType().Name}: {ex.Message}");
}

if (result.Bytes.Length == 0)
    return Fail("render produced empty PNG");

// Write output.
try
{
    if (output == "-")
    {
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(result.Bytes, 0, result.Bytes.Length);
    }
    else
    {
        File.WriteAllBytes(output, result.Bytes);
    }
}
catch (Exception ex)
{
    return Fail($"failed to write output: {ex.Message}");
}

if (!quiet && output != "-")
{
    Console.WriteLine(
        $"svg2png: {input} → {output}  natural={result.NaturalWidth}x{result.NaturalHeight}  scale={scale}x  bytes={result.Bytes.Length:N0}");
}
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"svg2png: {message}");
    return 1;
}
