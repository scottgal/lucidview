using System.Globalization;

namespace MermaidSharp.Rendering;

/// <summary>
/// Shared rendering utilities used across all diagram renderers.
/// </summary>
public static class RenderUtils
{
    /// <summary>Format a double for SVG output (2 decimal places max).</summary>
    public static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Measure single-line text width using character-count heuristic.</summary>
    public static double MeasureTextWidth(string text, double fontSize, bool bold = false)
    {
        var factor = bold ? 0.65 : 0.55;
        return text.Length * fontSize * factor;
    }

    /// <summary>
    /// Find the nearest edge point on a rectangle (centered at fromPos with given size)
    /// facing toward target coordinates.
    /// </summary>
    public static (double x, double y) GetConnectionPoint(
        double fromX, double fromY, double fromW, double fromH,
        double toX, double toY)
    {
        var dx = toX - fromX;
        var dy = toY - fromY;

        if (Math.Abs(dx) > Math.Abs(dy))
            return dx > 0
                ? (fromX + fromW / 2, fromY)
                : (fromX - fromW / 2, fromY);

        return dy > 0
            ? (fromX, fromY + fromH / 2)
            : (fromX, fromY - fromH / 2);
    }

    /// <summary>Measure text that may contain newlines, returning (width, height).</summary>
    public static (double Width, double Height) MeasureTextBlock(string text, double fontSize)
    {
        var lines = text.Split('\n');
        var maxLineWidth = 0.0;
        foreach (var line in lines)
        {
            var w = line.Length * fontSize * 0.55;
            if (w > maxLineWidth) maxLineWidth = w;
        }
        var height = lines.Length * fontSize * 1.5;
        return (maxLineWidth, height);
    }
}
