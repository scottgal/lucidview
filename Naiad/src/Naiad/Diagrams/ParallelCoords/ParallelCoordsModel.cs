namespace MermaidSharp.Diagrams.ParallelCoords;

public class ParallelCoordsModel : DiagramBase
{
    public List<ParallelAxis> Axes { get; } = [];
    public List<ParallelDataset> Datasets { get; } = []
    ;
    public bool ShowLegend { get; set; } = true;
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
}

public class ParallelAxis
{
    public required string Id { get; init; }
    public string? Label { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public int Index { get; set; }
}

public class ParallelDataset
{
    public required string Name { get; init; }
    public List<double> Values { get; } = [];
    public string? Color { get; set; }
    public string? CssClass { get; set; }
}
