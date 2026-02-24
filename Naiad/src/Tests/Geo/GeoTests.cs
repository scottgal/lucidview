using MermaidSharp.Diagrams.Geo;

public class GeoTests
{
    [Test]
    public void WorldMap_WithTownsAreasAndRegionStyles_Renders()
    {
        var input =
            """
            geo
                title "Geo Coverage"
                map world
                town "London" country=uk color=#ef4444 size=6
                town "New York" country=us color=#3b82f6
                area "Atlantic" 48.8,-3.7;44.0,-25.0;35.0,-60.0;40.0,-10.0 fill=#93c5fd opacity=0.4
                region europe fill=#fde68a label="EU Zone"
            """;

        var svg = Mermaid.Render(input);

        Assert.That(svg, Does.Contain("<svg"));
        Assert.That(svg, Does.Contain("Geo Coverage"));
        Assert.That(svg, Does.Contain("London"));
        Assert.That(svg, Does.Contain("New York"));
        Assert.That(svg, Does.Contain("EU Zone"));
    }

    [Test]
    public void CountryMap_UsaAliasAndProjection_Renders()
    {
        var input =
            """
            geo
                country us
                projection mercator
                town "Seattle" color=#22c55e
                town "Miami" color=#f97316
            """;

        var svg = Mermaid.Render(input);

        Assert.That(svg, Does.Contain("<svg"));
        Assert.That(svg, Does.Contain("Seattle"));
        Assert.That(svg, Does.Contain("Miami"));
    }

    [Test]
    public void TownOnly_AutoSelectsSingleCountryMap()
    {
        var input =
            """
            geo
                town "London" country=uk
                town "Manchester" country=uk
            """;

        var svg = Mermaid.Render(input);

        Assert.That(svg, Does.Contain("London"));
        Assert.That(svg, Does.Contain("Manchester"));
        Assert.That(svg, Does.Contain("United Kingdom"));
    }

    [Test]
    public void GeoMapRegistry_CanRegisterCustomMapPack()
    {
        const string pluginName = "test-geo-pack";

        Mermaid.RegisterGeoMapPack(new StaticGeoMapPackPlugin(
            name: pluginName,
            maps:
            [
                new GeoMapDefinition(
                    Name: "testmap",
                    Width: 400,
                    Height: 220,
                    MinLongitude: -10,
                    MaxLongitude: 10,
                    MinLatitude: -10,
                    MaxLatitude: 10,
                    Regions:
                    [
                        new GeoRegionDefinition(
                            Id: "main",
                            PathData: "M20 20 L380 20 L380 200 L20 200 Z",
                            LabelPosition: new MermaidSharp.Models.Position(200, 110),
                            Label: "Main")
                    ],
                    Aliases: ["custom-test"],
                    Attribution: "Test pack")
            ]));

        try
        {
            var input =
                """
                geo
                    map custom-test
                    point "Center" 0,0 color=#ef4444
                    area "Core" -4,-4;4,-4;4,4;-4,4 fill=#86efac opacity=0.5
                """;

            var svg = Mermaid.Render(input);
            Assert.That(svg, Does.Contain("Center"));
            Assert.That(svg, Does.Contain("Core"));
            Assert.That(svg, Does.Contain("Test pack"));
        }
        finally
        {
            Mermaid.UnregisterGeoMapPack(pluginName);
        }
    }

    [Test]
    public void DetectDiagramType_Geo_IsRecognized()
    {
        var diagramType = Mermaid.DetectDiagramType(
            """
            geo
                map world
                point "A" 0,0
            """);

        Assert.That(diagramType, Is.EqualTo(DiagramType.Geo));
    }

    [Test]
    public void GeoLocationResolver_CanBePluggedIn()
    {
        const string pluginName = "test-town-resolver";

        Mermaid.RegisterGeoLocationResolver(new StaticGeoLocationResolverPlugin(
            name: pluginName,
            aliases: ["test-town"],
            resolver: (query, _) =>
            {
                if (!string.Equals(query, "Hobbiton", StringComparison.OrdinalIgnoreCase))
                    return (false, new GeoResolvedLocation("", "", 0, 0));

                return (true, new GeoResolvedLocation("Hobbiton", "GB", 51.5, -1.6));
            }));

        try
        {
            var input =
                """
                geo
                    country uk
                    town "Hobbiton" color=#22c55e
                """;

            var svg = Mermaid.Render(input);
            Assert.That(svg, Does.Contain("Hobbiton"));
        }
        finally
        {
            Mermaid.UnregisterGeoLocationResolver(pluginName);
        }
    }
}
