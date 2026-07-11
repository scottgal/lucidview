using System.Text.RegularExpressions;

namespace MermaidSharp.Diagrams.C4;

/// <summary>
/// Extracts Mermaid C4 <c>Update…</c> style directives (which the strict grammar does not model) out
/// of a diagram before parsing, capturing per-element background colours from
/// <c>UpdateElementStyle(id, $bgColor="…")</c>. Keeps the parser from choking on real-world C4 that
/// carries styling — and lets a component be coloured by its owning agent.
/// </summary>
internal static partial class C4StyleDirectives
{
    [GeneratedRegex(@"^\s*Update[A-Za-z]+\s*\(")]
    private static partial Regex UpdateLine();

    [GeneratedRegex(@"UpdateElementStyle\s*\(\s*([A-Za-z0-9_-]+)\s*,([^)]*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ElementStyle();

    [GeneratedRegex("\\$bgColor\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex BgColor();

    /// <summary>
    /// Returns the input with all <c>Update…(…)</c> directive lines removed, plus a map of element id →
    /// background colour captured from any <c>UpdateElementStyle</c> directives.
    /// </summary>
    public static (string Cleaned, IReadOnlyDictionary<string, string> BgColors) Extract(string input)
    {
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in ElementStyle().Matches(input))
        {
            var bg = BgColor().Match(m.Groups[2].Value);
            if (bg.Success)
                colors[m.Groups[1].Value] = bg.Groups[1].Value.Trim();
        }

        var cleaned = string.Join('\n',
            input.Replace("\r\n", "\n").Split('\n').Where(line => !UpdateLine().IsMatch(line)));

        return (cleaned, colors);
    }
}
