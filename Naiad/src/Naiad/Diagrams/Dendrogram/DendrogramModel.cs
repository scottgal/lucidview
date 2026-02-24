namespace MermaidSharp.Diagrams.Dendrogram;

public class DendrogramModel : DiagramBase
{
    public List<DendrogramLeaf> Leaves { get; } = [];
    public List<DendrogramMerge> Merges { get; } = [];
    public bool Horizontal { get; set; }
}

public class DendrogramLeaf
{
    public required string Id { get; init; }
    public required string Label { get; init; }
}

public class DendrogramMerge
{
    public required string Left { get; init; }
    public required string Right { get; init; }
    public required double Height { get; init; }
    public string? Label { get; set; }
}
