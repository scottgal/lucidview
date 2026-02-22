namespace MermaidSharp.Diagrams.Flowchart;

public sealed record FlowchartSkin(
    string Name,
    string Background,
    string TextColor,
    string NodeFill,
    string NodeStroke,
    string EdgeStroke,
    string EdgeLabelBackground,
    string SubgraphFill,
    string SubgraphStroke,
    string SubgraphTitleColor,
    double NodeCornerRadius = 0)
{
    public static FlowchartSkin Resolve(string? theme, ThemeColorOverrides? overrides = null)
    {
        var key = (theme ?? "default").Trim().ToLowerInvariant();
        var skin = key switch
        {
            "dark" => NaiadDark,
            "forest" => Forest,
            "neutral" => Neutral,
            "base" => Naiad,
            "mermaid" => Mermaid,
            "mermaid-dark" => MermaidDark,
            _ => Naiad
        };

        // If no explicit non-default theme was selected, infer dark/light from
        // host-provided background so flowcharts follow the ambient app theme.
        if (overrides?.BackgroundColor is { Length: > 0 } bg &&
            (string.IsNullOrWhiteSpace(theme) || key is "default" or "base"))
        {
            skin = ThemeColorUtils.IsDarkColor(bg) ? NaiadDark : Naiad;
        }

        if (overrides is null) return skin;

        return skin with
        {
            TextColor = overrides.TextColor ?? skin.TextColor,
            Background = overrides.BackgroundColor ?? skin.Background,
            NodeFill = overrides.NodeFill ?? skin.NodeFill,
            NodeStroke = overrides.NodeStroke ?? skin.NodeStroke,
            EdgeStroke = overrides.EdgeStroke ?? skin.EdgeStroke,
            EdgeLabelBackground = overrides.EdgeLabelBackground ?? skin.EdgeLabelBackground,
            SubgraphFill = overrides.SubgraphFill ?? skin.SubgraphFill,
            SubgraphStroke = overrides.SubgraphStroke ?? skin.SubgraphStroke,
            SubgraphTitleColor = overrides.TextColor ?? skin.SubgraphTitleColor
        };
    }

    // --- Naiad skins (default): Tailwind-inspired, polished, rounded corners ---

    static readonly FlowchartSkin Naiad = new(
        Name: "default",
        Background: "#ffffff",
        TextColor: "#334155",           // slate-700
        NodeFill: "#eff6ff",            // blue-50
        NodeStroke: "#3b82f6",          // blue-500
        EdgeStroke: "#475569",          // slate-600
        EdgeLabelBackground: "rgba(241,245,249,0.9)",  // slate-100 @ 90%
        SubgraphFill: "rgba(239,246,255,0.2)",         // blue-50 @ 20%
        SubgraphStroke: "#bfdbfe",      // blue-200
        SubgraphTitleColor: "#2563eb",  // blue-600
        NodeCornerRadius: 5);

    static readonly FlowchartSkin NaiadDark = new(
        Name: "dark",
        Background: "#0f172a",          // slate-900
        TextColor: "#e2e8f0",           // slate-200
        NodeFill: "#1e293b",            // slate-800
        NodeStroke: "#60a5fa",          // blue-400
        EdgeStroke: "#94a3b8",          // slate-400
        EdgeLabelBackground: "rgba(30,41,59,0.9)",     // slate-800 @ 90%
        SubgraphFill: "rgba(59,130,246,0.08)",         // blue-500 @ 8%
        SubgraphStroke: "#1e40af",      // blue-800
        SubgraphTitleColor: "#93c5fd",  // blue-300
        NodeCornerRadius: 5);

    // --- Mermaid-compatible skins: pixel-identical to mermaid.js ---

    static readonly FlowchartSkin Mermaid = new(
        Name: "mermaid",
        Background: "#ffffff",
        TextColor: "#333",
        NodeFill: "#ECECFF",
        NodeStroke: "#9370DB",
        EdgeStroke: "#333333",
        EdgeLabelBackground: "rgba(232,232,232,0.8)",
        SubgraphFill: "#ffffde",
        SubgraphStroke: "#aaaa33",
        SubgraphTitleColor: "#333",
        NodeCornerRadius: 0);

    static readonly FlowchartSkin MermaidDark = new(
        Name: "mermaid-dark",
        Background: "#0d1117",
        TextColor: "#c9d1d9",
        NodeFill: "#1f2937",
        NodeStroke: "#6e7681",
        EdgeStroke: "#9da7b3",
        EdgeLabelBackground: "rgba(31,41,55,0.9)",
        SubgraphFill: "rgba(17,24,39,0.4)",
        SubgraphStroke: "#4b5563",
        SubgraphTitleColor: "#c9d1d9",
        NodeCornerRadius: 0);

    static readonly FlowchartSkin Neutral = new(
        Name: "neutral",
        Background: "#ffffff",
        TextColor: "#1f2328",
        NodeFill: "#f4f5f7",
        NodeStroke: "#8c959f",
        EdgeStroke: "#57606a",
        EdgeLabelBackground: "rgba(244,245,247,0.9)",
        SubgraphFill: "rgba(246,248,250,0.5)",
        SubgraphStroke: "#8c959f",
        SubgraphTitleColor: "#1f2328",
        NodeCornerRadius: 3);

    static readonly FlowchartSkin Forest = new(
        Name: "forest",
        Background: "#ffffff",
        TextColor: "#1b4332",
        NodeFill: "#e9f5ec",
        NodeStroke: "#2d6a4f",
        EdgeStroke: "#2d6a4f",
        EdgeLabelBackground: "rgba(233,245,236,0.9)",
        SubgraphFill: "rgba(216,243,220,0.4)",
        SubgraphStroke: "#40916c",
        SubgraphTitleColor: "#1b4332",
        NodeCornerRadius: 3);
}
