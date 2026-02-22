using MermaidSharp.Rendering.Skins;

namespace MermaidSharp.Rendering.Skins.Showcase;

public static class MermaidSkinPacksShowcaseExtensions
{
    static readonly IReadOnlyDictionary<string, string> BaseFlowchartShapes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rectangle"] = "M8 0H92Q100 0 100 8V52Q100 60 92 60H8Q0 60 0 52V8Q0 0 8 0Z",
            ["rounded-rectangle"] = "M12 0H88Q100 0 100 12V48Q100 60 88 60H12Q0 60 0 48V12Q0 0 12 0Z",
            ["stadium"] = "M30 0H70Q100 0 100 30Q100 60 70 60H30Q0 60 0 30Q0 0 30 0Z",
            ["diamond"] = "M50 0L100 30L50 60L0 30Z",
            ["hexagon"] = "M16 0H84L100 30L84 60H16L0 30Z",
            ["cylinder"] = "M0 7A50 7 0 0 1 100 7V53A50 7 0 0 1 0 53V7ZM0 7A50 7 0 0 0 100 7",
            ["circle"] = "M50 0A30 30 0 1 1 50 60A30 30 0 1 1 50 0Z"
        };

    static readonly IDiagramSkinPackPlugin Prism3dPlugin = new StaticDiagramSkinPackPlugin(
        name: "prism3d",
        aliases: ["3d", "prism", "prism-3d"],
        shapes: BuildPack(BuildPrism3dTemplate));

    static readonly IDiagramSkinPackPlugin NeonPlugin = new StaticDiagramSkinPackPlugin(
        name: "neon",
        aliases: ["cyber", "neon-city"],
        shapes: BuildPack(BuildNeonTemplate));

    static readonly IDiagramSkinPackPlugin SunsetPlugin = new StaticDiagramSkinPackPlugin(
        name: "sunset",
        aliases: ["vibrant", "warm"],
        shapes: BuildPack(BuildSunsetTemplate));

    public static void RegisterShowcaseSkinPacks()
    {
        MermaidSkinPacks.Register(Prism3dPlugin);
        MermaidSkinPacks.Register(NeonPlugin);
        MermaidSkinPacks.Register(SunsetPlugin);
    }

    public static bool UnregisterPrism3dSkinPack() => MermaidSkinPacks.Unregister("prism3d");
    public static bool UnregisterNeonSkinPack() => MermaidSkinPacks.Unregister("neon");
    public static bool UnregisterSunsetSkinPack() => MermaidSkinPacks.Unregister("sunset");

    public static void UnregisterShowcaseSkinPacks()
    {
        UnregisterPrism3dSkinPack();
        UnregisterNeonSkinPack();
        UnregisterSunsetSkinPack();
    }

    static IReadOnlyDictionary<string, SkinShapeTemplate> BuildPack(
        Func<string, string, SkinShapeTemplate> factory)
    {
        var shapes = new Dictionary<string, SkinShapeTemplate>(StringComparer.Ordinal);
        foreach (var shape in BaseFlowchartShapes)
            shapes[shape.Key] = factory(shape.Key, shape.Value);
        return shapes;
    }

    static SkinShapeTemplate BuildPrism3dTemplate(string _, string pathData)
    {
        var defs = $$"""
                     <linearGradient id="prism-grad" x1="0%" y1="0%" x2="100%" y2="100%">
                       <stop offset="0%" stop-color="#DBEAFE" stop-opacity="1"/>
                       <stop offset="48%" stop-color="#60A5FA" stop-opacity="1"/>
                       <stop offset="100%" stop-color="#1D4ED8" stop-opacity="1"/>
                     </linearGradient>
                     <linearGradient id="prism-top-sheen" x1="0%" y1="0%" x2="0%" y2="100%">
                       <stop offset="0%" stop-color="#FFFFFF" stop-opacity="0.72"/>
                       <stop offset="100%" stop-color="#FFFFFF" stop-opacity="0"/>
                     </linearGradient>
                     <filter id="prism-shadow" x="-20%" y="-20%" width="140%" height="140%">
                       <feDropShadow dx="0" dy="1.4" stdDeviation="1.3" flood-color="#0F172A" flood-opacity="0.32"/>
                     </filter>
                     <clipPath id="prism-clip"><path d="{{pathData}}"/></clipPath>
                     """;

        var layers = new[]
        {
            new SkinPathLayerTemplate(
                pathData,
                "fill:#0B1220;stroke:#1E3A8A;stroke-width:1.8;stroke-linejoin:round;stroke-linecap:round;filter:url(#prism-shadow)"),
            new SkinPathLayerTemplate(
                pathData,
                "fill:url(#prism-grad);opacity:1;stroke:none"),
            new SkinPathLayerTemplate(
                "M0 0H100V24H0Z",
                "fill:url(#prism-top-sheen);clip-path:url(#prism-clip)"),
            new SkinPathLayerTemplate(
                "M66 0H100V60H66Z",
                "fill:rgba(15,23,42,0.2);clip-path:url(#prism-clip)")
        };

        return new SkinShapeTemplate(
            pathData,
            0,
            0,
            100,
            60,
            null,
            layers,
            defs);
    }

    static SkinShapeTemplate BuildNeonTemplate(string _, string pathData)
    {
        var defs = $$"""
                     <radialGradient id="neon-grad" cx="50%" cy="32%" r="70%">
                       <stop offset="0%" stop-color="#F0ABFC" stop-opacity="1"/>
                       <stop offset="42%" stop-color="#C084FC" stop-opacity="0.98"/>
                       <stop offset="100%" stop-color="#06B6D4" stop-opacity="0.96"/>
                     </radialGradient>
                     <linearGradient id="neon-edge-glint" x1="0%" y1="0%" x2="100%" y2="0%">
                       <stop offset="0%" stop-color="#22D3EE" stop-opacity="0.08"/>
                       <stop offset="50%" stop-color="#FFFFFF" stop-opacity="0.42"/>
                       <stop offset="100%" stop-color="#22D3EE" stop-opacity="0.08"/>
                     </linearGradient>
                     <filter id="neon-glow" x="-30%" y="-30%" width="160%" height="160%">
                       <feGaussianBlur stdDeviation="1.6" result="neonBlur"/>
                       <feMerge>
                         <feMergeNode in="neonBlur"/>
                         <feMergeNode in="SourceGraphic"/>
                       </feMerge>
                     </filter>
                     <clipPath id="neon-clip"><path d="{{pathData}}"/></clipPath>
                     """;

        var layers = new[]
        {
            new SkinPathLayerTemplate(
                pathData,
                "fill:#050B1B;stroke:#22D3EE;stroke-width:2;stroke-linejoin:round;stroke-linecap:round"),
            new SkinPathLayerTemplate(
                pathData,
                "fill:url(#neon-grad);opacity:0.97;stroke:none;filter:url(#neon-glow)"),
            new SkinPathLayerTemplate(
                pathData,
                "fill:none;stroke:url(#neon-edge-glint);stroke-width:1"),
            new SkinPathLayerTemplate(
                "M0 0H100V18H0Z",
                "fill:rgba(255,255,255,0.16);clip-path:url(#neon-clip)")
        };

        return new SkinShapeTemplate(
            pathData,
            0,
            0,
            100,
            60,
            null,
            layers,
            defs);
    }

    static SkinShapeTemplate BuildSunsetTemplate(string _, string pathData)
    {
        var defs = $$"""
                     <linearGradient id="sunset-grad" x1="0%" y1="0%" x2="100%" y2="100%">
                       <stop offset="0%" stop-color="#FED7AA" stop-opacity="1"/>
                       <stop offset="34%" stop-color="#FB7185" stop-opacity="0.99"/>
                       <stop offset="70%" stop-color="#F97316" stop-opacity="0.98"/>
                       <stop offset="100%" stop-color="#C2410C" stop-opacity="0.98"/>
                     </linearGradient>
                     <linearGradient id="sunset-top-glow" x1="0%" y1="0%" x2="0%" y2="100%">
                       <stop offset="0%" stop-color="#FFF7ED" stop-opacity="0.6"/>
                       <stop offset="100%" stop-color="#FFF7ED" stop-opacity="0"/>
                     </linearGradient>
                     <filter id="sunset-shadow" x="-20%" y="-20%" width="140%" height="140%">
                       <feDropShadow dx="0" dy="1.2" stdDeviation="1.1" flood-color="#7C2D12" flood-opacity="0.3"/>
                     </filter>
                     <clipPath id="sunset-clip"><path d="{{pathData}}"/></clipPath>
                     """;

        var layers = new[]
        {
            new SkinPathLayerTemplate(
                pathData,
                "fill:#7C2D12;stroke:#7C2D12;stroke-width:1.8;stroke-linejoin:round;stroke-linecap:round;filter:url(#sunset-shadow)"),
            new SkinPathLayerTemplate(
                pathData,
                "fill:url(#sunset-grad);opacity:1;stroke:none"),
            new SkinPathLayerTemplate(
                "M0 0H100V22H0Z",
                "fill:url(#sunset-top-glow);clip-path:url(#sunset-clip)"),
            new SkinPathLayerTemplate(
                "M0 46H100V60H0Z",
                "fill:rgba(124,45,18,0.22);clip-path:url(#sunset-clip)")
        };

        return new SkinShapeTemplate(
            pathData,
            0,
            0,
            100,
            60,
            null,
            layers,
            defs);
    }
}
