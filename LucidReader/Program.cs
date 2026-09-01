using Avalonia;
using Avalonia.Data.Core.Plugins;
#if DEBUG
using Avalonia.Headless;
using Mostlylucid.Avalonia.UITesting;
#endif

namespace LucidReader;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Handled before Avalonia is touched, so the check runs on a machine
        // with no display and cannot be affected by anything the UI does.
        //
        // This is the one piece of test tooling that ships in Release rather
        // than living behind #if DEBUG, and SmokeTest's own summary records
        // why: a Debug-only check cannot answer the question that mattered,
        // which is whether the packaged binary works. See the packaging
        // scripts, which refuse to produce an artifact this exits non-zero on.
        if (args.Contains("--smoke-test"))
            return SmokeTest.RunAsync().GetAwaiter().GetResult();

        return BuildAvaloniaApp(args).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp([]);

    public static AppBuilder BuildAvaloniaApp(string[] args)
    {
        BindingPlugins.DataValidators.Clear();

        var builder = AppBuilder.Configure<App>();

#if DEBUG
        // --ux-headless renders into a bitmap instead of an OS window, so a
        // verification run does not put a window on screen or take keyboard
        // focus. Driving the app on the native platform steals focus on every
        // launch, and a batch of scripts makes the machine unusable while it
        // runs. The harness's own capture path already settles a headless
        // frame before shooting it (HeadlessRender.SettleAsync), so
        // screenshots still reflect what renders.
        //
        // UseHeadlessDrawing stays false on purpose: true skips real drawing
        // and every screenshot comes back blank, which is worse than useless
        // because a script would still pass.
        builder = args.Contains("--ux-headless")
            ? builder.UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            }).UseSkia()
            : builder.UsePlatformDetect();
#else
        builder = builder.UsePlatformDetect();
#endif

        builder = builder.LogToTrace();

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
}
