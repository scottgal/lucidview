namespace MermaidSharp.Diagrams.BubblePack;

public class BubblePackModel : DiagramBase
{
    public List<BubbleNode> RootNodes { get; } = [];
}

public class BubbleNode
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public double Value { get; set; }
    public List<BubbleNode> Children { get; } = [];
    public string? Color { get; set; }
    public string? CssClass { get; set; }

    public bool IsLeaf => Children.Count == 0;

    public double TotalValue => IsLeaf ? Value : Children.Sum(c => c.TotalValue);

    public double X { get; set; }
    public double Y { get; set; }
    public double Radius { get; set; }
}
