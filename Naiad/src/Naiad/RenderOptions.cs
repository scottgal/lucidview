namespace MermaidSharp;

/// <summary>
/// Theme color overrides for diagram rendering. When set on RenderOptions,
/// these values override the built-in skin colors.
/// </summary>
public class ThemeColorOverrides
{
    public string? TextColor { get; set; }
    public string? BackgroundColor { get; set; }
    public string? NodeFill { get; set; }
    public string? NodeStroke { get; set; }
    public string? EdgeStroke { get; set; }
    public string? SubgraphFill { get; set; }
    public string? SubgraphStroke { get; set; }
    public string? EdgeLabelBackground { get; set; }
}

public class RenderOptions
{
    public static RenderOptions Default => new();

    public double Padding { get; set; } = 40;
    public string Theme { get; set; } = "default";

    /// <summary>
    /// Optional theme color overrides. When set, these values override the built-in skin colors.
    /// </summary>
    public ThemeColorOverrides? ThemeColors { get; set; }
    public double FontSize { get; set; } = 14;
    public string FontFamily { get; set; } = "Arial, sans-serif";
    public bool ShowBoundingBox { get; set; }
    
    /// <summary>
    /// Maximum number of nodes allowed in a diagram. Prevents DoS attacks.
    /// Set to 0 to disable this limit.
    /// </summary>
    public int MaxNodes { get; set; } = 1000;
    
    /// <summary>
    /// Maximum number of edges allowed in a diagram. Prevents DoS attacks.
    /// Set to 0 to disable this limit.
    /// </summary>
    public int MaxEdges { get; set; } = 500;
    
    /// <summary>
    /// Maximum complexity score (nodes + edges * 2) allowed. Prevents DoS attacks.
    /// Set to 0 to disable this limit.
    /// </summary>
    public int MaxComplexity { get; set; } = 2000;
    
    /// <summary>
    /// Maximum input text size in characters. Prevents DoS via large inputs.
    /// Set to 0 to disable this limit.
    /// </summary>
    public int MaxInputSize { get; set; } = 50000;
    
    /// <summary>
    /// Maximum rendering time in milliseconds. Prevents DoS via slow rendering.
    /// Set to 0 to disable this limit.
    /// </summary>
    public int RenderTimeout { get; set; } = 10000;
    
    /// <summary>
    /// Whether to use curved (rounded) edges instead of sharp corners in flowcharts.
    /// </summary>
    public bool CurvedEdges { get; set; } = true;

    /// <summary>
    /// Whether to include external resources like FontAwesome.
    /// Security: Set to false to prevent loading external CDNs.
    /// </summary>
    public bool IncludeExternalResources { get; set; }
}