using MarkdownViewer.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StyloExtract.Abstractions;
using StyloExtract.Abstractions.TemplateEnrichment;
using StyloExtract.Core;
using StyloExtract.Llm.LlamaSharp;
using StyloExtract.Playwright;
using StyloExtract.Streaming;

namespace MarkdownViewer.Services;

internal static class FullServices
{
    private static readonly Lazy<IServiceProvider> _lazy = new(Build);
    private static FileSystemWatcher? _watcher;

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

        // Wires the operator-template YAML store + the deterministic-template
        // YAML sink (alpha.11+). LayoutExtractor picks up the sink as an
        // optional dep and writes <host>-deterministic.yaml after each
        // heuristic induction so the templates dir gets a YAML for every
        // induced extractor, not just LLM-induced ones.
        services.AddStyloExtractOperatorTemplates(
            Path.Combine(AppPaths.LocalState, "templates"));

        // Task 5: register the Playwright rendered-DOM fetcher.
        services.AddSingleton<IRenderedHtmlFetcher>(_ => new PlaywrightHtmlFetcher());

        // alpha.17: streaming gateway scanner with host-keyed templates +
        // auto-induction. Pairs with the existing extraction pipeline — runs
        // the byte stream through structural fences to emit a verdict
        // (Captured / Bailout / NoTemplate / Continue) BEFORE the full
        // LayoutExtractor materialises the DOM. lucidview wires it into
        // DownloadWebPageAsync to show the verdict in the status bar and to
        // upsert an induced template back to the store on NoTemplate.
        //
        // SqliteStreamingTemplateStore persists across runs so the dogfood
        // cycle works in two separate `dotnet run` invocations:
        //   visit 1: NoTemplate -> induce + upsert -> persisted to disk
        //   visit 2: ScanByHost hot-cache miss -> WarmByHostAsync hits sqlite
        //            -> real Captured / Bailout verdict
        //
        // alpha.18: refit + versioning. RefitTelemetrySink surfaces refit
        // events via the existing ExtractionTelemetry "llm" status-bar segment
        // so the user sees "<host> (refit v2)" when drift triggers a refit.
        // Registered BEFORE AddStyloExtractStreaming so the consumer sink
        // wins over the extension's TryAddSingleton noop default.
        services.AddSingleton<IStreamingTemplateVersionSink, RefitTelemetrySink>();
        services.AddStyloExtractStreaming(o =>
            o.SqlitePath = Path.Combine(AppPaths.LocalState, "streaming-templates.db"));

        // Task 8: telemetry sink — must be registered before HtmlToMarkdownServiceFull.
        services.AddSingleton<ExtractionTelemetry>();

        // Lights up the status-bar Llm segment while a CPU-only inference is in
        // flight so a 30-60 s qwen3.5:4b call doesn't look like the app froze.
        services.AddSingleton<ILlmActivityObserver, StatusBarLlmActivityObserver>();

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

        // Pipeline-stage indicator: watch the templates dir for new YAMLs so the
        // status bar lights up the correct inducer segment when one fires:
        //   - Heuristic deterministic inducer writes <host>-deterministic.yaml → "induce"
        //   - LLM background inducer writes <host>.yaml (LlmEnabled path)       → "llm"
        // Both fire independent of LlmEnabled because deterministic YAML lands
        // even when LLM is off.
        try
        {
            var templatesDir = Path.Combine(AppPaths.LocalState, "templates");
            Directory.CreateDirectory(templatesDir);
            var telemetry = provider.GetRequiredService<ExtractionTelemetry>();
            var watcher = new FileSystemWatcher(templatesDir, "*.yaml")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler hit = (_, e) =>
            {
                var name = Path.GetFileNameWithoutExtension(e.Name) ?? "";
                var isDeterministic = name.EndsWith("-deterministic", StringComparison.Ordinal);
                var host = isDeterministic ? name[..^"-deterministic".Length] : name;
                var stage = isDeterministic ? ExtractionStage.Induce : ExtractionStage.Llm;
                telemetry.EmitStage(stage, started: false, detail: host);
            };
            watcher.Changed += hit;
            watcher.Created += hit;
            // Anchor the watcher so it isn't GC'd.
            _watcher = watcher;
        }
        catch { /* watcher is nice-to-have; don't break startup */ }

        return provider;
    }

}
