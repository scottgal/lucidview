using Avalonia;
using Avalonia.Data.Core.Plugins;
using MarkdownViewer.Models;
using MarkdownViewer.Services;
using StyloExtract.Abstractions;
#if DEBUG
using Mostlylucid.Avalonia.UITesting;
#endif

namespace MarkdownViewer;

internal static class FullProgram
{
    private static readonly string CrashLogPath = Path.Combine(AppPaths.LocalState, "crash.log");

    [STAThread]
    public static async Task<int> Main(string[] args)
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

        // CLI verbs.
        if (args.Contains("--install-browsers"))
        {
            Console.WriteLine("Installing Playwright Chromium...");
            StyloExtract.Playwright.PlaywrightInstaller.EnsureBrowsersInstalled("chromium");
            Console.WriteLine("Done.");
            return 0;
        }

        if (args.Length > 0 && args[0] == "--download-model")
        {
            var hfId = args.Length > 1
                ? args[1]
                : AppSettingsFull.Load().LlmModelPath;

            Console.WriteLine($"Pre-downloading model: {hfId}");
            Console.WriteLine($"Cache dir: {AppPaths.ModelCacheDir}");

            var resolvedPath = FullServices.ResolveModelPath(hfId);

            if (!File.Exists(resolvedPath))
            {
                await DownloadModelAsync(hfId, resolvedPath, CancellationToken.None);
            }
            else
            {
                Console.WriteLine($"Model already cached at: {resolvedPath}");
            }

            // Verify by loading weights via the provider.
            var provider = (StyloExtract.Llm.LlamaSharp.LlamaSharpTextProvider)
                FullServices.Get<ILlmTextProvider>();
            provider.EnsureLoaded();
            Console.WriteLine("Model ready.");
            return 0;
        }

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

    /// <summary>
    /// Downloads a HuggingFace model file to <paramref name="destPath"/>.
    /// <paramref name="hfRef"/> is "owner/repo/filename.gguf".
    /// Shows byte-level progress to the console.
    /// </summary>
    private static async Task DownloadModelAsync(string hfRef, string destPath, CancellationToken ct)
    {
        var parts = hfRef.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            throw new ArgumentException(
                $"Invalid HuggingFace reference '{hfRef}'. Expected 'owner/repo/filename.gguf'.", nameof(hfRef));

        var owner = parts[0];
        var repo = parts[1];
        var filename = string.Join("/", parts[2..]);
        var hfUrl = $"https://huggingface.co/{owner}/{repo}/resolve/main/{filename}";

        Console.WriteLine($"Downloading from: {hfUrl}");

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        var tempPath = destPath + ".tmp";

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromHours(2);
        // Follow HF redirects and report progress.
        using var response = await httpClient.GetAsync(hfUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        try
        {
            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            var lastReport = DateTime.UtcNow;

            while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;

                if ((DateTime.UtcNow - lastReport).TotalSeconds >= 5)
                {
                    if (totalBytes.HasValue)
                        Console.WriteLine($"  {downloaded / 1_048_576} MB / {totalBytes.Value / 1_048_576} MB");
                    else
                        Console.WriteLine($"  {downloaded / 1_048_576} MB downloaded...");
                    lastReport = DateTime.UtcNow;
                }
            }

            await fileStream.FlushAsync(ct);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            throw;
        }

        File.Move(tempPath, destPath, overwrite: true);
        Console.WriteLine($"Saved to: {destPath}");
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
