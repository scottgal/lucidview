using System.Globalization;
using System.Text.RegularExpressions;

namespace MermaidSharp.Diagrams.Geo;

public class GeoParser : IDiagramParser<GeoModel>
{
    public DiagramType DiagramType => DiagramType.Geo;

    enum GeoItemType
    {
        Title,
        Map,
        Country,
        Projection,
        Town,
        Point,
        Area,
        Region
    }

    sealed record GeoItem(GeoItemType Type, string Payload);

    static readonly Regex TokenPattern = new(
        @"[^\s=]+=""(?:[^""\\]|\\.)*""|""(?:[^""\\]|\\.)*""|\S+",
        RegexCompat.Compiled);

    static readonly Regex CoordinatePattern = new(
        @"^\s*(?<lat>-?\d+(?:\.\d+)?)\s*,\s*(?<lon>-?\d+(?:\.\d+)?)\s*$",
        RegexCompat.Compiled | RegexOptions.CultureInvariant);

    static readonly Regex SafeColorPattern = new(
        @"^(#[0-9a-fA-F]{3,8}|[a-zA-Z]+|(rgb|rgba|hsl|hsla)\([\d\s.,%+\-]+\))$",
        RegexCompat.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    static readonly Parser<char, string> RestOfLine =
        Token(c => c != '\r' && c != '\n').ManyString();

    static readonly Parser<char, Unit> SkipLine =
        Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
            .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

    static Parser<char, GeoItem> DirectiveLine(string keyword, GeoItemType type) =>
        from _ in CommonParsers.InlineWhitespace
        from __ in CIString(keyword)
        from ___ in CommonParsers.RequiredWhitespace
        from payload in RestOfLine
        from ____ in CommonParsers.LineEnd
        select new GeoItem(type, payload.Trim());

    static Parser<char, GeoItem?> ContentItem =>
        OneOf(
            Try(DirectiveLine("title", GeoItemType.Title).Select(x => (GeoItem?)x)),
            Try(DirectiveLine("map", GeoItemType.Map).Select(x => (GeoItem?)x)),
            Try(DirectiveLine("country", GeoItemType.Country).Select(x => (GeoItem?)x)),
            Try(DirectiveLine("projection", GeoItemType.Projection).Select(x => (GeoItem?)x)),
            Try(DirectiveLine("town", GeoItemType.Town).Select(x => (GeoItem?)x)),
            Try(DirectiveLine("point", GeoItemType.Point).Select(x => (GeoItem?)x)),
            Try(DirectiveLine("area", GeoItemType.Area).Select(x => (GeoItem?)x)),
            Try(DirectiveLine("region", GeoItemType.Region).Select(x => (GeoItem?)x)),
            SkipLine.ThenReturn((GeoItem?)null));

    public static Parser<char, GeoModel> Parser =>
        from _ in CommonParsers.InlineWhitespace
        from __ in OneOf(Try(CIString("geo-beta")), CIString("geo"))
        from ___ in CommonParsers.InlineWhitespace
        from ____ in CommonParsers.LineEnd
        from content in ContentItem.ManyThen(End)
        select BuildModel(content.Item1.Where(x => x is not null).Cast<GeoItem>().ToList());

    static GeoModel BuildModel(List<GeoItem> items)
    {
        var model = new GeoModel();

        foreach (var item in items)
        {
            switch (item.Type)
            {
                case GeoItemType.Title:
                    model.Title = ParseTextValue(item.Payload);
                    break;
                case GeoItemType.Map:
                    var mapName = ParseTextValue(item.Payload);
                    if (GeoCountryCatalog.TryResolveMapName(mapName, out var resolvedMapName))
                        model.MapName = resolvedMapName;
                    break;
                case GeoItemType.Country:
                    var countryName = ParseTextValue(item.Payload);
                    if (GeoCountryCatalog.TryResolveMapName(countryName, out var countryMapName))
                        model.MapName = countryMapName;
                    break;
                case GeoItemType.Projection:
                    if (TryParseProjection(item.Payload, out var projection))
                        model.Projection = projection;
                    break;
                case GeoItemType.Town:
                    if (TryParseTown(item.Payload, out var town))
                        model.Towns.Add(town);
                    break;
                case GeoItemType.Point:
                    if (TryParsePoint(item.Payload, out var point))
                        model.Points.Add(point);
                    break;
                case GeoItemType.Area:
                    if (TryParseArea(item.Payload, out var area))
                        model.Areas.Add(area);
                    break;
                case GeoItemType.Region:
                    if (TryParseRegionStyle(item.Payload, out var regionStyle))
                        model.RegionStyles.Add(regionStyle);
                    break;
            }
        }

        return model;
    }

    static bool TryParseProjection(string payload, out GeoProjection projection)
    {
        var value = payload.Trim().ToLowerInvariant();
        projection = value switch
        {
            "mercator" => GeoProjection.Mercator,
            "equirectangular" => GeoProjection.Equirectangular,
            "eq" => GeoProjection.Equirectangular,
            _ => GeoProjection.Equirectangular
        };

        return value is "mercator" or "equirectangular" or "eq";
    }

    static bool TryParseTown(string payload, out GeoTownMarker town)
    {
        town = new GeoTownMarker { Query = string.Empty };
        var tokens = Tokenize(payload);
        if (tokens.Count == 0)
            return false;

        var index = 0;
        var query = ParseTextValue(tokens[index]);
        if (string.IsNullOrWhiteSpace(query))
            return false;
        index++;

        string? positionalCountry = null;
        if (index < tokens.Count && !tokens[index].Contains('='))
        {
            positionalCountry = ParseTextValue(tokens[index]);
            index++;
        }

        var attributes = ParseAttributes(tokens, index);

        var country = attributes.GetValueOrDefault("country") ??
                      attributes.GetValueOrDefault("in") ??
                      positionalCountry;
        var countryCode = GeoCountryCatalog.TryResolveCountryCode(country) ?? country;
        var label = ParseTextValue(attributes.GetValueOrDefault("label"));
        if (string.IsNullOrWhiteSpace(label))
            label = null;

        var radius = 4.5;
        if (attributes.TryGetValue("size", out var sizeValue) || attributes.TryGetValue("radius", out sizeValue))
        {
            if (double.TryParse(sizeValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRadius))
                radius = Math.Clamp(parsedRadius, 2, 20);
        }

        town = new GeoTownMarker
        {
            Query = query,
            Country = countryCode,
            Label = label,
            Radius = radius,
            Fill = NormalizeColor(attributes.GetValueOrDefault("fill")) ??
                   NormalizeColor(attributes.GetValueOrDefault("color")),
            Stroke = NormalizeColor(attributes.GetValueOrDefault("stroke"))
        };

        return true;
    }

    static bool TryParsePoint(string payload, out GeoPoint point)
    {
        point = new GeoPoint();
        var tokens = Tokenize(payload);
        if (tokens.Count == 0)
            return false;

        var index = 0;
        string? label = null;
        if (!TryParseCoordinateToken(tokens[index], out _, out _))
        {
            label = ParseTextValue(tokens[index]);
            index++;
        }

        if (index >= tokens.Count || !TryParseCoordinateToken(tokens[index], out var latitude, out var longitude))
            return false;

        index++;

        var attributes = ParseAttributes(tokens, index);
        if (attributes.TryGetValue("size", out var sizeValue) ||
            attributes.TryGetValue("radius", out sizeValue))
        {
            if (double.TryParse(sizeValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius))
                point.Radius = Math.Clamp(radius, 2, 20);
        }

        point.Label = label;
        point.Latitude = latitude;
        point.Longitude = longitude;
        point.Fill = NormalizeColor(attributes.GetValueOrDefault("fill")) ??
                     NormalizeColor(attributes.GetValueOrDefault("color"));
        point.Stroke = NormalizeColor(attributes.GetValueOrDefault("stroke"));
        return true;
    }

    static bool TryParseArea(string payload, out GeoArea area)
    {
        area = new GeoArea();
        var tokens = Tokenize(payload);
        if (tokens.Count == 0)
            return false;

        var index = 0;
        string? label = null;
        if (!LooksLikeCoordinateListToken(tokens[index]) && !tokens[index].Contains('='))
        {
            label = ParseTextValue(tokens[index]);
            index++;
        }

        var coordinateTokens = new List<string>();
        while (index < tokens.Count && !tokens[index].Contains('='))
        {
            coordinateTokens.Add(tokens[index]);
            index++;
        }

        if (coordinateTokens.Count == 0 ||
            !TryParseCoordinateList(string.Join(' ', coordinateTokens), area.Coordinates) ||
            area.Coordinates.Count < 3)
        {
            return false;
        }

        var attributes = ParseAttributes(tokens, index);
        area.Label = label;
        area.Fill = NormalizeColor(attributes.GetValueOrDefault("fill")) ??
                    NormalizeColor(attributes.GetValueOrDefault("color"));
        area.Stroke = NormalizeColor(attributes.GetValueOrDefault("stroke"));

        if (double.TryParse(attributes.GetValueOrDefault("opacity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity))
            area.Opacity = Math.Clamp(opacity, 0, 1);

        return true;
    }

    static bool TryParseRegionStyle(string payload, out GeoRegionStyle regionStyle)
    {
        regionStyle = new GeoRegionStyle { RegionId = string.Empty };
        var tokens = Tokenize(payload);
        if (tokens.Count == 0)
            return false;

        var regionId = ParseTextValue(tokens[0]);
        if (string.IsNullOrWhiteSpace(regionId))
            return false;

        var attributes = ParseAttributes(tokens, 1);
        regionStyle = new GeoRegionStyle
        {
            RegionId = regionId,
            Label = ParseTextValue(attributes.GetValueOrDefault("label")),
            Fill = NormalizeColor(attributes.GetValueOrDefault("fill")) ??
                   NormalizeColor(attributes.GetValueOrDefault("color")),
            Stroke = NormalizeColor(attributes.GetValueOrDefault("stroke")),
            Opacity = double.TryParse(attributes.GetValueOrDefault("opacity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity)
                ? Math.Clamp(opacity, 0, 1)
                : null
        };

        return true;
    }

    static List<string> Tokenize(string payload) =>
        [.. TokenPattern.Matches(payload).Select(match => match.Value)];

    static Dictionary<string, string> ParseAttributes(List<string> tokens, int startIndex)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = startIndex; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var separator = token.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = token[..separator].Trim();
            var value = token[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            attributes[key] = ParseTextValue(value);
        }

        return attributes;
    }

    static bool TryParseCoordinateToken(string token, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;

        var match = CoordinatePattern.Match(token);
        if (!match.Success)
            return false;

        return double.TryParse(match.Groups["lat"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) &&
               double.TryParse(match.Groups["lon"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude);
    }

    static bool TryParseCoordinateList(string payload, List<GeoCoordinate> coordinates)
    {
        coordinates.Clear();
        var parts = payload.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (!TryParseCoordinateToken(part, out var latitude, out var longitude))
                return false;

            coordinates.Add(new GeoCoordinate(latitude, longitude));
        }

        return coordinates.Count > 0;
    }

    static bool LooksLikeCoordinateListToken(string token) =>
        token.Contains(',', StringComparison.Ordinal);

    static string ParseTextValue(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        var value = token.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];

        return value.Replace("\\\"", "\"", StringComparison.Ordinal);
    }

    static string? NormalizeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (normalized.Length > 64)
            return null;
        if (normalized.IndexOfAny(['"', '\'', '<', '>', ';']) >= 0)
            return null;
        if (normalized.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("expression", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return SafeColorPattern.IsMatch(normalized) ? normalized : null;
    }

    public Result<char, GeoModel> Parse(string input) => Parser.Parse(input);
}
