namespace MarkdownViewer.Models;

public enum AppTheme
{
    Light,
    Dark,
    VSCode,
    GitHub
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

    public static ThemeDefinition GetTheme(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Light => Light,
            AppTheme.Dark => Dark,
            AppTheme.VSCode => VSCode,
            AppTheme.GitHub => GitHub,
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