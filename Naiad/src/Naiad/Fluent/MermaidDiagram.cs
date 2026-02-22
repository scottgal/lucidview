namespace MermaidSharp.Fluent;

public abstract class MermaidDiagram
{
    public abstract DiagramType DiagramType { get; }
    public abstract string ToMermaid(SerializeOptions? options = null);
}
