namespace MermaidSharp.Rendering.Surfaces;

public static class MermaidRenderSurfacesSkiaExtensions
{
    public static void RegisterSkiaSurface() =>
        MermaidRenderSurfaces.Register(new SkiaDiagramRenderSurfacePlugin());
}
