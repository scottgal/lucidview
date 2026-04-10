using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

public class ThemeService
{
    private readonly Application _app;
    public AppTheme RequestedTheme { get; private set; } = AppTheme.Auto;
    public AppTheme CurrentEffectiveTheme { get; private set; } = AppTheme.Dark;

    /// <summary>
    /// User-defined custom theme loaded from settings.json. Used when
    /// <see cref="ApplyTheme"/> is called with <see cref="AppTheme.Custom"/>.
    /// </summary>
    public ThemeDefinition? CustomTheme { get; set; }

    public ThemeService(Application app)
    {
        _app = app;
    }

    public AppTheme ApplyTheme(AppTheme theme)
    {
        RequestedTheme = theme;

        if (theme == AppTheme.Auto)
        {
            // Follow the host/platform theme automatically.
            _app.RequestedThemeVariant = ThemeVariant.Default;
            ApplyThemeResources(ResolveSystemTheme());
            return CurrentEffectiveTheme;
        }

        if (theme == AppTheme.Custom && CustomTheme != null)
        {
            // Heuristic: detect light vs dark by background luminance.
            var isLight = TryParseColor(CustomTheme.Background, out var bg) && (bg.R + bg.G + bg.B) / 3 > 128;
            _app.RequestedThemeVariant = isLight ? ThemeVariant.Light : ThemeVariant.Dark;
            ApplyDefinition(CustomTheme, isLight ? AppTheme.Light : AppTheme.Dark);
            CurrentEffectiveTheme = AppTheme.Custom;
            return AppTheme.Custom;
        }

        _app.RequestedThemeVariant = IsLightTheme(theme) ? ThemeVariant.Light : ThemeVariant.Dark;
        ApplyThemeResources(theme);
        return CurrentEffectiveTheme;
    }

    private static bool TryParseColor(string hex, out Color color) => Color.TryParse(hex, out color);

    public AppTheme RefreshAutoTheme()
    {
        if (RequestedTheme != AppTheme.Auto)
            return CurrentEffectiveTheme;

        ApplyThemeResources(ResolveSystemTheme());
        return CurrentEffectiveTheme;
    }

    private static void SetColor(IResourceDictionary resources, string key, string hex)
    {
        if (Color.TryParse(hex, out var color)) resources[key] = new SolidColorBrush(color);
    }

    private static void SetColorValue(IResourceDictionary resources, string key, string hex)
    {
        if (Color.TryParse(hex, out var color)) resources[key] = color;
    }

    private static bool IsLightTheme(AppTheme theme) =>
        theme == AppTheme.Light
        || theme == AppTheme.MostlyLucidLight
        || theme == AppTheme.Pride; // Pride uses a near-white background, must apply Light variant

    private AppTheme ResolveSystemTheme()
    {
        var variant = _app.ActualThemeVariant;
        return variant == ThemeVariant.Light ? AppTheme.Light : AppTheme.Dark;
    }

    private void ApplyThemeResources(AppTheme effectiveTheme)
    {
        var definition = ThemeColors.GetTheme(effectiveTheme);
        ApplyDefinition(definition, effectiveTheme);
    }

    private void ApplyDefinition(ThemeDefinition definition, AppTheme effectiveTheme)
    {
        CurrentEffectiveTheme = effectiveTheme;

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
        SetColor(resources, "BrandLucid", definition.BrandLucid);
        SetColor(resources, "BrandVIEW", definition.BrandVIEW);

        // LiveMarkdown theme resources (Color values)
        SetColorValue(resources, "ForegroundColor", definition.Text);
        SetColorValue(resources, "BorderColor", definition.Border);
        SetColorValue(resources, "CardBackgroundColor", definition.TableHeaderBg);
        SetColorValue(resources, "SecondaryCardBackgroundColor", definition.CodeBackground);
        SetColorValue(resources, "CodeInlineColor", definition.Text);
        SetColorValue(resources, "QuoteBorderColor", definition.BlockquoteBorder);
    }
}
