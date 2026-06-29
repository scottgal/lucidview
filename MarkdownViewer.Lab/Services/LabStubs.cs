// Stub implementations for the #if LAB join points in lean source.
// These provide the API surface needed for compilation; real implementations
// are delivered in Tasks 4-6 of the lucidLAB plan.
// Note: LabServices.ResolveModelPath contains real path-resolution logic
// (lifted from the deleted FULL edition), not a stub.
using Microsoft.Extensions.DependencyInjection;

namespace MarkdownViewer.Services;

// ---------------------------------------------------------------------------
// ExtractionTelemetry stubs
// ---------------------------------------------------------------------------

public sealed record LastExtractionInfo(
    DateTime When,
    Uri? Source,
    string MatchStatus,
    Guid TemplateId,
    int TemplateVersion,
    string Fetcher,
    TimeSpan TotalDuration,
    bool LlmInductionFired,
    TimeSpan LlmDuration,
    int BlockCount,
    int OutputCharacterCount);

public enum ExtractionStage
{
    Idle,
    Fetch,
    Stream,
    Match,
    Induce,
    Llm,
    Render,
}

public sealed class StageEvent
{
    public ExtractionStage Stage { get; init; }
    public bool Started { get; init; }
    public string? Detail { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed class ExtractionTelemetry
{
    public event Action<StageEvent>? StageChanged;

#pragma warning disable CA1822 // These are intentional instance stubs; Tasks 4-6 wire real backing fields.
    public LastExtractionInfo? Last => null;
    public IReadOnlyList<LastExtractionInfo> Recent => [];
#pragma warning restore CA1822

    /// <summary>
    /// Emits a stage-start or stage-complete event to the StageChanged subscribers.
    /// </summary>
    public void EmitStage(
        ExtractionStage stage,
        bool started,
        string? detail = null,
        TimeSpan duration = default)
    {
        StageChanged?.Invoke(new StageEvent
        {
            Stage = stage,
            Started = started,
            Detail = detail,
            Duration = duration
        });
    }
}

// ---------------------------------------------------------------------------
// LabServices stub — empty DI container; real wiring in Task 5
// ---------------------------------------------------------------------------

internal static class LabServices
{
    private static readonly Lazy<IServiceProvider> _lazy = new(static () =>
        new ServiceCollection().BuildServiceProvider());

    public static IServiceProvider Provider => _lazy.Value;
    public static T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

    internal static string ResolveModelPath(string hfRefOrAbsPath)
    {
        if (Path.IsPathRooted(hfRefOrAbsPath))
            return hfRefOrAbsPath;

        var cacheDir = MarkdownViewer.Lab.AppPaths.ModelCacheDir;
        var parts = hfRefOrAbsPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return Path.Combine(cacheDir, hfRefOrAbsPath);

        var owner = parts[0];
        var repo = parts[1];
        var filename = parts[^1];
        return Path.Combine(cacheDir, $"{owner}_{repo}_{filename}");
    }
}

// ---------------------------------------------------------------------------
// ModelBootstrap stub — no-op until Task 6
// ---------------------------------------------------------------------------

internal sealed record DoctorReport(
    string ModelPath,
    bool ModelPresent,
    long ModelSizeBytes,
    string BrowsersPath,
    bool BrowsersPresent,
    bool Ready);

internal static class ModelBootstrap
{
    public static DoctorReport Doctor() => new(
        ModelPath: Path.Combine(MarkdownViewer.Lab.AppPaths.ModelCacheDir, "model.gguf"),
        ModelPresent: false,
        ModelSizeBytes: 0,
        BrowsersPath: Path.Combine(MarkdownViewer.Lab.AppPaths.LocalState, "browsers"),
        BrowsersPresent: false,
        Ready: false);

    public static Task EnsureModelAsync(IProgress<double>? progress, CancellationToken ct) =>
        Task.CompletedTask;

    public static Task EnsureBrowsersAsync(IProgress<string>? progress, CancellationToken ct) =>
        Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// HtmlToMarkdownServiceLab stub — real StyloExtract wiring in Task 5
// ---------------------------------------------------------------------------

public sealed class HtmlToMarkdownServiceLab : IHtmlToMarkdownService
{
    public enum Mode { Read, Scan }

    public static Mode CurrentMode { get; set; } = Mode.Read;

    public Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct) =>
        Task.FromResult(html);
}
