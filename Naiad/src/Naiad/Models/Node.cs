namespace MermaidSharp.Models;

public class Node
{
    public required string Id { get; init; }
    public string? Label { get; set; }
    public NodeShape Shape { get; set; } = NodeShape.Rectangle;
    public double Width { get; set; }
    public double Height { get; set; }
    public Position Position { get; set; }
    public Style Style { get; set; } = new();
    public string? CssClass { get; set; }
    public string? Link { get; set; }
    public string? Tooltip { get; set; }
    public string? SkinShapeName { get; set; }
    public string? ParentId { get; set; }
    public bool IsGroup { get; set; }

    // Layout properties (set by layout engine)
    public int Rank { get; set; }
    public int Order { get; set; }

    /// <summary>
    /// When text is wrapped during measurement, this stores the wrapped version
    /// with newlines inserted. Used for rendering multi-line text in nodes.
    /// </summary>
    public string? WrappedLabel { get; set; }

    /// <summary>
    /// Display label with HTML line breaks converted to newlines and formatting tags stripped.
    /// Use this for plain-text rendering contexts (measurement, Avalonia FormattedText).
    /// </summary>
    public string DisplayLabel => StripHtmlTags(Label ?? Id);

    /// <summary>
    /// Convert HTML line breaks to newlines and strip all HTML tags, preserving text content.
    /// </summary>
    private static string StripHtmlTags(string label)
    {
        // Replace HTML line break variants with actual newlines
        label = System.Text.RegularExpressions.Regex.Replace(
            label, @"<br\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Strip ALL remaining HTML tags but keep their text content
        label = System.Text.RegularExpressions.Regex.Replace(label, @"<[^>]+>", "");

        // Decode HTML entities so they don't get double-encoded by SVG serialization
        label = label
            .Replace("&quot;", "\"")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&apos;", "'")
            .Replace("&#39;", "'");

        return label.Trim();
    }

    public Rect Bounds => new(
        Position.X - Width / 2,
        Position.Y - Height / 2,
        Width,
        Height);
}
