namespace MermaidSharp.Diagrams.Voronoi;

public class VoronoiModel : DiagramBase
{
    public List<VoronoiSite> Sites { get; } = [];
    public bool ShowCells { get; set; } = true;
    public bool ShowEdges { get; set; } = true;
    public bool ShowCentroids { get; set; } = true;
}

public class VoronoiSite
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Weight { get; set; }
    public string? Color { get; set; }
}
