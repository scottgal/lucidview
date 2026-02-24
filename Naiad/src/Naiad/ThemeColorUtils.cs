using System.Globalization;

namespace MermaidSharp;

internal static class ThemeColorUtils
{
    internal static bool IsDarkColor(string? color)
    {
        if (!TryParseColor(color, out var r, out var g, out var b))
            return false;

        return (0.299 * r + 0.587 * g + 0.114 * b) < 128;
    }

    static bool TryParseColor(string? color, out byte r, out byte g, out byte b) =>
        TryParseHexColor(color, out r, out g, out b) ||
        TryParseRgbColor(color, out r, out g, out b);

    static bool TryParseHexColor(string? color, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(color)) return false;

        var c = color.Trim();
        if (!c.StartsWith('#')) return false;

        if (c.Length == 7 &&
            byte.TryParse(c[1..3], NumberStyles.HexNumber, null, out r) &&
            byte.TryParse(c[3..5], NumberStyles.HexNumber, null, out g) &&
            byte.TryParse(c[5..7], NumberStyles.HexNumber, null, out b))
            return true;

        if (c.Length == 4 &&
            byte.TryParse(new string(c[1], 2), NumberStyles.HexNumber, null, out r) &&
            byte.TryParse(new string(c[2], 2), NumberStyles.HexNumber, null, out g) &&
            byte.TryParse(new string(c[3], 2), NumberStyles.HexNumber, null, out b))
            return true;

        return false;
    }

    static bool TryParseRgbColor(string? color, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(color)) return false;

        var c = color.Trim();
        var prefix = c.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) ? "rgba(" :
            c.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) ? "rgb(" : null;
        if (prefix is null || !c.EndsWith(')'))
            return false;

        var inner = c[prefix.Length..^1];
        var parts = inner.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rd) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var gd) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var bd))
            return false;

        r = (byte)Math.Clamp((int)Math.Round(rd), 0, 255);
        g = (byte)Math.Clamp((int)Math.Round(gd), 0, 255);
        b = (byte)Math.Clamp((int)Math.Round(bd), 0, 255);
        return true;
    }
}
