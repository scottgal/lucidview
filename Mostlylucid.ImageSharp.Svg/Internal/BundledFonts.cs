using System.Reflection;
using SixLabors.Fonts;

namespace Mostlylucid.ImageSharp.Svg.Internal;

/// <summary>
/// Loads the embedded DejaVu Sans font on first use and exposes it as the
/// universal fallback for the SVG renderer. This is what makes text
/// rendering platform-independent: without it we'd fall back to whatever
/// the host happens to have installed (Helvetica on macOS, Arial on
/// Windows, DejaVu on Linux), and the rendered glyph metrics would differ.
/// With it, every machine renders the same SVG to byte-identical text.
/// </summary>
internal static class BundledFonts
{
    private const string ResourceName = "Mostlylucid.ImageSharp.Svg.Fonts.DejaVuSans.ttf";
    private static FontCollection? _collection;
    private static FontFamily? _fallback;
    private static readonly object _lock = new();

    /// <summary>
    /// The DejaVu Sans family loaded from the embedded resource. Triggers
    /// the one-time load on first call. Returns null only if the resource
    /// is missing (which would mean the package is broken).
    /// </summary>
    public static FontFamily? Fallback
    {
        get
        {
            if (_fallback.HasValue) return _fallback;
            lock (_lock)
            {
                if (_fallback.HasValue) return _fallback;
                LoadCore();
                return _fallback;
            }
        }
    }

    private static void LoadCore()
    {
        try
        {
            _collection = new FontCollection();
            var asm = typeof(BundledFonts).Assembly;
            using var stream = asm.GetManifestResourceStream(ResourceName);
            if (stream == null)
                return;
            var family = _collection.Add(stream);
            _fallback = family;
        }
        catch
        {
            // Leave _fallback null — caller will fall back to system fonts.
        }
    }
}
