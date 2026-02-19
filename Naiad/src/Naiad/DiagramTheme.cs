namespace MermaidSharp;

/// <summary>
/// Shared color theme for non-flowchart diagram renderers.
/// Provides light and dark palettes that each renderer can use
/// instead of hard-coding colors.
/// </summary>
public sealed record DiagramTheme(
    string Background,
    string TextColor,
    string MutedText,
    string PrimaryFill,
    string PrimaryStroke,
    string SecondaryFill,
    string SecondaryStroke,
    string TertiaryFill,
    string TertiaryStroke,
    string GridLine,
    string AxisLine,
    string LabelBackground)
{
    /// <summary>True when this is a dark theme.</summary>
    public bool IsDark => this == Dark;

    public static DiagramTheme Resolve(RenderOptions options)
    {
        var key = (options.Theme ?? "default").Trim().ToLowerInvariant();
        return key == "dark" ? Dark : Light;
    }

    public static readonly DiagramTheme Light = new(
        Background: "#ffffff",
        TextColor: "#1a192b",
        MutedText: "#666666",
        PrimaryFill: "#ECECFF",
        PrimaryStroke: "#9370DB",
        SecondaryFill: "#ffffde",
        SecondaryStroke: "#aaaa33",
        TertiaryFill: "#e8f4fd",
        TertiaryStroke: "#5b9bd5",
        GridLine: "#e0e0e0",
        AxisLine: "#333333",
        LabelBackground: "rgba(255,255,255,0.85)");

    public static readonly DiagramTheme Dark = new(
        Background: "#0d1117",
        TextColor: "#c9d1d9",
        MutedText: "#8b949e",
        PrimaryFill: "#1c2333",
        PrimaryStroke: "#7c6bbd",
        SecondaryFill: "#2a2520",
        SecondaryStroke: "#8a7e3b",
        TertiaryFill: "#162230",
        TertiaryStroke: "#4a8cc7",
        GridLine: "#21262d",
        AxisLine: "#c9d1d9",
        LabelBackground: "rgba(13,17,23,0.85)");

    /// <summary>
    /// Palette of distinct colors for charts, pie slices, etc.
    /// Returns colors appropriate for the theme.
    /// </summary>
    public string[] ChartPalette => IsDark
        ? ["#7c6bbd", "#4a8cc7", "#3b8a5e", "#8a7e3b", "#b05050",
           "#6b8fa3", "#9b6b9b", "#6b9b7b", "#a3856b", "#5b7b9b"]
        : ["#ECECFF", "#ffffde", "#e8f4fd", "#e8f5e9", "#fff3e0",
           "#fce4ec", "#f3e5f5", "#e0f2f1", "#fff8e1", "#e3f2fd"];

    /// <summary>
    /// Palette of distinct saturated colors for pie/sankey/journey charts.
    /// </summary>
    public string[] VividPalette => IsDark
        ? ["#7c6bbd", "#4a8cc7", "#3b8a5e", "#c9a33b", "#b05050",
           "#6b8fa3", "#9b6b9b", "#6b9b7b", "#c08050", "#5090b0"]
        : ["#9370DB", "#5b9bd5", "#4caf50", "#ff9800", "#e74c3c",
           "#2196f3", "#9c27b0", "#009688", "#ff5722", "#3f51b5"];
}
