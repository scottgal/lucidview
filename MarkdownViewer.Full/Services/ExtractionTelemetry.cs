using System.Text;
using System.Text.Json;

namespace MarkdownViewer.Services;

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

/// <summary>
/// Discrete stages of a single extraction request, shown in the status bar
/// as a horizontal sequence with active/done stages at 1.0 opacity and
/// upcoming stages at 0.5.
/// </summary>
public enum ExtractionStage
{
    Idle,
    Fetch,     // HTTP fetch (and optional Playwright rendered-DOM retry)
    Match,     // StyloExtract pipeline: parse → fingerprint → match → render
    Llm,       // Background LLM template induction (when it fires)
    Render,    // Markdown handed to LiveMarkdown; image cache + UI paint
}

public sealed class StageEvent
{
    public ExtractionStage Stage { get; init; }
    /// <summary>True when the stage just started; false when it completed.</summary>
    public bool Started { get; init; }
    /// <summary>Sub-state label, e.g. "Http" / "Playwright" for Fetch, "Novel" / "FastPathHit" for Match.</summary>
    public string? Detail { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed class ExtractionTelemetry
{
    private const int Capacity = 50;
    private readonly object _lock = new();
    private readonly LinkedList<LastExtractionInfo> _ring = new();

    public LastExtractionInfo? Last
    {
        get { lock (_lock) return _ring.Last?.Value; }
    }

    public IReadOnlyList<LastExtractionInfo> Recent
    {
        get { lock (_lock) return _ring.ToList(); }
    }

    public event Action<LastExtractionInfo>? Recorded;

    /// <summary>Fires for each stage start and completion so the UI can light
    /// up the matching segment.</summary>
    public event Action<StageEvent>? StageChanged;

    public void EmitStage(ExtractionStage stage, bool started, string? detail = null, TimeSpan duration = default)
    {
        StageChanged?.Invoke(new StageEvent
        {
            Stage = stage,
            Started = started,
            Detail = detail,
            Duration = duration,
        });
    }

    public void Record(LastExtractionInfo info)
    {
        lock (_lock)
        {
            _ring.AddLast(info);
            while (_ring.Count > Capacity)
                _ring.RemoveFirst();
        }
        Recorded?.Invoke(info);
    }

    public void Clear()
    {
        lock (_lock) _ring.Clear();
    }

    public string ExportNdjson()
    {
        var sb = new StringBuilder();
        foreach (var info in Recent)
            sb.Append(JsonSerializer.Serialize(info)).Append('\n');
        return sb.ToString();
    }
}
