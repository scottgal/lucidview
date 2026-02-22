using MermaidSharp.Rendering.Skins.Cats;
using MermaidSharp.Rendering.Skins.Showcase;

namespace Naiad.Wasm.Host;

internal static class Program
{
    public static Task Main(string[] args)
    {
        MermaidSkinPacksCatsExtensions.RegisterCatsSkinPack();
        MermaidSkinPacksShowcaseExtensions.RegisterShowcaseSkinPacks();
        return Task.CompletedTask;
    }
}
