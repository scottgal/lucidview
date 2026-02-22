namespace MermaidSharp.Rendering.Surfaces;

public static class SkiaRenderSurfaceExtensions
{
    public static void RegisterSkiaPlugins(this DiagramRenderSurfaceRegistry registry) =>
        registry.Register(new SkiaDiagramRenderSurfacePlugin());
}
