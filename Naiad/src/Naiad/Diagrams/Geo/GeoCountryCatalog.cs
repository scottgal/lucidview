namespace MermaidSharp.Diagrams.Geo;

internal static class GeoCountryCatalog
{
    static readonly Dictionary<string, string> CountryToMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["us"] = "usa",
        ["usa"] = "usa",
        ["unitedstates"] = "usa",
        ["america"] = "usa",
        ["uk"] = "uk",
        ["gb"] = "uk",
        ["gbr"] = "uk",
        ["britain"] = "uk",
        ["greatbritain"] = "uk",
        ["unitedkingdom"] = "uk",
        ["england"] = "uk",
        ["fr"] = "france",
        ["fra"] = "france",
        ["france"] = "france",
        ["de"] = "germany",
        ["deu"] = "germany",
        ["germany"] = "germany",
        ["es"] = "spain",
        ["esp"] = "spain",
        ["spain"] = "spain",
        ["it"] = "italy",
        ["ita"] = "italy",
        ["italy"] = "italy",
        ["ca"] = "canada",
        ["can"] = "canada",
        ["canada"] = "canada",
        ["au"] = "australia",
        ["aus"] = "australia",
        ["australia"] = "australia",
        ["in"] = "india",
        ["ind"] = "india",
        ["india"] = "india",
        ["jp"] = "japan",
        ["jpn"] = "japan",
        ["japan"] = "japan",
        ["br"] = "brazil",
        ["bra"] = "brazil",
        ["brazil"] = "brazil",
        ["world"] = "world",
        ["globe"] = "world"
    };

    static readonly Dictionary<string, string> CountryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["usa"] = "US",
        ["uk"] = "GB",
        ["france"] = "FR",
        ["germany"] = "DE",
        ["spain"] = "ES",
        ["italy"] = "IT",
        ["canada"] = "CA",
        ["australia"] = "AU",
        ["india"] = "IN",
        ["japan"] = "JP",
        ["brazil"] = "BR"
    };

    public static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value.Trim().ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c))
            .ToArray();
        return new string(chars);
    }

    public static bool TryResolveMapName(string? token, out string mapName)
    {
        mapName = string.Empty;
        var key = NormalizeToken(token);
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (CountryToMap.TryGetValue(key, out var resolved) && !string.IsNullOrWhiteSpace(resolved))
        {
            mapName = resolved;
            return true;
        }

        // allow direct map names too
        mapName = token!.Trim();
        return true;
    }

    public static string? TryResolveCountryCode(string? token)
    {
        if (!TryResolveMapName(token, out var mapName))
            return null;

        return CountryCodes.GetValueOrDefault(mapName);
    }
}
