using Avalonia;
using Avalonia.Data.Core.Plugins;
using System;
#if DEBUG
using Mostlylucid.Avalonia.UITesting;
#endif

namespace MarkdownViewer.Lab;

internal static class LabProgram
{
    [STAThread]
    public static int Main(string[] args)
    {
        // CLI verbs (--shot, --doctor, etc.) handled in Task 21 will dispatch
        // here before AppMain.
        return AppMain(args);
    }

    private static int AppMain(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        BindingPlugins.DataValidators.Clear();

        var builder = AppBuilder.Configure<MarkdownViewer.App>()
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

        return builder.AfterSetup(_ =>
        {
            BindingPlugins.DataValidators.Clear();
        });
    }
}
