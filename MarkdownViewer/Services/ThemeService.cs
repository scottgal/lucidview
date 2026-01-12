using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

public class ThemeService
{
    private readonly Application _app;

    public ThemeService(Application app)
    {
        _app = app;
    }

    public void ApplyTheme(AppTheme theme)
    {
        var definition = ThemeColors.GetTheme(theme);

        // Set Avalonia theme variant
        _app.RequestedThemeVariant = theme == AppTheme.Light
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        // Apply custom colors as resources
        var resources = _app.Resources;

        SetColor(resources, "AppBackground", definition.Background);
        SetColor(resources, "AppBackgroundSecondary", definition.BackgroundSecondary);
        SetColor(resources, "AppBackgroundTertiary", definition.BackgroundTertiary);
        SetColor(resources, "AppSurface", definition.Surface);
        SetColor(resources, "AppSurfaceHover", definition.SurfaceHover);
        SetColor(resources, "AppBorder", definition.Border);
        SetColor(resources, "AppBorderSubtle", definition.BorderSubtle);
        SetColor(resources, "AppText", definition.Text);
        SetColor(resources, "AppTextSecondary", definition.TextSecondary);
        SetColor(resources, "AppTextMuted", definition.TextMuted);
        SetColor(resources, "AppAccent", definition.Accent);
        SetColor(resources, "AppAccentHover", definition.AccentHover);
        SetColor(resources, "AppLink", definition.Link);
        SetColor(resources, "AppSuccess", definition.Success);
        SetColor(resources, "AppWarning", definition.Warning);
        SetColor(resources, "AppError", definition.Error);
        SetColor(resources, "AppCodeBackground", definition.CodeBackground);
        SetColor(resources, "AppCodeBorder", definition.CodeBorder);
        SetColor(resources, "AppBlockquoteBorder", definition.BlockquoteBorder);
        SetColor(resources, "AppHeadingBorder", definition.HeadingBorder);
        SetColor(resources, "AppTableHeaderBg", definition.TableHeaderBg);
        SetColor(resources, "AppSelectionBg", definition.SelectionBg);
    }

    private static void SetColor(IResourceDictionary resources, string key, string hex)
    {
        if (Color.TryParse(hex, out var color))
        {
            resources[key] = new SolidColorBrush(color);
        }
    }
}
