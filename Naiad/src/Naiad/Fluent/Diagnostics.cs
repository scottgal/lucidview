namespace MermaidSharp.Fluent;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record SourceSpan(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

public sealed record MermaidDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    SourceSpan? Span = null,
    string? Path = null,
    string? NodeId = null,
    string? Suggestion = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed class DiagnosticBag
{
    readonly List<MermaidDiagnostic> _items;

    public DiagnosticBag() => _items = [];

    public DiagnosticBag(IEnumerable<MermaidDiagnostic> items) => _items = [.. items];

    public static DiagnosticBag Empty { get; } = new();

    public IReadOnlyList<MermaidDiagnostic> Items => _items;
    public bool HasErrors => _items.Any(x => x.Severity == DiagnosticSeverity.Error);
    public int ErrorCount => _items.Count(x => x.Severity == DiagnosticSeverity.Error);
    public int WarningCount => _items.Count(x => x.Severity == DiagnosticSeverity.Warning);

    public void Add(MermaidDiagnostic diagnostic) => _items.Add(diagnostic);

    public IEnumerable<MermaidDiagnostic> WhereSeverity(DiagnosticSeverity severity) =>
        _items.Where(x => x.Severity == severity);

    public IEnumerable<MermaidDiagnostic> WhereCodePrefix(string codePrefix) =>
        _items.Where(x => x.Code.StartsWith(codePrefix, StringComparison.OrdinalIgnoreCase));

    static readonly System.Text.Json.JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public string ToJson() =>
        System.Text.Json.JsonSerializer.Serialize(_items, s_jsonOptions);
}
