using MermaidSharp.Rendering.Skins;

namespace MermaidSharp.Rendering.Skins.Cats;

public static class MermaidSkinPacksCatsExtensions
{
    static readonly IDiagramSkinPackPlugin CatsPlugin = new StaticDiagramSkinPackPlugin(
        name: "cats",
        aliases: ["cat", "kitty", "kitties"],
        shapes: new Dictionary<string, SkinShapeTemplate>(StringComparer.Ordinal)
        {
            ["rectangle"] = new("M10 24L22 8L35 20H65L78 8L90 24V50Q90 60 80 60H20Q10 60 10 50Z", 0, 0, 100, 60),
            ["rounded-rectangle"] = new("M12 26L24 10L36 22H64L76 10L88 26V48Q88 60 76 60H24Q12 60 12 48Z", 0, 0, 100, 60),
            ["stadium"] = new("M14 28L24 12L36 24H64L76 12L86 28Q86 60 50 60Q14 60 14 28Z", 0, 0, 100, 60),
            ["diamond"] = new("M50 0L78 16L100 30L78 44L50 60L22 44L0 30L22 16Z", 0, 0, 100, 60),
            ["hexagon"] = new("M18 8H34L44 20H56L66 8H82L100 30L82 52H66L56 40H44L34 52H18L0 30Z", 0, 0, 100, 60),
            ["cylinder"] = new("M10 20L24 6L36 18H64L76 6L90 20V52A40 8 0 0 1 10 52V20ZM10 20A40 8 0 0 0 90 20", 0, 0, 100, 60),
            ["circle"] = new("M20 24L30 8L40 24A20 20 0 1 1 60 24L70 8L80 24A20 20 0 1 1 20 24Z", 0, 0, 100, 60),
            ["cat-head"] = new("M10 24L22 8L35 20H65L78 8L90 24V50Q90 60 80 60H20Q10 60 10 50Z", 0, 0, 100, 60),
            ["cat-paw"] = new("M50 24A11 11 0 1 1 50 46A11 11 0 1 1 50 24ZM24 22A6 6 0 1 1 24 34A6 6 0 1 1 24 22ZM76 22A6 6 0 1 1 76 34A6 6 0 1 1 76 22ZM38 8A5 5 0 1 1 38 18A5 5 0 1 1 38 8ZM62 8A5 5 0 1 1 62 18A5 5 0 1 1 62 8Z", 0, 0, 100, 60),
            ["cat-fish"] = new("M4 30L20 18H56L70 8V20L96 12V48L70 40V52L56 42H20Z", 0, 0, 100, 60)
        });

    public static void RegisterCatsSkinPack() => MermaidSkinPacks.Register(CatsPlugin);

    public static bool UnregisterCatsSkinPack() => MermaidSkinPacks.Unregister("cats");
}
