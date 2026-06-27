using StyloExtract.Streaming;

namespace MarkdownViewer.Services;

/// <summary>
/// alpha.18: bridges the streaming-template refit event stream into the
/// existing ExtractionTelemetry "llm" stage so the status bar lights up
/// when drift triggers a refit. Display detail is shaped as
/// <c>"&lt;host&gt; (refit v&lt;n&gt;)"</c> so the user can tell a refit-
/// driven version bump apart from a brand-new heuristic / LLM induction.
/// </summary>
internal sealed class RefitTelemetrySink : IStreamingTemplateVersionSink
{
    private readonly ExtractionTelemetry _telemetry;

    public RefitTelemetrySink(ExtractionTelemetry telemetry)
    {
        _telemetry = telemetry;
    }

    public ValueTask OnRefittedAsync(StreamingTemplateRefitEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            _telemetry.EmitStage(
                ExtractionStage.Llm,
                started: false,
                detail: $"{evt.Host} (refit v{evt.NewVersion})");
            Console.WriteLine(
                $"[streaming-refit] {evt.Host} v{evt.OldVersion} -> v{evt.NewVersion} ({evt.Reason})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[streaming-refit] telemetry-emit failed: {ex.Message}");
        }
        return ValueTask.CompletedTask;
    }
}
