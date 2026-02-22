namespace MermaidSharp.Fluent;

sealed class UnsupportedFluentDiagramPlugin(DiagramType type) : IFluentDiagramPlugin
{
    static readonly IFluentBuilderFactory Factory = new UnsupportedBuilderFactory();

    public DiagramType DiagramType => type;
    public IFluentBuilderFactory BuilderFactory => Factory;

    public ParseResult<MermaidDiagram> TryParse(string source, ParseOptions? options = null)
    {
        var diagnostics = new DiagnosticBag(
        [
            MermaidFluentDiagnostics.Error(
                "FLUENT100",
                $"Fluent parser for diagram type '{type}' is not implemented yet.",
                path: options?.SourcePath,
                suggestion: "Use Mermaid.Render(source) directly or add a plugin for this diagram type.")
        ]);
        return ParseResult<MermaidDiagram>.Failed(diagnostics);
    }

    public SerializeResult TrySerialize(MermaidDiagram diagram, SerializeOptions? options = null)
    {
        var diagnostics = new DiagnosticBag(
        [
            MermaidFluentDiagnostics.Error(
                "FLUENT101",
                $"Fluent serializer for diagram type '{type}' is not implemented yet.",
                suggestion: "Use the diagram-type specific plugin implementation.")
        ]);
        return SerializeResult.Failed(diagnostics);
    }

    sealed class UnsupportedBuilderFactory : IFluentBuilderFactory
    {
        public object CreateBuilder() =>
            throw new NotSupportedException("Builder not available for this diagram type.");
    }
}
