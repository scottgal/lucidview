namespace MermaidSharp.Fluent;

sealed class FlowchartFluentDiagramPlugin : IFluentDiagramPlugin
{
    static readonly IFluentBuilderFactory Factory = new FlowchartBuilderFactory();

    public DiagramType DiagramType => DiagramType.Flowchart;
    public IFluentBuilderFactory BuilderFactory => Factory;

    public ParseResult<MermaidDiagram> TryParse(string source, ParseOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            var diagnostics = new DiagnosticBag(
            [
                MermaidFluentDiagnostics.Error(
                    "PARSE001",
                    "Input source is empty.",
                    suggestion: "Provide Mermaid flowchart source text.")
            ]);
            return ParseResult<MermaidDiagram>.Failed(diagnostics);
        }

        var parser = new FlowchartParser();
        var result = parser.Parse(source);
        if (!result.Success)
        {
            var diagnostics = new DiagnosticBag(
            [
                MermaidFluentDiagnostics.Error(
                    "PARSE002",
                    $"Failed to parse flowchart: {result.Error}",
                    path: options?.SourcePath,
                    suggestion: "Verify flowchart syntax and direction/header lines.")
            ]);
            return ParseResult<MermaidDiagram>.Failed(diagnostics);
        }

        return ParseResult<MermaidDiagram>.Succeeded(FlowchartDiagram.FromModel(result.Value));
    }

    public SerializeResult TrySerialize(MermaidDiagram diagram, SerializeOptions? options = null)
    {
        if (diagram is not FlowchartDiagram flowchart)
        {
            var diagnostics = new DiagnosticBag(
            [
                MermaidFluentDiagnostics.Error(
                    "SER001",
                    $"Flowchart plugin cannot serialize diagram type '{diagram.DiagramType}'.",
                    suggestion: "Use a plugin that matches the diagram type.")
            ]);
            return SerializeResult.Failed(diagnostics);
        }

        return SerializeResult.Succeeded(flowchart.ToMermaid(options));
    }

    sealed class FlowchartBuilderFactory : IFluentBuilderFactory
    {
        public object CreateBuilder() => new FlowchartBuilder();
    }
}
