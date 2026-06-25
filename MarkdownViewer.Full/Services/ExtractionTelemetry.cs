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
    TimeSpan FetchDuration,
    bool LlmInductionFired,
    TimeSpan LlmDuration,
    int BlockCount,
    int OutputCharacterCount);

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
            sb.AppendLine(JsonSerializer.Serialize(info));
        return sb.ToString();
    }
}
