namespace MermaidSharp.Fluent;

public sealed record ParseOptions(
    bool Strict = false,
    string? SourcePath = null);

public sealed record SerializeOptions(
    string NewLine = "\n",
    string Indent = "    ");

public sealed record ExportOptions(
    float Scale = 1f,
    int Quality = 100,
    string? Background = null,
    bool EmbedFonts = false);

public enum MermaidOutputFormat
{
    Mermaid,
    Svg,
    Png,
    Pdf,
    Jpeg,
    Webp,
    Xps,
    Xaml,
    ReactFlow
}

public sealed record ParseResult<T>(
    bool Success,
    T? Value,
    DiagnosticBag Diagnostics)
{
    public static ParseResult<T> Succeeded(T value, DiagnosticBag? diagnostics = null) =>
        new(true, value, diagnostics ?? DiagnosticBag.Empty);

    public static ParseResult<T> Failed(DiagnosticBag diagnostics) =>
        new(false, default, diagnostics);
}

public sealed record SerializeResult(
    bool Success,
    string? Mermaid,
    DiagnosticBag Diagnostics)
{
    public static SerializeResult Succeeded(string mermaid, DiagnosticBag? diagnostics = null) =>
        new(true, mermaid, diagnostics ?? DiagnosticBag.Empty);

    public static SerializeResult Failed(DiagnosticBag diagnostics) =>
        new(false, null, diagnostics);
}

public sealed record ExportResult(
    bool Success,
    MermaidOutputFormat Format,
    byte[]? Bytes,
    string? Text,
    string? MimeType,
    DiagnosticBag Diagnostics)
{
    public static ExportResult Succeeded(
        MermaidOutputFormat format,
        byte[]? bytes,
        string? text,
        string? mimeType,
        DiagnosticBag? diagnostics = null) =>
        new(true, format, bytes, text, mimeType, diagnostics ?? DiagnosticBag.Empty);

    public static ExportResult Failed(MermaidOutputFormat format, DiagnosticBag diagnostics) =>
        new(false, format, null, null, null, diagnostics);
}

public interface IOutputCapabilities
{
    bool Supports(MermaidOutputFormat format);
    IReadOnlyCollection<MermaidOutputFormat> Formats { get; }
}

public sealed class MermaidOutputCapabilities(IEnumerable<MermaidOutputFormat> formats) : IOutputCapabilities
{
    readonly HashSet<MermaidOutputFormat> _formats = [.. formats];

    public IReadOnlyCollection<MermaidOutputFormat> Formats => _formats;
    public bool Supports(MermaidOutputFormat format) => _formats.Contains(format);
}

public interface IFluentBuilderFactory
{
    object CreateBuilder();
}

public interface IFluentDiagramPlugin
{
    DiagramType DiagramType { get; }
    ParseResult<MermaidDiagram> TryParse(string source, ParseOptions? options = null);
    SerializeResult TrySerialize(MermaidDiagram diagram, SerializeOptions? options = null);
    IFluentBuilderFactory BuilderFactory { get; }
}

public interface IFluentPluginRegistry
{
    bool TryGet(DiagramType type, out IFluentDiagramPlugin plugin);
    IReadOnlyCollection<IFluentDiagramPlugin> Plugins { get; }
}
