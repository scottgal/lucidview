namespace Naiad.Wasm.Host;

internal static class Program
{
    public static Task Main(string[] args)
    {
        RegisterProfilePlugins();
        return Task.CompletedTask;
    }

    static void RegisterProfilePlugins()
    {
#if NAIAD_WASM_COMPLETE
        MermaidSharp.Rendering.Skins.Cats.MermaidSkinPacksCatsExtensions.RegisterCatsSkinPack();
        MermaidSharp.Rendering.Skins.Showcase.MermaidSkinPacksShowcaseExtensions.RegisterShowcaseSkinPacks();
        MermaidSharp.Rendering.Surfaces.MermaidRenderSurfacesImageSharpExtensions.RegisterImageSharpSurface();
#endif
    }
}
