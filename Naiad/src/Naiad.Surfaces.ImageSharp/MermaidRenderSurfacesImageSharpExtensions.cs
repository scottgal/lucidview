namespace MermaidSharp.Rendering.Surfaces;

public static class MermaidRenderSurfacesImageSharpExtensions
{
    public static void RegisterImageSharpSurface() =>
        MermaidRenderSurfaces.Register(new ImageSharpDiagramRenderSurfacePlugin());
}
