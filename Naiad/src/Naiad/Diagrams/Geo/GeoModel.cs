namespace MermaidSharp.Diagrams.Geo;

public enum GeoProjection
{
    Equirectangular,
    Mercator
}

public sealed class GeoModel : DiagramBase
{
    public string MapName { get; set; } = "world";
    public GeoProjection Projection { get; set; } = GeoProjection.Equirectangular;
    public List<GeoPoint> Points { get; } = [];
    public List<GeoTownMarker> Towns { get; } = [];
    public List<GeoArea> Areas { get; } = [];
    public List<GeoRegionStyle> RegionStyles { get; } = [];
}

public sealed class GeoPoint
{
    public string? Label { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Radius { get; set; } = 4.5;
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
}

public sealed class GeoArea
{
    public string? Label { get; set; }
    public List<GeoCoordinate> Coordinates { get; } = [];
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public double Opacity { get; set; } = 0.35;
}

public readonly record struct GeoCoordinate(double Latitude, double Longitude);

public sealed class GeoTownMarker
{
    public required string Query { get; init; }
    public string? Country { get; init; }
    public string? Label { get; init; }
    public double Radius { get; init; } = 4.5;
    public string? Fill { get; init; }
    public string? Stroke { get; init; }
}

public sealed class GeoRegionStyle
{
    public required string RegionId { get; init; }
    public string? Label { get; set; }
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public double? Opacity { get; set; }
}
