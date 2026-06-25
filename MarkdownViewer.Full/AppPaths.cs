using System.Runtime.InteropServices;

namespace MarkdownViewer;

internal static class AppPaths
{
    private const string AppFolder = "lucidVIEW-FULL";
    private const string XdgFolder = "lucidview-full";

    public static string LocalState { get; } = EnsureDir(ResolveLocalState());
    public static string ModelCacheDir { get; } = EnsureDir(
        Environment.GetEnvironmentVariable("LUCIDVIEW_MODEL_CACHE")
        ?? Path.Combine(LocalState, "models"));
    public static string TemplateStorePath { get; } = Path.Combine(LocalState, "styloextract-templates.db");
    public static string SettingsFilePath { get; } = Path.Combine(LocalState, "settings.json");

    private static string ResolveLocalState()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolder);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", AppFolder);

        var xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var baseDir = string.IsNullOrEmpty(xdgState)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "state")
            : xdgState;
        return Path.Combine(baseDir, XdgFolder);
    }

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
