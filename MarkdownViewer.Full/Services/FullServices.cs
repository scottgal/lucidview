using MarkdownViewer.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StyloExtract.Abstractions;
using StyloExtract.Core;
using StyloExtract.Llm.LlamaSharp;
using StyloExtract.Playwright;

namespace MarkdownViewer.Services;

internal static class FullServices
{
    private static readonly Lazy<IServiceProvider> _lazy = new(Build);

    public static IServiceProvider Provider => _lazy.Value;
    public static T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

    /// <summary>
    /// Resolves a HuggingFace-style ref ("owner/repo/filename.gguf") to an
    /// absolute path under ModelCacheDir. Returns null if the ref is already
    /// an absolute path that doesn't look like an HF reference.
    /// </summary>
    internal static string ResolveModelPath(string hfRefOrAbsPath)
    {
        if (Path.IsPathRooted(hfRefOrAbsPath))
            return hfRefOrAbsPath;

        var parts = hfRefOrAbsPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return Path.Combine(AppPaths.ModelCacheDir, hfRefOrAbsPath);

        var owner = parts[0];
        var repo = parts[1];
        var filename = parts[^1];
        return Path.Combine(AppPaths.ModelCacheDir, $"{owner}_{repo}_{filename}");
    }

    private static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

        services.AddStyloExtract(o =>
        {
            o.StorePath = AppPaths.TemplateStorePath;
            o.DefaultProfile = ExtractionProfile.RagFull;
        });

        // Task 5: register the Playwright rendered-DOM fetcher.
        services.AddSingleton<IRenderedHtmlFetcher>(_ => new PlaywrightHtmlFetcher());

        // Task 8: telemetry sink — must be registered before HtmlToMarkdownServiceFull.
        services.AddSingleton<ExtractionTelemetry>();

        services.AddSingleton<IHtmlToMarkdownService, HtmlToMarkdownServiceFull>();

        // Task 6: register LlamaSharp as ILlmTextProvider + wire template inducer.
        var settings = AppSettingsFull.Load();

        if (settings.LlmEnabled)
        {
            var resolvedModelPath = ResolveModelPath(settings.LlmModelPath);
            services.AddStyloExtractLlamaSharp(o =>
            {
                o.ModelPath = resolvedModelPath;
                // ContextSize is uint; clamp to 0 minimum.
                o.ContextSize = (uint)Math.Max(0, settings.LlmContextSize);
                o.Threads = settings.LlmThreads;
                // GpuLayerCount: -1 in settings means "let llama.cpp decide" (0 = CPU-only).
                o.GpuLayerCount = settings.LlmGpuLayerCount < 0 ? 0 : settings.LlmGpuLayerCount;
            });
            services.AddStyloExtractLlmInducer(
                Path.Combine(AppPaths.LocalState, "templates"));
        }

        var provider = services.BuildServiceProvider();

        // StyloExtract.Core registers TemplateEnrichmentCoordinator (BackgroundService)
        // as an IHostedService. In a standard .NET host, IHost.StartAsync() wakes them.
        // FULL uses StartWithClassicDesktopLifetime (no IHost), so we kick the
        // hosted services manually — otherwise the LLM template inducer queues work
        // but nothing ever processes it, and we get heuristic-only extraction even
        // with the LlamaSharp provider wired in.
        foreach (var hosted in provider.GetServices<IHostedService>())
            _ = hosted.StartAsync(CancellationToken.None);

        return provider;
    }
}
