namespace MermaidSharp.Rendering.Surfaces;

public sealed class SvgDiagramRenderSurfacePlugin : IDiagramRenderSurfacePlugin
{
    static readonly IReadOnlyCollection<RenderSurfaceFormat> Formats =
        [RenderSurfaceFormat.Svg];

    public string Name => "svg";
    public IReadOnlyCollection<RenderSurfaceFormat> SupportedFormats => Formats;
    public bool Supports(RenderSurfaceFormat format) => format == RenderSurfaceFormat.Svg;

    public RenderSurfaceOutput Render(RenderSurfaceContext context, RenderSurfaceRequest request)
    {
        var xml = context.SvgDocument.ToXml();
        return RenderSurfaceOutput.FromText(xml, "image/svg+xml");
    }
}
