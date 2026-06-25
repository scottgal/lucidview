using MarkdownViewer.Models;
using StyloExtract.Abstractions;
using StyloExtract.Llm.LlamaSharp;
using StyloExtract.Playwright;

namespace MarkdownViewer.Services;

internal sealed record DoctorReport(
    string ModelPath,
    bool ModelPresent,
    long ModelSizeBytes,
    string BrowsersPath,
    bool BrowsersPresent,
    bool Ready);

internal static class ModelBootstrap
{
    public static DoctorReport Doctor()
    {
        var settings = AppSettingsFull.Load();
        // Use FullServices.ResolveModelPath for consistent resolution with --download-model verb.
        var modelPath = FullServices.ResolveModelPath(settings.LlmModelPath);
        var modelInfo = File.Exists(modelPath) ? new FileInfo(modelPath) : null;

        var browsersPath = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH")
                          ?? PlaywrightDefaultPath();
        var browsersPresent = Directory.Exists(browsersPath)
                              && Directory.EnumerateDirectories(browsersPath, "chromium*").Any();

        return new DoctorReport(
            ModelPath: modelPath,
            ModelPresent: modelInfo is not null,
            ModelSizeBytes: modelInfo?.Length ?? 0,
            BrowsersPath: browsersPath,
            BrowsersPresent: browsersPresent,
            Ready: modelInfo is not null && browsersPresent);
    }

    public static Task EnsureModelAsync(IProgress<double>? progress, CancellationToken ct)
    {
        // LlamaSharpTextProvider.EnsureLoaded() is sync (Task 6 deviation).
        // Wrap in Task.Run to avoid blocking the UI thread.
        var provider = (LlamaSharpTextProvider)FullServices.Get<ILlmTextProvider>();
        return Task.Run(() => provider.EnsureLoaded(), ct);
    }

    public static Task EnsureBrowsersAsync(IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Installing Chromium...");
        // PlaywrightInstaller is sync; offload to thread pool.
        return Task.Run(() => PlaywrightInstaller.EnsureBrowsersInstalled("chromium"), ct);
    }

    private static string PlaywrightDefaultPath()
    {
        // Microsoft.Playwright respects PLAYWRIGHT_BROWSERS_PATH (checked before calling this).
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ms-playwright");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "ms-playwright");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "ms-playwright");
    }
}
