// Shim: exposes MarkdownViewer.AppPaths in the MarkdownViewer namespace so
// that the #if LAB blocks in lean source can reference it without modification.
// Forwards to MarkdownViewer.Lab.AppPaths.
namespace MarkdownViewer;

internal static class AppPaths
{
    public static string LocalState => MarkdownViewer.Lab.AppPaths.LocalState;
    public static string ModelCacheDir => MarkdownViewer.Lab.AppPaths.ModelCacheDir;
    public static string WorkspacesRoot => MarkdownViewer.Lab.AppPaths.WorkspacesRoot;
    public static string TelemetryDir => MarkdownViewer.Lab.AppPaths.TelemetryDir;
    public static string SettingsFilePath => Path.Combine(LocalState, "settings.json");
    public static string TemplateStorePath => Path.Combine(LocalState, "styloextract-templates.db");
}
