using Avalonia;
using Avalonia.Data.Core.Plugins;
#if DEBUG
using Mostlylucid.Avalonia.UITesting;
#endif

namespace MarkdownViewer;

internal static class FullProgram
{
    private static readonly string CrashLogPath = Path.Combine(AppPaths.LocalState, "crash.log");

    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) LogCrash("UnhandledException", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        // CLI verbs land here in later tasks (--download-model, --install-browsers,
        // --doctor). For now no verbs — start the UI.

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            LogCrash("Main", ex);
            throw;
        }
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        BindingPlugins.DataValidators.Clear();

        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

#if DEBUG
        builder = builder.UseUITesting(opts =>
        {
            opts.DefaultScreenshotDir = "ux-screenshots";
            opts.Log = Console.WriteLine;
            opts.EnableCrossWindowTracking = true;
            opts.CaptureScreenshotsByDefault = false;
        });
#endif

        return builder.AfterSetup(_ => BindingPlugins.DataValidators.Clear());
    }

    private static void LogCrash(string source, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:O}] {source}: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\n\n");
        }
        catch { }
    }
}
