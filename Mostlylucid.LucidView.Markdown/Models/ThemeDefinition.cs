namespace Mostlylucid.LucidView.Markdown.Models;

/// <summary>
/// Defines the color palette for a theme applied to the markdown viewer.
/// </summary>
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
    /// <summary>
    /// Foreground color that has sufficient contrast against the
    /// <see cref="Accent"/> background. White for dark accent colors,
    /// near-black for light accent colors (e.g. dark mode's #58a6ff).
    /// </summary>
    public string AccentForeground { get; set; } = "#ffffff";
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
    public string BrandLucid { get; set; } = "#DDDDDD";
    public string BrandVIEW { get; set; } = "#FFFFFF";
}
