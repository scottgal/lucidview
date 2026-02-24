namespace MermaidSharp.Diagrams.Geo;

public sealed record GeoResolvedLocation(
    string Name,
    string CountryCode,
    double Latitude,
    double Longitude);

public interface IGeoLocationResolverPlugin
{
    string Name { get; }
    IReadOnlyCollection<string> Aliases { get; }
    bool TryResolve(string query, string? country, out GeoResolvedLocation location);
}

public sealed class StaticGeoLocationResolverPlugin(
    string name,
    Func<string, string?, (bool Success, GeoResolvedLocation Location)> resolver,
    IReadOnlyCollection<string>? aliases = null) : IGeoLocationResolverPlugin
{
    public string Name { get; } = name;
    public IReadOnlyCollection<string> Aliases { get; } = aliases ?? [];

    public bool TryResolve(string query, string? country, out GeoResolvedLocation location)
    {
        var result = resolver(query, country);
        location = result.Location;
        return result.Success;
    }
}

public sealed class GeoLocationResolverRegistry
{
    readonly object _sync = new();
    readonly List<IGeoLocationResolverPlugin> _plugins = [];

    public IReadOnlyCollection<IGeoLocationResolverPlugin> Plugins
    {
        get
        {
            lock (_sync)
            {
                return [.. _plugins];
            }
        }
    }

    public bool TryRegister(IGeoLocationResolverPlugin plugin, out string? error)
    {
        try
        {
            Register(plugin);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Register(IGeoLocationResolverPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (string.IsNullOrWhiteSpace(plugin.Name))
            throw new ArgumentException("Geo location resolver name cannot be empty.", nameof(plugin));

        var ids = GetIdentifiers(plugin).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            _plugins.RemoveAll(existing => GetIdentifiers(existing).Any(ids.Contains));
            _plugins.Insert(0, plugin);
        }
    }

    public bool Unregister(string pluginName)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
            return false;

        lock (_sync)
        {
            return _plugins.RemoveAll(x => MatchesIdentifier(x, pluginName)) > 0;
        }
    }

    public IReadOnlyList<string> GetAvailableResolverNames()
    {
        lock (_sync)
        {
            return
            [
                .. _plugins
                    .Select(p => p.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];
        }
    }

    public bool TryResolve(string query, string? country, out GeoResolvedLocation location)
    {
        location = new GeoResolvedLocation("", "", 0, 0);

        List<IGeoLocationResolverPlugin> plugins;
        lock (_sync)
        {
            plugins = [.. _plugins];
        }

        foreach (var plugin in plugins)
        {
            if (plugin.TryResolve(query, country, out location))
                return true;
        }

        return false;
    }

    static IEnumerable<string> GetIdentifiers(IGeoLocationResolverPlugin plugin)
    {
        yield return plugin.Name;
        foreach (var alias in plugin.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
                yield return alias;
        }
    }

    static bool MatchesIdentifier(IGeoLocationResolverPlugin plugin, string id) =>
        string.Equals(plugin.Name, id, StringComparison.OrdinalIgnoreCase) ||
        plugin.Aliases.Any(alias => string.Equals(alias, id, StringComparison.OrdinalIgnoreCase));
}

public static class MermaidGeoLocations
{
    static readonly GeoLocationResolverRegistry Registry = CreateDefaultRegistry();

    public static IReadOnlyCollection<IGeoLocationResolverPlugin> Plugins => Registry.Plugins;

    public static IReadOnlyList<string> GetAvailableResolverNames() => Registry.GetAvailableResolverNames();

    public static bool TryResolve(string query, string? country, out GeoResolvedLocation location) =>
        Registry.TryResolve(query, country, out location);

    public static bool TryRegister(IGeoLocationResolverPlugin plugin, out string? error) =>
        Registry.TryRegister(plugin, out error);

    public static void Register(IGeoLocationResolverPlugin plugin) => Registry.Register(plugin);

    public static bool Unregister(string pluginName) => Registry.Unregister(pluginName);

    static GeoLocationResolverRegistry CreateDefaultRegistry()
    {
        var registry = new GeoLocationResolverRegistry();
        registry.Register(CreateBuiltInTownResolver());
        return registry;
    }

    static IGeoLocationResolverPlugin CreateBuiltInTownResolver()
    {
        var entries = CreateTownIndex();
        return new StaticGeoLocationResolverPlugin(
            name: "town-gazetteer",
            aliases: ["builtin-towns", "gazetteer"],
            resolver: (query, country) =>
            {
                var normalizedQuery = GeoCountryCatalog.NormalizeToken(query);
                if (string.IsNullOrWhiteSpace(normalizedQuery))
                    return (false, new GeoResolvedLocation("", "", 0, 0));

                var normalizedCountry = GeoCountryCatalog.TryResolveCountryCode(country) ??
                                        GeoCountryCatalog.NormalizeToken(country).ToUpperInvariant();

                var candidates = entries
                    .Where(x => x.SearchNames.Contains(normalizedQuery, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (!string.IsNullOrWhiteSpace(normalizedCountry))
                {
                    var inCountry = candidates
                        .Where(x => string.Equals(x.CountryCode, normalizedCountry, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (inCountry.Count > 0)
                        candidates = inCountry;
                }

                var best = candidates.OrderBy(x => x.Rank).FirstOrDefault();
                if (best is null)
                    return (false, new GeoResolvedLocation("", "", 0, 0));

                return (true, new GeoResolvedLocation(best.Name, best.CountryCode, best.Latitude, best.Longitude));
            });
    }

    sealed record TownEntry(
        string Name,
        string CountryCode,
        double Latitude,
        double Longitude,
        int Rank,
        IReadOnlyCollection<string> SearchNames);

    static List<TownEntry> CreateTownIndex()
    {
        List<TownEntry> towns =
        [
            Town("London", "GB", 51.5074, -0.1278, 1, "london"),
            Town("Manchester", "GB", 53.4808, -2.2426, 2, "manchester"),
            Town("Birmingham", "GB", 52.4862, -1.8904, 3, "birmingham"),
            Town("Edinburgh", "GB", 55.9533, -3.1883, 4, "edinburgh"),
            Town("Glasgow", "GB", 55.8642, -4.2518, 5, "glasgow"),
            Town("Paris", "FR", 48.8566, 2.3522, 1, "paris"),
            Town("Lyon", "FR", 45.7640, 4.8357, 2, "lyon"),
            Town("Marseille", "FR", 43.2965, 5.3698, 3, "marseille"),
            Town("Berlin", "DE", 52.5200, 13.4050, 1, "berlin"),
            Town("Munich", "DE", 48.1351, 11.5820, 2, "munich", "muenchen"),
            Town("Hamburg", "DE", 53.5511, 9.9937, 3, "hamburg"),
            Town("Madrid", "ES", 40.4168, -3.7038, 1, "madrid"),
            Town("Barcelona", "ES", 41.3874, 2.1686, 2, "barcelona"),
            Town("Valencia", "ES", 39.4699, -0.3763, 3, "valencia"),
            Town("Rome", "IT", 41.9028, 12.4964, 1, "rome", "roma"),
            Town("Milan", "IT", 45.4642, 9.1900, 2, "milan", "milano"),
            Town("Naples", "IT", 40.8518, 14.2681, 3, "naples", "napoli"),
            Town("New York", "US", 40.7128, -74.0060, 1, "newyork", "newyorkcity", "nyc"),
            Town("Los Angeles", "US", 34.0522, -118.2437, 2, "losangeles", "la"),
            Town("Chicago", "US", 41.8781, -87.6298, 3, "chicago"),
            Town("Seattle", "US", 47.6062, -122.3321, 4, "seattle"),
            Town("Miami", "US", 25.7617, -80.1918, 5, "miami"),
            Town("Dallas", "US", 32.7767, -96.7970, 6, "dallas"),
            Town("San Francisco", "US", 37.7749, -122.4194, 7, "sanfrancisco", "sf"),
            Town("Boston", "US", 42.3601, -71.0589, 8, "boston"),
            Town("Toronto", "CA", 43.6532, -79.3832, 1, "toronto"),
            Town("Montreal", "CA", 45.5017, -73.5673, 2, "montreal"),
            Town("Vancouver", "CA", 49.2827, -123.1207, 3, "vancouver"),
            Town("Calgary", "CA", 51.0447, -114.0719, 4, "calgary"),
            Town("Ottawa", "CA", 45.4215, -75.6972, 5, "ottawa"),
            Town("Sydney", "AU", -33.8688, 151.2093, 1, "sydney"),
            Town("Melbourne", "AU", -37.8136, 144.9631, 2, "melbourne"),
            Town("Brisbane", "AU", -27.4698, 153.0251, 3, "brisbane"),
            Town("Perth", "AU", -31.9505, 115.8605, 4, "perth"),
            Town("Adelaide", "AU", -34.9285, 138.6007, 5, "adelaide"),
            Town("Delhi", "IN", 28.6139, 77.2090, 1, "delhi", "newdelhi"),
            Town("Mumbai", "IN", 19.0760, 72.8777, 2, "mumbai", "bombay"),
            Town("Bengaluru", "IN", 12.9716, 77.5946, 3, "bengaluru", "bangalore"),
            Town("Kolkata", "IN", 22.5726, 88.3639, 4, "kolkata", "calcutta"),
            Town("Chennai", "IN", 13.0827, 80.2707, 5, "chennai", "madras"),
            Town("Hyderabad", "IN", 17.3850, 78.4867, 6, "hyderabad"),
            Town("Tokyo", "JP", 35.6762, 139.6503, 1, "tokyo"),
            Town("Osaka", "JP", 34.6937, 135.5023, 2, "osaka"),
            Town("Kyoto", "JP", 35.0116, 135.7681, 3, "kyoto"),
            Town("Nagoya", "JP", 35.1815, 136.9066, 4, "nagoya"),
            Town("Sapporo", "JP", 43.0618, 141.3545, 5, "sapporo"),
            Town("Fukuoka", "JP", 33.5902, 130.4017, 6, "fukuoka"),
            Town("Sao Paulo", "BR", -23.5505, -46.6333, 1, "saopaulo", "sao"),
            Town("Rio de Janeiro", "BR", -22.9068, -43.1729, 2, "riodejaneiro", "rio"),
            Town("Brasilia", "BR", -15.7939, -47.8828, 3, "brasilia"),
            Town("Salvador", "BR", -12.9777, -38.5016, 4, "salvador"),
            Town("Dublin", "IE", 53.3498, -6.2603, 1, "dublin"),
            Town("Amsterdam", "NL", 52.3676, 4.9041, 1, "amsterdam"),
            Town("Brussels", "BE", 50.8503, 4.3517, 1, "brussels"),
            Town("Lisbon", "PT", 38.7223, -9.1393, 1, "lisbon"),
            Town("Zurich", "CH", 47.3769, 8.5417, 1, "zurich"),
            Town("Vienna", "AT", 48.2082, 16.3738, 1, "vienna"),
            Town("Prague", "CZ", 50.0755, 14.4378, 1, "prague"),
            Town("Warsaw", "PL", 52.2297, 21.0122, 1, "warsaw"),
            Town("Stockholm", "SE", 59.3293, 18.0686, 1, "stockholm"),
            Town("Oslo", "NO", 59.9139, 10.7522, 1, "oslo"),
            Town("Helsinki", "FI", 60.1699, 24.9384, 1, "helsinki"),
            Town("Copenhagen", "DK", 55.6761, 12.5683, 1, "copenhagen")
        ];

        return towns;
    }

    static TownEntry Town(
        string name,
        string countryCode,
        double latitude,
        double longitude,
        int rank,
        params string[] aliases)
    {
        var names = new List<string> { GeoCountryCatalog.NormalizeToken(name) };
        foreach (var alias in aliases)
        {
            var normalized = GeoCountryCatalog.NormalizeToken(alias);
            if (!string.IsNullOrWhiteSpace(normalized) &&
                !names.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(normalized);
            }
        }

        return new TownEntry(name, countryCode, latitude, longitude, rank, names);
    }
}
