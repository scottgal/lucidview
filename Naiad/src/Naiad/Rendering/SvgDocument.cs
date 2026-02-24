using static MermaidSharp.Rendering.RenderUtils;
using System.Net;

namespace MermaidSharp.Rendering;

public class SvgDocument
{
    public double Width { get; set; }
    public double Height { get; set; }
    public string? ViewBoxOverride { get; set; }
    public string ViewBox => ViewBoxOverride ?? $"0 0 {FmtWidth(Width)} {Fmt(Height)}";
    public List<SvgElement> Elements { get; } = [];
    public SvgDefs Defs { get; } = new();
    public string? CssStyles { get; set; }

    /// <summary>
    /// Optional background color for the SVG. When set, a full-size rect is rendered behind all elements.
    /// </summary>
    public string? BackgroundColor { get; set; }

    /// <summary>
    /// Element hit regions for interactive diagrams (ID → bounding rect). Not serialized to SVG XML.
    /// </summary>
    public Dictionary<string, (double X, double Y, double Width, double Height)> HitRegions { get; } = [];

    /// <summary>
    /// Diagram-type-specific metadata (e.g. "c4Type"="Context", "boundary:myapp"="true"). Not serialized.
    /// </summary>
    public Dictionary<string, string> Metadata { get; } = [];

    // Mermaid.ink compatibility properties
    public string Id { get; set; } = "mermaid-svg";
    public string? DiagramClass { get; set; }
    public string? AriaRoledescription { get; set; }
    public string? Role { get; set; } = "graphics-document document";
    public string? FontAwesomeImport { get; set; } = "@import url(\"https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.7.2/css/all.min.css\");";
    
    /// <summary>
    /// Whether to include external resources in output.
    /// Security: When false, excludes external CDNs.
    /// </summary>
    public bool IncludeExternalResources { get; set; }

    public static SvgDocument CreateEmpty(RenderOptions? options = null) =>
        new() { Width = 0, Height = 0, ViewBoxOverride = "0 0 0 0" };

    public string ToXml()
    {
        var sb = new StringBuilder();

        // Build mermaid-compatible SVG root element (attribute order matches mermaid.ink exactly)
        // Use pixel width/height for SkiaSharp SVG rasterization compatibility
        sb.Append($"<svg id=\"{WebUtility.HtmlEncode(Id)}\" width=\"{FmtWidth(Width)}\" height=\"{Fmt(Height)}\" xmlns=\"http://www.w3.org/2000/svg\"");

        if (!string.IsNullOrEmpty(DiagramClass))
        {
            sb.Append($" class=\"{WebUtility.HtmlEncode(DiagramClass)}\"");
        }

        sb.Append($" viewBox=\"{ViewBox}\"");
        sb.Append($" style=\"max-width: {FmtWidth(Width)}px;\"");

        if (!string.IsNullOrEmpty(Role))
        {
            sb.Append($" role=\"{WebUtility.HtmlEncode(Role)}\"");
        }

        if (!string.IsNullOrEmpty(AriaRoledescription))
        {
            sb.Append($" aria-roledescription=\"{WebUtility.HtmlEncode(AriaRoledescription)}\"");
        }

        sb.Append(" xmlns:xlink=\"http://www.w3.org/1999/xlink\">");
        
        // Font Awesome import - only if IncludeExternalResources is true
        if (IncludeExternalResources && !string.IsNullOrEmpty(FontAwesomeImport))
        {
            sb.Append($"<style xmlns=\"http://www.w3.org/1999/xhtml\">{System.Net.WebUtility.HtmlEncode(FontAwesomeImport)}</style>");
        }
        
        // Main CSS styles - sanitized
        if (CssStyles is not null)
        {
            sb.Append($"<style>{SecurityValidator.SanitizeCss(CssStyles)}</style>");
        }

        if (Defs.HasContent)
        {
            sb.Append(Defs.ToXml());
        }

        // Background rect - renders behind all elements for dark mode support
        if (!string.IsNullOrEmpty(BackgroundColor))
        {
            sb.Append($"<rect width=\"{FmtWidth(Width)}\" height=\"{Fmt(Height)}\" fill=\"{WebUtility.HtmlEncode(BackgroundColor)}\" />");
        }

        foreach (var element in Elements)
        {
            sb.Append(element.ToXml());
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    static string FmtWidth(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}

public class SvgDefs
{
    public List<SvgMarker> Markers { get; } = [];
    public List<SvgGradient> Gradients { get; } = [];
    public List<SvgFilter> Filters { get; } = [];
    public List<string> RawFragments { get; } = [];

    public bool HasContent => Markers.Count > 0 || Gradients.Count > 0 || Filters.Count > 0 || RawFragments.Count > 0;

    public void AddRawFragment(string fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment))
            return;

        foreach (var existing in RawFragments)
        {
            if (existing.Equals(fragment, StringComparison.Ordinal))
                return;
        }

        RawFragments.Add(fragment);
    }

    public string ToXml()
    {
        var sb = new StringBuilder();
        sb.Append("<defs>");

        foreach (var marker in Markers)
        {
            sb.Append(marker.ToXml());
        }

        foreach (var gradient in Gradients)
        {
            sb.Append(gradient.ToXml());
        }

        foreach (var filter in Filters)
        {
            sb.Append(filter.ToXml());
        }

        foreach (var fragment in RawFragments)
        {
            sb.Append(fragment);
        }

        sb.Append("</defs>");
        return sb.ToString();
    }
}
