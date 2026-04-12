using SixLabors.ImageSharp;

namespace Mostlylucid.ImageSharp.Svg;

/// <summary>
/// Configuration knobs for SVG rasterization. All properties are optional —
/// the defaults render at 1× with a transparent background.
/// </summary>
public sealed class SvgRenderOptions
{
    /// <summary>
    /// Output scale multiplier. <c>2f</c> renders a 2× bitmap suitable for
    /// hi-DPI displays without breaking the SVG's intrinsic dimensions
    /// (returned separately by <see cref="SvgImage.Load"/>).
    /// </summary>
    public float Scale { get; set; } = 1f;

    /// <summary>
    /// Background fill colour. Null means transparent.
    /// </summary>
    public Color? Background { get; set; }

    /// <summary>
    /// Family used when an SVG specifies a font that isn't installed on the
    /// system. The renderer also falls back to the first available system
    /// font if this name doesn't resolve.
    /// </summary>
    public string FallbackFontFamily { get; set; } = "DejaVu Sans";

    /// <summary>
    /// When true (the default) the renderer uses the embedded DejaVu Sans
    /// font for ALL text, regardless of what the SVG's <c>font-family</c>
    /// attribute requests. This is the only way to get byte-identical text
    /// rendering across Windows / macOS / Linux — the alternative is each
    /// host falling back to whatever it has installed (Helvetica on macOS,
    /// Arial on Windows, DejaVu on Linux), and the rasterized glyphs vary.
    ///
    /// Set to false if you want the renderer to honour the SVG's font
    /// request first and fall back to bundled DejaVu only when none of the
    /// requested families are installed.
    /// </summary>
    public bool ForceBundledFont { get; set; } = true;

    /// <summary>
    /// Optional ImageSharp <see cref="Configuration"/> override. Pass a
    /// minimal PNG-only configuration in AOT/trimmed builds to let the
    /// trimmer drop the JPEG/WebP/TIFF/BMP/GIF/PBM/QOI encoder + decoder
    /// modules from the published binary. When null, the renderer uses
    /// <see cref="Configuration.Default"/> which registers every format
    /// ImageSharp ships with.
    /// </summary>
    public Configuration? Configuration { get; set; }
}
