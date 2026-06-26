using System.IO.Hashing;
using System.Text;
using MarkdownViewer.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StyloExtract.Abstractions;
using StyloExtract.Core;
using StyloExtract.Llm.LlamaSharp;
using StyloExtract.Playwright;
using StyloExtract.Streaming;

namespace MarkdownViewer.Services;

internal static class FullServices
{
    private static readonly Lazy<IServiceProvider> _lazy = new(Build);
    private static FileSystemWatcher? _watcher;

    /// <summary>
    /// Hand-built streaming template id for mostlylucid.net pages. Seeded into the
    /// in-memory store at startup so the gateway scanner has something to match
    /// against the HttpClient byte stream on the dogfood path. Other hosts get a
    /// NoTemplate verdict — auto-induction of streaming templates is a follow-up.
    /// </summary>
    internal static readonly Guid MostlyLucidStreamingTemplateId =
        Guid.Parse("11111111-2222-3333-4444-555555555555");

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

        // alpha.16: streaming gateway scanner. Pairs with the existing extraction
        // pipeline — runs the byte stream through structural fences to emit a
        // verdict (Captured / Bailout / NoTemplate / Continue) BEFORE the full
        // LayoutExtractor materialises the DOM. lucidview wires it into
        // DownloadWebPageAsync to show the verdict in the status bar.
        // InMemoryStreamingTemplateStore is fine for the dogfood demo — durable
        // Sqlite variant is overkill until host-keyed auto-induction exists.
        services.AddSingleton<IStreamingTemplateStore, InMemoryStreamingTemplateStore>();
        services.AddSingleton<StreamingPathSelector>();

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

        // alpha.16: seed the hand-built mostlylucid streaming template into the
        // hot cache so the gateway scanner has something to match on first load.
        // Fire-and-forget — InMemoryStreamingTemplateStore.RegisterAsync is sync
        // under the hood so this completes essentially instantly.
        _ = SeedStreamingTemplatesAsync(provider);

        // StyloExtract.Core registers TemplateEnrichmentCoordinator (BackgroundService)
        // as an IHostedService. In a standard .NET host, IHost.StartAsync() wakes them.
        // FULL uses StartWithClassicDesktopLifetime (no IHost), so we kick the
        // hosted services manually — otherwise the LLM template inducer queues work
        // but nothing ever processes it, and we get heuristic-only extraction even
        // with the LlamaSharp provider wired in.
        foreach (var hosted in provider.GetServices<IHostedService>())
            _ = hosted.StartAsync(CancellationToken.None);

        // Pipeline-stage indicator: watch the templates dir for new YAMLs so the
        // status bar's "llm" segment can light up when either:
        //   - LLM background inducer writes <host>.yaml (alpha.9+)
        //   - Heuristic deterministic inducer writes <host>-deterministic.yaml (alpha.11+)
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
                var detail = isDeterministic ? $"{host} (det)" : host;
                telemetry.EmitStage(ExtractionStage.Llm, started: false, detail: detail);
            };
            watcher.Changed += hit;
            watcher.Created += hit;
            // Anchor the watcher so it isn't GC'd.
            _watcher = watcher;
        }
        catch { /* watcher is nice-to-have; don't break startup */ }

        return provider;
    }

    /// <summary>
    /// alpha.16: registers one hand-built streaming template covering generic
    /// mostlylucid-style markup (header/nav prefix, paragraph-pair content-start,
    /// footer/body content-end). Lifted from the streaming bench's TagEvents
    /// helper. Real-world auto-induction of host-keyed streaming templates is a
    /// follow-up — for the dogfood demo this single template is enough to prove
    /// the scanner runs against the real byte stream.
    /// </summary>
    private static async Task SeedStreamingTemplatesAsync(IServiceProvider sp)
    {
        try
        {
            var store = sp.GetRequiredService<IStreamingTemplateStore>();
            var template = new StreamingTemplate
            {
                TemplateId = MostlyLucidStreamingTemplateId,
                PrefixFence = TemplateFence.BuildFromEvents(
                    StreamingTagEvents("<header>", "</header>", "<nav>", "</nav>"),
                    requiredDepth: 0),
                ContentStartFence = TemplateFence.BuildFromEvents(
                    StreamingTagEvents("<p>", "</p>", "<p>", "</p>"),
                    requiredDepth: 0),
                ContentEndFence = TemplateFence.BuildFromEvents(
                    StreamingTagEvents("<footer>", "</footer>", "</body>", "</html>"),
                    requiredDepth: 0),
                MinContentDepth = 0,
                BailoutBytes = 5_000_000,
                MaxCaptureBytes = 5_000_000,
                WindowSize = 4,
                MaxEventsWithoutTransition = 256,
            };
            await store.RegisterAsync(template);
            Console.WriteLine($"[streaming] seeded template {MostlyLucidStreamingTemplateId} (mostlylucid generic-blog fences)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[streaming] seed failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Mirrors bench/StyloExtract.Streaming.Benchmarks/ExtractionComparisonBench.TagEvents:
    /// hashes "&lt;name&gt;" / "&lt;/name&gt;" tag strings into (tagHash, classHash) tuples
    /// suitable for TemplateFence.BuildFromEvents. Class hash stays 0 — the bench
    /// doesn't differentiate by class for these structural fences either.
    /// </summary>
    private static (ulong tagHash, ulong classHash)[] StreamingTagEvents(params string[] tags)
    {
        var result = new (ulong, ulong)[tags.Length];
        Span<byte> buf = stackalloc byte[64];
        for (int i = 0; i < tags.Length; i++)
        {
            var t = tags[i];
            var isClose = t.StartsWith("</", StringComparison.Ordinal);
            var nameStart = isClose ? 2 : 1;
            var nameEnd = t.IndexOf('>', nameStart);
            var name = t.AsSpan(nameStart, nameEnd - nameStart);
            var n = Encoding.UTF8.GetBytes(name, buf);
            result[i] = (XxHash3.HashToUInt64(buf[..n]), 0UL);
        }
        return result;
    }
}
