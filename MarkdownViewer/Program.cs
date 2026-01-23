using Avalonia;
using Avalonia.Data.Core.Plugins;

namespace MarkdownViewer;

internal class Program
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MarkdownViewer",
        "crash.log"
    );

    [STAThread]
    public static void Main(string[] args)
    {
        // Set up global exception handlers for crash diagnostics
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (TypeInitializationException ex) when (IsSkiaVersionMismatch(ex))
        {
            LogCrash("Main", ex);
            Console.Error.WriteLine();
            Console.Error.WriteLine("=== SkiaSharp Native Library Mismatch ===");
            Console.Error.WriteLine("The system has an incompatible version of libSkiaSharp installed.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("To fix this issue, try one of:");
            Console.Error.WriteLine("  1. Remove system libSkiaSharp: sudo apt remove libskiasharp (Debian/Ubuntu)");
            Console.Error.WriteLine("  2. Set library path: LD_LIBRARY_PATH=\"\" ./lucidVIEW");
            Console.Error.WriteLine("  3. Use the self-contained published version");
            Console.Error.WriteLine();
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            LogCrash("Main", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        // Disable ALL data validators to avoid IBinding errors from Markdown.Avalonia's StaticBinding
        BindingPlugins.DataValidators.Clear();

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .AfterSetup(builder =>
            {
                // Additional safety: clear validators again after setup
                BindingPlugins.DataValidators.Clear();
            });
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash("UnhandledException", ex);
        }
        else
        {
            LogCrash("UnhandledException", new Exception($"Non-exception object: {e.ExceptionObject}"));
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("UnobservedTaskException", e.Exception);
        // Mark as observed to prevent crash on task exceptions
        e.SetObserved();
    }

    private static bool IsSkiaVersionMismatch(Exception ex)
    {
        // Check if this is the SkiaSharp native library version mismatch error
        var current = ex;
        while (current != null)
        {
            if (current.Message.Contains("libSkiaSharp", StringComparison.OrdinalIgnoreCase) &&
                current.Message.Contains("incompatible", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            current = current.InnerException;
        }
        return false;
    }

    private static void LogCrash(string source, Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = $"""
                ============================================================
                CRASH LOG - {timestamp}
                Source: {source}
                ============================================================
                Exception Type: {ex.GetType().FullName}
                Message: {ex.Message}

                Stack Trace:
                {ex.StackTrace}

                Inner Exception:
                {(ex.InnerException != null ? $"{ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}" : "None")}

                Environment:
                - OS: {Environment.OSVersion}
                - CLR: {Environment.Version}
                - Is64Bit: {Environment.Is64BitProcess}
                - CommandLine: {Environment.CommandLine}
                ============================================================


                """;

            File.AppendAllText(CrashLogPath, logEntry);
        }
        catch
        {
            // Can't log the crash - nothing more we can do
        }
    }
}