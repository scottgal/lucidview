using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using SixLabors.ImageSharp;

namespace Mostlylucid.ImageSharp.Svg.Internal;

/// <summary>
/// Numeric / color / transform / style parsing routines used by the renderer.
/// All allocate as little as possible (no Regex on hot paths). Public on
/// purpose so unit tests can target them in isolation.
/// </summary>
internal static class SvgValueParser
{
    public static double ParseNumber(string? value, double fallback = 0d)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var span = TrimUnits(value.AsSpan());
        return double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? n
            : fallback;
    }

    public static double? ParseNullableNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var span = TrimUnits(value.AsSpan());
        return double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;
    }

    private static ReadOnlySpan<char> TrimUnits(ReadOnlySpan<char> value)
    {
        // Strip CSS unit suffixes that we treat as user-units (px/pt). We
        // don't currently honour mm/cm/in/em/% — anything outside px/pt is
        // best-effort parsed by trimming all trailing letters.
        var i = value.Length;
        while (i > 0 && (char.IsLetter(value[i - 1]) || value[i - 1] == '%'))
            i--;
        return value[..i].Trim();
    }

    private static readonly List<double> EmptyNumberList = new(0);

    public static List<double> ParseNumberList(string? value)
    {
        // Allocation fast-path: return a shared empty list when there's
        // nothing to parse. This is called per <transform> command and per
        // viewBox / points / dasharray attribute — most elements have no
        // numbers to parse, so the common case allocates zero.
        if (string.IsNullOrEmpty(value)) return EmptyNumberList;
        var span = value.AsSpan();
        // Quick check for any digit before allocating the result list.
        var hasDigit = false;
        for (var k = 0; k < span.Length; k++)
        {
            if (char.IsDigit(span[k])) { hasDigit = true; break; }
        }
        if (!hasDigit) return EmptyNumberList;

        var result = new List<double>(8);
        var i = 0;
        while (i < span.Length)
        {
            // Skip separators (commas + whitespace).
            while (i < span.Length && (span[i] == ',' || char.IsWhiteSpace(span[i])))
                i++;

            if (i >= span.Length) break;

            var start = i;
            // Allow leading sign.
            if (span[i] == '+' || span[i] == '-') i++;

            while (i < span.Length && (char.IsDigit(span[i]) || span[i] == '.'
                   || span[i] == 'e' || span[i] == 'E'
                   || ((span[i] == '+' || span[i] == '-')
                       && (span[i - 1] == 'e' || span[i - 1] == 'E'))))
                i++;

            if (i == start) { i++; continue; }

            if (double.TryParse(span[start..i],
                NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                result.Add(n);
        }
        return result;
    }

    private static readonly Dictionary<string, string> EmptyStyleDict =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parse an inline <c>style="…"</c> attribute into a property dictionary.
    /// Returns a shared empty dictionary singleton when the input is null or
    /// empty so the common case (most SVG elements have no inline style) does
    /// not allocate.
    /// </summary>
    public static Dictionary<string, string> ParseStyle(string? style)
    {
        if (string.IsNullOrEmpty(style)) return EmptyStyleDict;

        // Skim for at least one ':' before allocating. Mermaid emits empty
        // <style> blocks on some elements; bail out without allocating.
        if (style.IndexOf(':') < 0) return EmptyStyleDict;

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in style.Split(';'))
        {
            var seg = raw.AsSpan().Trim();
            if (seg.IsEmpty) continue;
            var idx = seg.IndexOf(':');
            if (idx <= 0 || idx >= seg.Length - 1) continue;
            var key = seg[..idx].Trim().ToString();
            var val = seg[(idx + 1)..].Trim().ToString();
            if (key.Length > 0 && val.Length > 0)
                dict[key] = val;
        }
        return dict;
    }

    public static Matrix3x2 ParseTransform(string? transform)
    {
        if (string.IsNullOrWhiteSpace(transform)) return Matrix3x2.Identity;

        var current = Matrix3x2.Identity;
        var span = transform.AsSpan();
        var i = 0;
        while (i < span.Length)
        {
            while (i < span.Length && (char.IsWhiteSpace(span[i]) || span[i] == ',')) i++;
            if (i >= span.Length) break;

            var nameStart = i;
            while (i < span.Length && char.IsLetter(span[i])) i++;
            if (i == nameStart) { i++; continue; }
            var name = span[nameStart..i].ToString();

            while (i < span.Length && span[i] != '(') i++;
            if (i >= span.Length) break;
            i++; // (

            var argsStart = i;
            while (i < span.Length && span[i] != ')') i++;
            var argsText = span[argsStart..i].ToString();
            if (i < span.Length) i++; // )

            var args = ParseNumberList(argsText);
            current = Combine(current, BuildTransform(name, args));
        }

        return current;
    }

    private static Matrix3x2 BuildTransform(string name, List<double> args)
    {
        switch (name.ToLowerInvariant())
        {
            case "matrix" when args.Count >= 6:
                return new Matrix3x2(
                    (float)args[0], (float)args[1],
                    (float)args[2], (float)args[3],
                    (float)args[4], (float)args[5]);
            case "translate":
                return Matrix3x2.CreateTranslation(
                    args.Count > 0 ? (float)args[0] : 0,
                    args.Count > 1 ? (float)args[1] : 0);
            case "scale":
                return Matrix3x2.CreateScale(
                    args.Count > 0 ? (float)args[0] : 1,
                    args.Count > 1 ? (float)args[1] : (args.Count > 0 ? (float)args[0] : 1));
            case "rotate" when args.Count >= 3:
                return Matrix3x2.CreateRotation(
                    DegreesToRadians((float)args[0]),
                    new Vector2((float)args[1], (float)args[2]));
            case "rotate":
                return Matrix3x2.CreateRotation(
                    DegreesToRadians(args.Count > 0 ? (float)args[0] : 0));
            case "skewx":
                return new Matrix3x2(
                    1, 0,
                    MathF.Tan(DegreesToRadians(args.Count > 0 ? (float)args[0] : 0)), 1,
                    0, 0);
            case "skewy":
                return new Matrix3x2(
                    1, MathF.Tan(DegreesToRadians(args.Count > 0 ? (float)args[0] : 0)),
                    0, 1,
                    0, 0);
            default:
                return Matrix3x2.Identity;
        }
    }

    private static Matrix3x2 Combine(Matrix3x2 left, Matrix3x2 right) =>
        Matrix3x2.Multiply(right, left);

    private static float DegreesToRadians(float degrees) =>
        degrees * (MathF.PI / 180f);

    /// <summary>
    /// Parse a CSS color: named, hex (#RGB/#RGBA/#RRGGBB/#RRGGBBAA), or
    /// rgb()/rgba()/hsl()/hsla(). Returns null for "none", "transparent",
    /// or unparseable input. Paint-server (url(#...)) lookups are handled
    /// upstream — this method reports null for those.
    /// </summary>
    public static Color? ParseColor(string? value, double opacity = 1d)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();

        if (v.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return null;

        if (v.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return null;

        // CSS rgb() / rgba() — ImageSharp's Color.TryParse covers hex and a
        // limited set of named colors but not the functional notation, so we
        // intercept it here. hsl()/hsla() also routed through this branch.
        if (v.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) ||
            v.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
        {
            return ParseRgbFunction(v, opacity);
        }
        if (v.StartsWith("hsl(", StringComparison.OrdinalIgnoreCase) ||
            v.StartsWith("hsla(", StringComparison.OrdinalIgnoreCase))
        {
            return ParseHslFunction(v, opacity);
        }

        if (Color.TryParse(v, out var color))
            return ApplyOpacity(color, opacity);

        return null;
    }

    private static Color? ParseRgbFunction(string value, double opacity)
    {
        var open = value.IndexOf('(');
        var close = value.LastIndexOf(')');
        if (open < 0 || close <= open) return null;
        var inner = value.Substring(open + 1, close - open - 1);
        var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3) return null;

        var r = ParseColorChannel(parts[0]);
        var g = ParseColorChannel(parts[1]);
        var b = ParseColorChannel(parts[2]);
        var a = parts.Length >= 4
            ? Math.Clamp(double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var av) ? av : 1d, 0d, 1d)
            : 1d;

        var pixel = new SixLabors.ImageSharp.PixelFormats.Rgba32(
            (byte)Math.Clamp(r, 0, 255),
            (byte)Math.Clamp(g, 0, 255),
            (byte)Math.Clamp(b, 0, 255),
            (byte)Math.Clamp((int)Math.Round(a * 255), 0, 255));
        return ApplyOpacity(Color.FromPixel(pixel), opacity);
    }

    private static int ParseColorChannel(string token)
    {
        // Channels are either 0-255 ints or 0%-100% percentages.
        var t = token.Trim();
        if (t.EndsWith('%') &&
            double.TryParse(t[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            return (int)Math.Round(pct * 2.55);
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? (int)Math.Round(n)
            : 0;
    }

    private static Color? ParseHslFunction(string value, double opacity)
    {
        var open = value.IndexOf('(');
        var close = value.LastIndexOf(')');
        if (open < 0 || close <= open) return null;
        var inner = value.Substring(open + 1, close - open - 1);
        var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3) return null;

        if (!double.TryParse(parts[0].Replace("deg", "", StringComparison.OrdinalIgnoreCase),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) return null;
        var s = ParsePercent(parts[1]);
        var l = ParsePercent(parts[2]);
        var a = parts.Length >= 4
            ? Math.Clamp(double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var av) ? av : 1d, 0d, 1d)
            : 1d;

        var (r, g, b) = HslToRgb(h, s, l);
        var pixel = new SixLabors.ImageSharp.PixelFormats.Rgba32(
            (byte)Math.Clamp((int)Math.Round(r * 255), 0, 255),
            (byte)Math.Clamp((int)Math.Round(g * 255), 0, 255),
            (byte)Math.Clamp((int)Math.Round(b * 255), 0, 255),
            (byte)Math.Clamp((int)Math.Round(a * 255), 0, 255));
        return ApplyOpacity(Color.FromPixel(pixel), opacity);
    }

    private static double ParsePercent(string token)
    {
        var t = token.Trim();
        if (t.EndsWith('%')) t = t[..^1];
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? Math.Clamp(n / 100d, 0d, 1d)
            : 0d;
    }

    private static (double R, double G, double B) HslToRgb(double h, double s, double l)
    {
        // Standard CSS HSL→RGB conversion. h is in degrees, s/l in 0..1.
        h = ((h % 360) + 360) % 360 / 60;
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h % 2 - 1));
        var m = l - c / 2;
        double r1 = 0, g1 = 0, b1 = 0;
        switch ((int)Math.Floor(h))
        {
            case 0: r1 = c; g1 = x; b1 = 0; break;
            case 1: r1 = x; g1 = c; b1 = 0; break;
            case 2: r1 = 0; g1 = c; b1 = x; break;
            case 3: r1 = 0; g1 = x; b1 = c; break;
            case 4: r1 = x; g1 = 0; b1 = c; break;
            case 5: r1 = c; g1 = 0; b1 = x; break;
        }
        return (r1 + m, g1 + m, b1 + m);
    }

    public static Color ApplyOpacity(Color color, double opacity)
    {
        if (opacity >= 1d) return color;
        if (opacity <= 0d)
            return Color.FromPixel(new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 0, 0));

        var rgba = color.ToPixel<SixLabors.ImageSharp.PixelFormats.Rgba32>();
        rgba.A = (byte)Math.Clamp((int)Math.Round(rgba.A * opacity), 0, 255);
        return Color.FromPixel(rgba);
    }
}
