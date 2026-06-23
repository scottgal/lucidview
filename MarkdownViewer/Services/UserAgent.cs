using System.Reflection;

namespace MarkdownViewer.Services;

internal static class UserAgent
{
    public static readonly string Value = BuildValue();

    private static string BuildValue()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        return $"lucidVIEW/{version} (Markdown Viewer)";
    }
}
