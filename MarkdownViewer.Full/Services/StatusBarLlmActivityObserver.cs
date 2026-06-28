using StyloExtract.Abstractions.TemplateEnrichment;

namespace MarkdownViewer.Services;

/// <summary>
/// Lights up the status-bar Llm segment while a CPU-bound LLM call is in
/// flight. Without this signal a multi-second qwen3.5:4b inference looks
/// indistinguishable from the app being stuck. Called on the
/// TemplateEnrichmentCoordinator's drain thread; we hop to the UI dispatcher
/// via <see cref="ExtractionTelemetry.EmitStage"/> which the existing
/// MainWindow handler marshals via Dispatcher.UIThread.
/// </summary>
internal sealed class StatusBarLlmActivityObserver : ILlmActivityObserver
{
    private readonly ExtractionTelemetry _telemetry;

    public StatusBarLlmActivityObserver(ExtractionTelemetry telemetry)
    {
        _telemetry = telemetry;
    }

    public void LlmCallStarted(string host, EnrichmentJobKind kind)
    {
        _telemetry.EmitStage(
            ExtractionStage.Llm,
            started: true,
            detail: $"{host} ({kind.ToString().ToLowerInvariant()})");
    }

    public void LlmCallEnded(string host, EnrichmentJobKind kind, bool success)
    {
        _telemetry.EmitStage(
            ExtractionStage.Llm,
            started: false,
            detail: success ? $"{host} ✓" : $"{host} (rejected)");
    }
}
