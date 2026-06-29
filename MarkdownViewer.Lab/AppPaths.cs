using System;
using System.IO;

namespace MarkdownViewer.Lab;

public static class AppPaths
{
    public static string LocalState { get; } = ResolveLocalState();
    public static string WorkspacesRoot { get; } = Path.Combine(LocalState, "workspaces");
    public static string ModelCacheDir { get; } = Path.Combine(LocalState, "models");
    public static string TelemetryDir { get; } = Path.Combine(LocalState, "telemetry");

    private static string ResolveLocalState()
    {
        string baseDir;
        if (OperatingSystem.IsWindows())
        {
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        else if (OperatingSystem.IsMacOS())
        {
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                    "Library", "Application Support");
        }
        else
        {
            baseDir = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                       ".local", "share");
        }

        var path = Path.Combine(baseDir, "lucidLAB");
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, "workspaces"));
        Directory.CreateDirectory(Path.Combine(path, "models"));
        Directory.CreateDirectory(Path.Combine(path, "telemetry"));
        return path;
    }
}
