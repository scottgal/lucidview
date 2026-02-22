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

    public double Padding { get; set; } = 8;
    public string Theme { get; set; } = "default";

    /// <summary>
    /// Optional theme color overrides. When set, these values override the built-in skin colors.
    /// </summary>
    public ThemeColorOverrides? ThemeColors { get; set; }
    public double FontSize { get; set; } = 16;
    public string FontFamily { get; set; } = "\"trebuchet ms\", verdana, arial, sans-serif";
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

    /// <summary>
    /// Optional shape skin pack name or source.
    /// Supported values:
    /// - Built-ins: "default", "daisyui", "material", "material3", "fluent2", "glass", "wireframe"
    /// - Registered plugin packs (for example from a skin NuGet package)
    /// - File system path (directory or .zip/.naiadskin archive), when file-system packs are enabled.
    /// </summary>
    public string? SkinPack { get; set; }

    /// <summary>
    /// Enable loading shape skin packs from the local file system.
    /// Security default is false so untrusted Mermaid input cannot read arbitrary files.
    /// </summary>
    public bool AllowFileSystemSkinPacks { get; set; }

    /// <summary>
    /// Optional base directory used when <see cref="SkinPack"/> is a relative path.
    /// Ignored for built-in pack names.
    /// </summary>
    public string? SkinPackBaseDirectory { get; set; }

    /// <summary>
    /// Maps node IDs to skin shape names for the wireframe/custom skin rendering.
    /// Populated from <c>%% naiad: shapes nav=navbar, btn=button</c> directives.
    /// Keeps the mermaid syntax 100% compatible with mermaid.js.
    /// </summary>
    public Dictionary<string, string>? SkinShapeMap { get; set; }

    // --- Naiad extension directives (B1-B4) ---

    /// <summary>
    /// Minimum width for all nodes. Overridden by per-node sizes in <see cref="NodeSizeMap"/>.
    /// Set via <c>%% naiad: minNodeWidth=200</c>.
    /// </summary>
    public double MinNodeWidth { get; set; }

    /// <summary>
    /// Minimum height for all nodes. Overridden by per-node sizes in <see cref="NodeSizeMap"/>.
    /// Set via <c>%% naiad: minNodeHeight=60</c>.
    /// </summary>
    public double MinNodeHeight { get; set; }

    /// <summary>
    /// Per-node size overrides. Maps node ID to (Width, Height).
    /// Set via <c>%% naiad: nodeSize=A:200x60,B:150x40</c>.
    /// </summary>
    public Dictionary<string, (double W, double H)>? NodeSizeMap { get; set; }

    /// <summary>
    /// Custom rank separation (vertical spacing between ranks).
    /// Set via <c>%% naiad: rankSep=80</c>.
    /// </summary>
    public double? RankSeparation { get; set; }

    /// <summary>
    /// Custom node separation (horizontal spacing between nodes in same rank).
    /// Set via <c>%% naiad: nodeSep=40</c>.
    /// </summary>
    public double? NodeSeparation { get; set; }

    /// <summary>
    /// Custom edge separation.
    /// Set via <c>%% naiad: edgeSep=20</c>.
    /// </summary>
    public double? EdgeSeparation { get; set; }

    /// <summary>
    /// Edge curve style: Basis (smooth B-spline, default), Linear (straight segments), Step (orthogonal).
    /// Set via <c>%% naiad: curve=linear</c>.
    /// </summary>
    public CurveStyle CurveStyle { get; set; } = CurveStyle.Basis;

    /// <summary>
    /// When true, all nodes are sized to the maximum measured width and height.
    /// Set via <c>%% naiad: equalizeNodes=true</c>.
    /// </summary>
    public bool EqualizeNodeSizes { get; set; }

    public RenderOptions Clone() => new()
    {
        Padding = Padding,
        Theme = Theme,
        ThemeColors = ThemeColors,
        FontSize = FontSize,
        FontFamily = FontFamily,
        ShowBoundingBox = ShowBoundingBox,
        MaxNodes = MaxNodes,
        MaxEdges = MaxEdges,
        MaxComplexity = MaxComplexity,
        MaxInputSize = MaxInputSize,
        RenderTimeout = RenderTimeout,
        CurvedEdges = CurvedEdges,
        IncludeExternalResources = IncludeExternalResources,
        SkinPack = SkinPack,
        AllowFileSystemSkinPacks = AllowFileSystemSkinPacks,
        SkinPackBaseDirectory = SkinPackBaseDirectory,
        SkinShapeMap = SkinShapeMap != null ? new Dictionary<string, string>(SkinShapeMap) : null,
        MinNodeWidth = MinNodeWidth,
        MinNodeHeight = MinNodeHeight,
        NodeSizeMap = NodeSizeMap != null ? new Dictionary<string, (double, double)>(NodeSizeMap) : null,
        RankSeparation = RankSeparation,
        NodeSeparation = NodeSeparation,
        EdgeSeparation = EdgeSeparation,
        CurveStyle = CurveStyle,
        EqualizeNodeSizes = EqualizeNodeSizes
    };
}

/// <summary>
/// Edge curve interpolation style for flowchart edges.
/// </summary>
public enum CurveStyle
{
    /// <summary>Smooth B-spline (default, matches mermaid.js d3.curveBasis).</summary>
    Basis,
    /// <summary>Straight polyline segments connecting waypoints.</summary>
    Linear,
    /// <summary>Orthogonal (right-angle) routing between waypoints.</summary>
    Step
}
