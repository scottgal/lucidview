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
    string SubgraphTitleColor)
{
    public static FlowchartSkin Resolve(string? theme, ThemeColorOverrides? overrides = null)
    {
        var key = (theme ?? "default").Trim().ToLowerInvariant();
        var skin = key switch
        {
            "dark" => Dark,
            "forest" => Forest,
            "neutral" => Neutral,
            "base" => Default,
            _ => Default
        };

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

    // Mermaid.js default skin: light purple nodes, purple borders
    static readonly FlowchartSkin Default = new(
        Name: "default",
        Background: "#ffffff",
        TextColor: "#333",
        NodeFill: "#ECECFF",
        NodeStroke: "#9370DB",
        EdgeStroke: "#333333",
        EdgeLabelBackground: "rgba(232,232,232,0.8)",
        SubgraphFill: "#ffffde",
        SubgraphStroke: "#aaaa33",
        SubgraphTitleColor: "#333");

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
        SubgraphTitleColor: "#1f2328");

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
        SubgraphTitleColor: "#1b4332");

    static readonly FlowchartSkin Dark = new(
        Name: "dark",
        Background: "#0d1117",
        TextColor: "#c9d1d9",
        NodeFill: "#1f2937",
        NodeStroke: "#6e7681",
        EdgeStroke: "#9da7b3",
        EdgeLabelBackground: "rgba(31,41,55,0.9)",
        SubgraphFill: "rgba(17,24,39,0.4)",
        SubgraphStroke: "#4b5563",
        SubgraphTitleColor: "#c9d1d9");
}
