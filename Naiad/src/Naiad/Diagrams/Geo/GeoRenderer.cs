using System.Globalization;
using static MermaidSharp.Rendering.RenderUtils;

namespace MermaidSharp.Diagrams.Geo;

public class GeoRenderer : IDiagramRenderer<GeoModel>
{
    const double TitleHeight = 34;
    const double AttributionHeight = 16;
    const double RegionStrokeWidth = 1.25;
    const double OverlayStrokeWidth = 1.5;

    public SvgDocument Render(GeoModel model, RenderOptions options)
    {
        var theme = DiagramTheme.Resolve(options);
        var resolvedTowns = ResolveTownMarkers(model.Towns);
        var mapName = SelectMap(model.MapName, resolvedTowns);
        var map = MermaidGeoMaps.Resolve(mapName);
        var allPoints = model.Points
            .Concat(resolvedTowns.Select(x => x.Point))
            .ToList();

        SecurityValidator.ValidateComplexity(allPoints.Count + model.Areas.Count + map.Regions.Count, 0, options);

        var titleOffset = string.IsNullOrWhiteSpace(model.Title) ? 0 : TitleHeight;
        var contentWidth = map.Width;
        var contentHeight = map.Height + (string.IsNullOrWhiteSpace(map.Attribution) ? 0 : AttributionHeight);
        var width = contentWidth + options.Padding * 2;
        var height = contentHeight + options.Padding * 2 + titleOffset;

        var mapX = options.Padding;
        var mapY = options.Padding + titleOffset;

        var builder = new SvgBuilder()
            .Size(width, height)
            .DiagramType("geo-diagram", "geo diagram")
            .AddStyles(MermaidStyles.BaseStyles)
            .AddRect(0, 0, width, height, fill: theme.Background)
            .AddRect(mapX, mapY, map.Width, map.Height, fill: theme.PrimaryFill, stroke: theme.GridLine, strokeWidth: 1);

        if (!string.IsNullOrWhiteSpace(model.Title))
        {
            builder.AddText(
                width / 2,
                options.Padding + TitleHeight / 2,
                model.Title!,
                anchor: "middle",
                baseline: "middle",
                fontSize: $"{options.FontSize + 2:0.#}px",
                fontFamily: options.FontFamily,
                fontWeight: "bold",
                fill: theme.TextColor);
        }

        var effectiveProjection = map.RequiredProjection ?? model.Projection;

        DrawRegions(builder, model, map, mapX, mapY, theme);
        DrawAreas(builder, model, map, mapX, mapY, theme, effectiveProjection);
        DrawPoints(builder, allPoints, effectiveProjection, map, mapX, mapY, options, theme);
        DrawAttribution(builder, map, mapX, mapY + map.Height, options, theme);

        return builder.Build();
    }

    static void DrawRegions(
        SvgBuilder builder,
        GeoModel model,
        GeoMapDefinition map,
        double mapX,
        double mapY,
        DiagramTheme theme)
    {
        var palette = theme.VividPalette;

        for (var i = 0; i < map.Regions.Count; i++)
        {
            var region = map.Regions[i];
            var regionStyle = ResolveRegionStyle(model.RegionStyles, region);

            var fill = regionStyle?.Fill ?? region.Fill ?? palette[i % palette.Length];
            var stroke = regionStyle?.Stroke ?? region.Stroke ?? theme.AxisLine;

            builder.AddPath(
                region.PathData,
                fill: fill,
                stroke: stroke,
                strokeWidth: RegionStrokeWidth,
                opacity: regionStyle?.Opacity,
                cssClass: "geo-region",
                transform: BuildTranslate(mapX, mapY));

            var label = regionStyle?.Label ?? region.Label;
            if (string.IsNullOrWhiteSpace(label))
                continue;

            builder.AddText(
                mapX + region.LabelPosition.X,
                mapY + region.LabelPosition.Y,
                label,
                anchor: "middle",
                baseline: "middle",
                fontSize: "11px",
                fill: theme.TextColor,
                cssClass: "geo-region-label");
        }
    }

    static void DrawAreas(
        SvgBuilder builder,
        GeoModel model,
        GeoMapDefinition map,
        double mapX,
        double mapY,
        DiagramTheme theme,
        GeoProjection projection)
    {
        foreach (var area in model.Areas)
        {
            if (area.Coordinates.Count < 3)
                continue;

            var projected = area.Coordinates
                .Select(coord => Project(coord, map, projection))
                .ToList();

            var path = BuildClosedPath(projected, mapX, mapY);
            builder.AddPath(
                path,
                fill: area.Fill ?? theme.TertiaryFill,
                stroke: area.Stroke ?? theme.TertiaryStroke,
                strokeWidth: OverlayStrokeWidth,
                opacity: area.Opacity,
                cssClass: "geo-area");

            if (string.IsNullOrWhiteSpace(area.Label))
                continue;

            var centroid = GetCentroid(projected);
            builder.AddText(
                centroid.X + mapX,
                centroid.Y + mapY,
                area.Label!,
                anchor: "middle",
                baseline: "middle",
                fontSize: "11px",
                fill: theme.TextColor,
                cssClass: "geo-area-label");
        }
    }

    static void DrawPoints(
        SvgBuilder builder,
        IEnumerable<GeoPoint> points,
        GeoProjection projection,
        GeoMapDefinition map,
        double mapX,
        double mapY,
        RenderOptions options,
        DiagramTheme theme)
    {
        foreach (var point in points)
        {
            var projected = Project(new GeoCoordinate(point.Latitude, point.Longitude), map, projection);
            var x = projected.X + mapX;
            var y = projected.Y + mapY;

            builder.AddCircle(
                x,
                y,
                Math.Clamp(point.Radius, 2, 20),
                fill: point.Fill ?? theme.PrimaryStroke,
                stroke: point.Stroke ?? theme.Background,
                strokeWidth: 1.2,
                cssClass: "geo-point");

            if (string.IsNullOrWhiteSpace(point.Label))
                continue;

            builder.AddText(
                x + 8,
                y - 8,
                point.Label!,
                anchor: "start",
                baseline: "middle",
                fontSize: $"{Math.Max(10, options.FontSize - 2):0.#}px",
                fontFamily: options.FontFamily,
                fill: theme.TextColor,
                cssClass: "geo-point-label");
        }
    }

    static void DrawAttribution(
        SvgBuilder builder,
        GeoMapDefinition map,
        double mapX,
        double mapBottomY,
        RenderOptions options,
        DiagramTheme theme)
    {
        if (string.IsNullOrWhiteSpace(map.Attribution))
            return;

        builder.AddText(
            mapX + map.Width,
            mapBottomY + 11,
            map.Attribution,
            anchor: "end",
            baseline: "middle",
            fontSize: $"{Math.Max(9, options.FontSize - 6):0.#}px",
            fontFamily: options.FontFamily,
            fill: theme.MutedText,
            cssClass: "geo-attribution");
    }

    static GeoRegionStyle? ResolveRegionStyle(IEnumerable<GeoRegionStyle> styles, GeoRegionDefinition region)
    {
        var regionAliases = region.Aliases ?? [];
        foreach (var style in styles)
        {
            if (string.Equals(style.RegionId, region.Id, StringComparison.OrdinalIgnoreCase))
                return style;

            if (regionAliases.Any(alias => string.Equals(alias, style.RegionId, StringComparison.OrdinalIgnoreCase)))
                return style;
        }

        return null;
    }

    static Position Project(GeoCoordinate coordinate, GeoMapDefinition map, GeoProjection projection)
    {
        var longitude = Math.Clamp(coordinate.Longitude, map.MinLongitude, map.MaxLongitude);
        var latitude = Math.Clamp(coordinate.Latitude, map.MinLatitude, map.MaxLatitude);

        var xRatio = (longitude - map.MinLongitude) / (map.MaxLongitude - map.MinLongitude);
        var yRatio = projection switch
        {
            GeoProjection.Mercator => ToMercatorYRatio(latitude, map),
            _ => (map.MaxLatitude - latitude) / (map.MaxLatitude - map.MinLatitude)
        };

        return new Position(
            xRatio * map.Width,
            yRatio * map.Height);
    }

    static double ToMercatorYRatio(double latitude, GeoMapDefinition map)
    {
        static double ToMercator(double lat)
        {
            var clamped = Math.Clamp(lat, -85, 85);
            var radians = clamped * Math.PI / 180;
            return Math.Log(Math.Tan(Math.PI / 4 + radians / 2));
        }

        var min = ToMercator(map.MinLatitude);
        var max = ToMercator(map.MaxLatitude);
        var current = ToMercator(latitude);

        if (Math.Abs(max - min) < 0.0001)
            return 0.5;

        return (max - current) / (max - min);
    }

    static Position GetCentroid(IReadOnlyList<Position> points)
    {
        if (points.Count == 0)
            return Position.Zero;

        var x = 0.0;
        var y = 0.0;
        foreach (var point in points)
        {
            x += point.X;
            y += point.Y;
        }

        return new Position(x / points.Count, y / points.Count);
    }

    static string BuildClosedPath(IEnumerable<Position> points, double xOffset, double yOffset)
    {
        var buffer = points.ToList();
        if (buffer.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append("M ");
        sb.Append(Fmt(buffer[0].X + xOffset));
        sb.Append(' ');
        sb.Append(Fmt(buffer[0].Y + yOffset));

        for (var i = 1; i < buffer.Count; i++)
        {
            sb.Append(" L ");
            sb.Append(Fmt(buffer[i].X + xOffset));
            sb.Append(' ');
            sb.Append(Fmt(buffer[i].Y + yOffset));
        }

        sb.Append(" Z");
        return sb.ToString();
    }

    static string BuildTranslate(double x, double y) =>
        FormattableString.Invariant($"translate({Fmt(x)},{Fmt(y)})");

    sealed record ResolvedTownPoint(GeoPoint Point, string CountryCode);

    static List<ResolvedTownPoint> ResolveTownMarkers(IEnumerable<GeoTownMarker> towns)
    {
        var points = new List<ResolvedTownPoint>();
        foreach (var town in towns)
        {
            if (!MermaidGeoLocations.TryResolve(town.Query, town.Country, out var resolved))
                continue;

            points.Add(new ResolvedTownPoint(
                new GeoPoint
                {
                    Label = string.IsNullOrWhiteSpace(town.Label) ? resolved.Name : town.Label,
                    Latitude = resolved.Latitude,
                    Longitude = resolved.Longitude,
                    Radius = Math.Clamp(town.Radius, 2, 20),
                    Fill = town.Fill,
                    Stroke = town.Stroke
                },
                resolved.CountryCode));
        }

        return points;
    }

    static string SelectMap(string requestedMapName, IReadOnlyCollection<ResolvedTownPoint> towns)
    {
        if (!string.Equals(requestedMapName, "world", StringComparison.OrdinalIgnoreCase))
            return requestedMapName;

        var mappedCountries = towns
            .Select(x => GeoCountryCatalog.TryResolveMapName(x.CountryCode, out var mapName) ? mapName : null)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (mappedCountries.Count == 1 && MermaidGeoMaps.TryResolve(mappedCountries[0], out _))
            return mappedCountries[0]!;

        return requestedMapName;
    }
}
