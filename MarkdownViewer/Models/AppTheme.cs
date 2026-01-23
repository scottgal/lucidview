namespace MarkdownViewer.Models;

public enum AppTheme
{
    Light,
    Dark,
    VSCode,
    GitHub,
    MostlyLucidDark,
    MostlyLucidLight
}

public enum CodeTheme
{
    Auto, // Matches app theme
    OneDark,
    OneLight,
    GitHubDark,
    GitHubLight,
    VSCodeDark,
    VSCodeLight,
    MonokaiPro,
    Dracula,
    NordDark,
    SolarizedDark,
    SolarizedLight
}

public static class ThemeColors
{
    public static readonly ThemeDefinition Light = new()
    {
        Name = "Light",
        Background = "#ffffff",
        BackgroundSecondary = "#f6f8fa",
        BackgroundTertiary = "#f0f2f5",
        Surface = "#ffffff",
        SurfaceHover = "#f3f4f6",
        Border = "#d0d7de",
        BorderSubtle = "#e5e7eb",
        Text = "#1f2328",
        TextSecondary = "#656d76",
        TextMuted = "#8b949e",
        Accent = "#0969da",
        AccentHover = "#0860ca",
        Link = "#0969da",
        Success = "#1a7f37",
        Warning = "#9a6700",
        Error = "#cf222e",
        CodeBackground = "#f6f8fa",
        CodeBorder = "#d0d7de",
        BlockquoteBorder = "#0969da",
        HeadingBorder = "#d0d7de",
        TableHeaderBg = "#f6f8fa",
        SelectionBg = "#0969da20",
        BrandLucid = "#555555", // Darker gray for light theme
        BrandVIEW = "#1a1a1a" // Black for light theme
    };

    public static readonly ThemeDefinition Dark = new()
    {
        Name = "Dark",
        Background = "#0d1117",
        BackgroundSecondary = "#161b22",
        BackgroundTertiary = "#1c2128",
        Surface = "#161b22",
        SurfaceHover = "#1f242b",
        Border = "#30363d",
        BorderSubtle = "#21262d",
        Text = "#e6edf3",
        TextSecondary = "#8b949e",
        TextMuted = "#6e7681",
        Accent = "#58a6ff",
        AccentHover = "#79b8ff",
        Link = "#58a6ff",
        Success = "#3fb950",
        Warning = "#d29922",
        Error = "#f85149",
        CodeBackground = "#161b22",
        CodeBorder = "#30363d",
        BlockquoteBorder = "#58a6ff",
        HeadingBorder = "#21262d",
        TableHeaderBg = "#161b22",
        SelectionBg = "#58a6ff30",
        BrandLucid = "#DDDDDD", // Gray for dark theme
        BrandVIEW = "#FFFFFF" // White for dark theme
    };

    public static readonly ThemeDefinition VSCode = new()
    {
        Name = "VS Code",
        Background = "#1e1e1e",
        BackgroundSecondary = "#252526",
        BackgroundTertiary = "#2d2d30",
        Surface = "#252526",
        SurfaceHover = "#2a2d2e",
        Border = "#3c3c3c",
        BorderSubtle = "#333333",
        Text = "#cccccc",
        TextSecondary = "#9d9d9d",
        TextMuted = "#6e7681",
        Accent = "#0078d4",
        AccentHover = "#1484d7",
        Link = "#3794ff",
        Success = "#4ec9b0",
        Warning = "#dcdcaa",
        Error = "#f14c4c",
        CodeBackground = "#1e1e1e",
        CodeBorder = "#3c3c3c",
        BlockquoteBorder = "#0078d4",
        HeadingBorder = "#3c3c3c",
        TableHeaderBg = "#2d2d30",
        SelectionBg = "#264f78",
        BrandLucid = "#DDDDDD", // Gray for dark theme
        BrandVIEW = "#FFFFFF" // White for dark theme
    };

    public static readonly ThemeDefinition GitHub = new()
    {
        Name = "GitHub",
        Background = "#0d1117",
        BackgroundSecondary = "#161b22",
        BackgroundTertiary = "#21262d",
        Surface = "#161b22",
        SurfaceHover = "#21262d",
        Border = "#30363d",
        BorderSubtle = "#21262d",
        Text = "#c9d1d9",
        TextSecondary = "#8b949e",
        TextMuted = "#6e7681",
        Accent = "#f78166",
        AccentHover = "#ffa198",
        Link = "#58a6ff",
        Success = "#3fb950",
        Warning = "#d29922",
        Error = "#f85149",
        CodeBackground = "#161b22",
        CodeBorder = "#30363d",
        BlockquoteBorder = "#f78166",
        HeadingBorder = "#21262d",
        TableHeaderBg = "#161b22",
        SelectionBg = "#388bfd26",
        BrandLucid = "#DDDDDD", // Gray for dark theme
        BrandVIEW = "#FFFFFF" // White for dark theme
    };

    /// <summary>
    /// mostlylucid dark theme - matches mostlylucid.net dark mode
    /// Deep blue-black with purple accents
    /// </summary>
    public static readonly ThemeDefinition MostlyLucidDark = new()
    {
        Name = "mostlylucid dark",
        Background = "#0f0f23",          // Deep dark blue-black
        BackgroundSecondary = "#1a1a2e", // Slightly lighter
        BackgroundTertiary = "#16213e",  // Card/panel background
        Surface = "#1a1a2e",
        SurfaceHover = "#1f1f3a",
        Border = "#2d2d5a",              // Subtle purple-blue border
        BorderSubtle = "#252550",
        Text = "#e4e4f0",                // Slightly warm white
        TextSecondary = "#a0a0c0",       // Muted lavender
        TextMuted = "#6c6c8a",
        Accent = "#7c3aed",              // Vibrant purple (primary brand)
        AccentHover = "#8b5cf6",         // Lighter purple on hover
        Link = "#60a5fa",                // Blue links
        Success = "#34d399",             // Emerald green
        Warning = "#fbbf24",             // Amber
        Error = "#f87171",               // Red
        CodeBackground = "#1e1e3f",      // Dark purple code background
        CodeBorder = "#3d3d6b",
        BlockquoteBorder = "#7c3aed",    // Purple accent for quotes
        HeadingBorder = "#2d2d5a",
        TableHeaderBg = "#1a1a2e",
        SelectionBg = "#7c3aed40",       // Purple selection
        BrandLucid = "#a78bfa",          // Light purple for "lucid"
        BrandVIEW = "#ffffff"            // White for "VIEW"
    };

    /// <summary>
    /// mostlylucid light theme - matches mostlylucid.net light mode
    /// Clean white with purple accents
    /// </summary>
    public static readonly ThemeDefinition MostlyLucidLight = new()
    {
        Name = "mostlylucid light",
        Background = "#ffffff",          // Clean white
        BackgroundSecondary = "#f8f7ff", // Very subtle purple tint
        BackgroundTertiary = "#f0eeff",  // Light lavender
        Surface = "#ffffff",
        SurfaceHover = "#f5f3ff",        // Light purple hover
        Border = "#e0dcf5",              // Soft purple border
        BorderSubtle = "#ebe8f7",
        Text = "#1a1625",                // Dark purple-black
        TextSecondary = "#4a4560",       // Muted purple-gray
        TextMuted = "#7a7590",
        Accent = "#7c3aed",              // Vibrant purple (primary brand)
        AccentHover = "#6d28d9",         // Darker purple on hover
        Link = "#2563eb",                // Blue links
        Success = "#059669",             // Emerald green
        Warning = "#d97706",             // Amber
        Error = "#dc2626",               // Red
        CodeBackground = "#f5f3ff",      // Very light purple code background
        CodeBorder = "#e0dcf5",
        BlockquoteBorder = "#7c3aed",    // Purple accent for quotes
        HeadingBorder = "#e0dcf5",
        TableHeaderBg = "#f8f7ff",
        SelectionBg = "#7c3aed20",       // Light purple selection
        BrandLucid = "#6d28d9",          // Dark purple for "lucid"
        BrandVIEW = "#1a1625"            // Dark for "VIEW"
    };

    public static ThemeDefinition GetTheme(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Light => Light,
            AppTheme.Dark => Dark,
            AppTheme.VSCode => VSCode,
            AppTheme.GitHub => GitHub,
            AppTheme.MostlyLucidDark => MostlyLucidDark,
            AppTheme.MostlyLucidLight => MostlyLucidLight,
            _ => Dark
        };
    }
}

public class ThemeDefinition
{
    public string Name { get; set; } = "";
    public string Background { get; set; } = "";
    public string BackgroundSecondary { get; set; } = "";
    public string BackgroundTertiary { get; set; } = "";
    public string Surface { get; set; } = "";
    public string SurfaceHover { get; set; } = "";
    public string Border { get; set; } = "";
    public string BorderSubtle { get; set; } = "";
    public string Text { get; set; } = "";
    public string TextSecondary { get; set; } = "";
    public string TextMuted { get; set; } = "";
    public string Accent { get; set; } = "";
    public string AccentHover { get; set; } = "";
    public string Link { get; set; } = "";
    public string Success { get; set; } = "";
    public string Warning { get; set; } = "";
    public string Error { get; set; } = "";
    public string CodeBackground { get; set; } = "";
    public string CodeBorder { get; set; } = "";
    public string BlockquoteBorder { get; set; } = "";
    public string HeadingBorder { get; set; } = "";
    public string TableHeaderBg { get; set; } = "";

    public string SelectionBg { get; set; } = "";

    // Brand colors for lucidVIEW logo
    public string BrandLucid { get; set; } = "#DDDDDD"; // "lucid" - gray
    public string BrandVIEW { get; set; } = "#FFFFFF"; // "VIEW" - white (black on light)
}