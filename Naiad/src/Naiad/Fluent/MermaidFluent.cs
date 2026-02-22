using System.Text;
using MermaidSharp.Rendering.Surfaces;

namespace MermaidSharp.Fluent;

public static class MermaidFluent
{
    static readonly IFluentPluginRegistry Registry = FluentPluginRegistry.CreateDefault();

    public static IOutputCapabilities OutputCapabilities => CreateCapabilities();
    public static IOutputCapabilities OutputCapabilitiesFor(DiagramType diagramType) =>
        CreateCapabilities(diagramType);

    public static FlowchartBuilder Flowchart(
        Direction direction = Direction.TopToBottom,
        Action<FlowchartBuilder>? configure = null)
    {
        var builder = new FlowchartBuilder(direction);
        configure?.Invoke(builder);
        return builder;
    }

    public static ParseResult<FlowchartDiagram> TryParseFlowchart(string source, ParseOptions? options = null)
    {
        if (!Registry.TryGet(DiagramType.Flowchart, out var plugin))
        {
            var diagnostics = new DiagnosticBag(
            [
                MermaidFluentDiagnostics.Error(
                    "FLUENT200",
                    "Flowchart plugin is not registered.",
                    suggestion: "Register a flowchart fluent plugin.")
            ]);
            return ParseResult<FlowchartDiagram>.Failed(diagnostics);
        }

        var parse = plugin.TryParse(source, options);
        if (!parse.Success || parse.Value is not FlowchartDiagram flowchart)
        {
            return ParseResult<FlowchartDiagram>.Failed(parse.Diagnostics);
        }

        return ParseResult<FlowchartDiagram>.Succeeded(flowchart, parse.Diagnostics);
    }

    public static ParseResult<MermaidDiagram> TryParseAny(string source, ParseOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            var diagnostics = new DiagnosticBag(
            [
                MermaidFluentDiagnostics.Error(
                    "PARSE000",
                    "Input source is empty.",
                    suggestion: "Provide Mermaid source text.")
            ]);
            return ParseResult<MermaidDiagram>.Failed(diagnostics);
        }

        DiagramType diagramType;
        try
        {
            diagramType = Mermaid.DetectDiagramType(source);
        }
        catch (Exception ex)
        {
            var diagnostics = new DiagnosticBag(
            [
                MermaidFluentDiagnostics.Error(
                    "PARSE999",
                    $"Could not detect diagram type: {ex.Message}",
                    path: options?.SourcePath,
                    suggestion: "Ensure the source starts with a valid Mermaid diagram header.")
            ]);
            return ParseResult<MermaidDiagram>.Failed(diagnostics);
        }

        if (!Registry.TryGet(diagramType, out var plugin))
        {
            var diagnostics = new DiagnosticBag(
            [
                MermaidFluentDiagnostics.Error(
                    "FLUENT201",
                    $"No fluent plugin registered for diagram type '{diagramType}'.",
                    path: options?.SourcePath,
                    suggestion: "Register a plugin for this diagram type.")
            ]);
            return ParseResult<MermaidDiagram>.Failed(diagnostics);
        }

        return plugin.TryParse(source, options);
    }

    public static bool TryParse(
        string source,
        out MermaidDiagram? diagram,
        out DiagnosticBag diagnostics,
        ParseOptions? options = null)
    {
        var result = TryParseAny(source, options);
        diagram = result.Value;
        diagnostics = result.Diagnostics;
        return result.Success;
    }

    public static SerializeResult TrySerialize(MermaidDiagram diagram, SerializeOptions? options = null)
    {
        if (!Registry.TryGet(diagram.DiagramType, out var plugin))
        {
            var diagnostics = new DiagnosticBag(
            [
                MermaidFluentDiagnostics.Error(
                    "SER404",
                    $"No serializer plugin registered for diagram type '{diagram.DiagramType}'.",
                    suggestion: "Register a plugin for this diagram type.")
            ]);
            return SerializeResult.Failed(diagnostics);
        }

        return plugin.TrySerialize(diagram, options);
    }

    public static ExportResult Export(
        MermaidDiagram diagram,
        MermaidOutputFormat format,
        ExportOptions? exportOptions = null,
        RenderOptions? renderOptions = null,
        SerializeOptions? serializeOptions = null)
    {
        exportOptions ??= new ExportOptions();
        var serializeResult = TrySerialize(diagram, serializeOptions);
        if (!serializeResult.Success || string.IsNullOrWhiteSpace(serializeResult.Mermaid))
        {
            return ExportResult.Failed(format, serializeResult.Diagnostics);
        }

        var mermaid = serializeResult.Mermaid;
        if (format == MermaidOutputFormat.Mermaid)
        {
            return ExportResult.Succeeded(
                MermaidOutputFormat.Mermaid,
                Encoding.UTF8.GetBytes(mermaid),
                mermaid,
                "text/vnd.mermaid");
        }

        if (!TryMapToSurfaceFormat(format, out var surfaceFormat))
        {
            return UnsupportedFormat(format, exportOptions);
        }

        if (!MermaidRenderSurfaces.SupportedFormats.Contains(surfaceFormat))
        {
            return UnsupportedFormat(format, exportOptions);
        }

        if (!MermaidRenderSurfaces.Supports(surfaceFormat, diagram.DiagramType))
        {
            return UnsupportedFormatForDiagram(format, diagram.DiagramType, exportOptions);
        }

        var request = new RenderSurfaceRequest(
            surfaceFormat,
            exportOptions.Scale,
            exportOptions.Quality,
            exportOptions.Background,
            exportOptions.EmbedFonts);

        RenderSurfaceFailure? failure;
        if (!MermaidRenderSurfaces.TryRender(mermaid, request, out var output, out failure, renderOptions))
        {
            var diagnostics = new DiagnosticBag(
            [
                MermaidFluentDiagnostics.Error(
                    "SER500",
                    $"Failed to export '{format}': {failure?.Message ?? "unknown error"}",
                    suggestion: "Inspect diagram source and registered render surface plugins.",
                    metadata: BuildRenderFailureMetadata(format, diagram.DiagramType, exportOptions, failure))
            ]);
            return ExportResult.Failed(format, diagnostics);
        }

        return ExportResult.Succeeded(
            format,
            output?.Bytes,
            output?.Text,
            output?.MimeType);
    }

    static IOutputCapabilities CreateCapabilities(DiagramType? diagramType = null)
    {
        var formats = new HashSet<MermaidOutputFormat> { MermaidOutputFormat.Mermaid };
        var surfaceFormats = diagramType is null
            ? MermaidRenderSurfaces.SupportedFormats
            : MermaidRenderSurfaces.SupportedFormatsFor(diagramType.Value);

        foreach (var format in surfaceFormats)
        {
            formats.Add(MapToOutputFormat(format));
        }

        return new MermaidOutputCapabilities(formats);
    }

    static MermaidOutputFormat MapToOutputFormat(RenderSurfaceFormat format) =>
        format switch
        {
            RenderSurfaceFormat.Svg => MermaidOutputFormat.Svg,
            RenderSurfaceFormat.Png => MermaidOutputFormat.Png,
            RenderSurfaceFormat.Pdf => MermaidOutputFormat.Pdf,
            RenderSurfaceFormat.Jpeg => MermaidOutputFormat.Jpeg,
            RenderSurfaceFormat.Webp => MermaidOutputFormat.Webp,
            RenderSurfaceFormat.Xps => MermaidOutputFormat.Xps,
            RenderSurfaceFormat.Xaml => MermaidOutputFormat.Xaml,
            RenderSurfaceFormat.ReactFlow => MermaidOutputFormat.ReactFlow,
            _ => MermaidOutputFormat.Svg
        };

    static bool TryMapToSurfaceFormat(MermaidOutputFormat format, out RenderSurfaceFormat surfaceFormat)
    {
        switch (format)
        {
            case MermaidOutputFormat.Svg:
                surfaceFormat = RenderSurfaceFormat.Svg;
                return true;
            case MermaidOutputFormat.Png:
                surfaceFormat = RenderSurfaceFormat.Png;
                return true;
            case MermaidOutputFormat.Pdf:
                surfaceFormat = RenderSurfaceFormat.Pdf;
                return true;
            case MermaidOutputFormat.Jpeg:
                surfaceFormat = RenderSurfaceFormat.Jpeg;
                return true;
            case MermaidOutputFormat.Webp:
                surfaceFormat = RenderSurfaceFormat.Webp;
                return true;
            case MermaidOutputFormat.Xps:
                surfaceFormat = RenderSurfaceFormat.Xps;
                return true;
            case MermaidOutputFormat.Xaml:
                surfaceFormat = RenderSurfaceFormat.Xaml;
                return true;
            case MermaidOutputFormat.ReactFlow:
                surfaceFormat = RenderSurfaceFormat.ReactFlow;
                return true;
            default:
                surfaceFormat = default;
                return false;
        }
    }

    static ExportResult UnsupportedFormat(MermaidOutputFormat format, ExportOptions exportOptions)
    {
        var diagnostics = new DiagnosticBag(
        [
            MermaidFluentDiagnostics.Error(
                "SER401",
                $"Output format '{format}' is not supported by registered render surface plugins.",
                suggestion: "Use MermaidFluent.OutputCapabilities to detect formats or register a custom surface plugin.",
                metadata: new Dictionary<string, object?>
                {
                    ["requestedFormat"] = format.ToString(),
                    ["scale"] = exportOptions.Scale,
                    ["quality"] = exportOptions.Quality,
                    ["platform"] = Environment.OSVersion.Platform.ToString()
                })
        ]);
        return ExportResult.Failed(format, diagnostics);
    }

    static ExportResult UnsupportedFormatForDiagram(
        MermaidOutputFormat format,
        DiagramType diagramType,
        ExportOptions exportOptions)
    {
        var diagnostics = new DiagnosticBag(
        [
            MermaidFluentDiagnostics.Error(
                "SER402",
                $"Output format '{format}' is not supported for diagram type '{diagramType}' by registered render surface plugins.",
                suggestion: "Use MermaidFluent.OutputCapabilitiesFor(diagramType) or register a diagram-type-aware render surface plugin.",
                metadata: new Dictionary<string, object?>
                {
                    ["requestedFormat"] = format.ToString(),
                    ["diagramType"] = diagramType.ToString(),
                    ["scale"] = exportOptions.Scale,
                    ["quality"] = exportOptions.Quality,
                    ["platform"] = Environment.OSVersion.Platform.ToString()
                })
        ]);
        return ExportResult.Failed(format, diagnostics);
    }

    static IReadOnlyDictionary<string, object?> BuildRenderFailureMetadata(
        MermaidOutputFormat format,
        DiagramType diagramType,
        ExportOptions exportOptions,
        RenderSurfaceFailure? failure)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["requestedFormat"] = format.ToString(),
            ["diagramType"] = diagramType.ToString(),
            ["scale"] = exportOptions.Scale,
            ["quality"] = exportOptions.Quality,
            ["platform"] = Environment.OSVersion.Platform.ToString()
        };

        if (failure is null)
        {
            return metadata;
        }

        metadata["renderSurfaceCode"] = failure.Code;
        metadata["renderSurfacePlugin"] = failure.PluginName;
        metadata["renderSurfaceMetadata"] = failure.Metadata;
        return metadata;
    }
}

static class MermaidFluentDiagnostics
{
    public static MermaidDiagnostic Error(
        string code,
        string message,
        SourceSpan? span = null,
        string? path = null,
        string? nodeId = null,
        string? suggestion = null,
        IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, DiagnosticSeverity.Error, message, span, path, nodeId, suggestion, metadata);
}
