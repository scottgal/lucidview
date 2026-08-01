using Avalonia;
using Avalonia.Media;

namespace MarkdownViewer.Controls.Editor;

/// <summary>
/// Provides theme-aware brushes pulled from the application's dynamic resources.
/// Use these instead of hardcoded colors so the editor responds to theme changes.
/// </summary>
public static class EditorTheme
{
    public static IBrush Text
        => TryGetBrush("AppText") ?? Brushes.White;

    public static IBrush TextSecondary
        => TryGetBrush("AppTextSecondary") ?? new SolidColorBrush(Color.FromArgb(180, 180, 180, 190));

    public static IBrush Background
        => TryGetBrush("AppBackground") ?? new SolidColorBrush(Color.FromArgb(255, 13, 17, 23));

    public static IBrush BackgroundSecondary
        => TryGetBrush("AppBackgroundSecondary") ?? new SolidColorBrush(Color.FromArgb(255, 22, 27, 34));

    public static IBrush Surface
        => TryGetBrush("AppSurface") ?? new SolidColorBrush(Color.FromArgb(255, 30, 35, 45));

    public static IBrush BorderSubtle
        => TryGetBrush("AppBorderSubtle") ?? new SolidColorBrush(Color.FromArgb(60, 120, 120, 140));

    public static IBrush Accent
        => TryGetBrush("AppAccent") ?? new SolidColorBrush(Color.FromArgb(255, 124, 58, 237));

    public static IBrush CardHover
        => TryGetBrush("AppSurfaceHover") ?? new SolidColorBrush(Color.FromArgb(20, 120, 120, 140));

    public static IBrush CardActive
        => new SolidColorBrush(Color.FromArgb(40, 90, 130, 200));

    private static IBrush? TryGetBrush(string key)
    {
        if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true
            && resource is IBrush brush)
            return brush;
        return null;
    }
}
