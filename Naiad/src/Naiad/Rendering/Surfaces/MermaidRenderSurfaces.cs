namespace MermaidSharp.Rendering.Surfaces;

public static class MermaidRenderSurfaces
{
    static readonly DiagramRenderSurfaceRegistry Registry = CreateDefaultRegistry();

    public static IReadOnlyCollection<RenderSurfaceFormat> SupportedFormats => Registry.SupportedFormats;
    public static IReadOnlyCollection<RenderSurfaceFormat> SupportedFormatsFor(DiagramType diagramType) =>
        Registry.SupportedFormatsFor(diagramType);

    public static IReadOnlyCollection<IDiagramRenderSurfacePlugin> Plugins => Registry.Plugins;

    public static void Register(IDiagramRenderSurfacePlugin plugin) => Registry.Register(plugin);
    public static bool Unregister(string pluginName) => Registry.Unregister(pluginName);
    public static bool Supports(RenderSurfaceFormat format, DiagramType diagramType) =>
        Registry.Supports(format, diagramType);

    public static bool TryRender(
        string mermaidSource,
        RenderSurfaceRequest request,
        out RenderSurfaceOutput? output,
        out string? error,
        RenderOptions? options = null)
    {
        RenderSurfaceFailure? failure;
        var success = TryRender(mermaidSource, request, out output, out failure, options);
        error = failure?.Message;
        return success;
    }

    public static bool TryRender(
        string mermaidSource,
        RenderSurfaceRequest request,
        out RenderSurfaceOutput? output,
        out RenderSurfaceFailure? failure,
        RenderOptions? options = null)
    {
        DiagramType diagramType;
        try
        {
            diagramType = Mermaid.DetectDiagramType(mermaidSource);
        }
        catch (Exception ex)
        {
            output = null;
            failure = new RenderSurfaceFailure(
                "RS100",
                $"Diagram type detection failed: {ex.Message}",
                Metadata: new Dictionary<string, object?>
                {
                    ["requestedFormat"] = request.Format.ToString()
                });
            return false;
        }

        SvgDocument? document;
        try
        {
            document = Mermaid.RenderToDocument(mermaidSource, options);
            if (document is null)
            {
                output = null;
                failure = new RenderSurfaceFailure(
                    "RS101",
                    "Renderer returned null SVG document.",
                    Metadata: new Dictionary<string, object?>
                    {
                        ["requestedFormat"] = request.Format.ToString(),
                        ["diagramType"] = diagramType.ToString()
                    });
                return false;
            }
        }
        catch (Exception ex)
        {
            output = null;
            failure = new RenderSurfaceFailure(
                "RS102",
                $"Diagram rendering failed: {ex.Message}",
                Metadata: new Dictionary<string, object?>
                {
                    ["requestedFormat"] = request.Format.ToString(),
                    ["diagramType"] = diagramType.ToString()
                });
            return false;
        }

        var context = new RenderSurfaceContext(
            mermaidSource,
            diagramType,
            document,
            options);
        return Registry.TryRender(context, request, out output, out failure);
    }

    public static bool TryRender(
        string mermaidSource,
        DiagramType diagramType,
        SvgDocument document,
        RenderSurfaceRequest request,
        out RenderSurfaceOutput? output,
        out string? error,
        RenderOptions? options = null)
    {
        RenderSurfaceFailure? failure;
        var success = TryRender(
            mermaidSource,
            diagramType,
            document,
            request,
            out output,
            out failure,
            options);
        error = failure?.Message;
        return success;
    }

    public static bool TryRender(
        string mermaidSource,
        DiagramType diagramType,
        SvgDocument document,
        RenderSurfaceRequest request,
        out RenderSurfaceOutput? output,
        out RenderSurfaceFailure? failure,
        RenderOptions? options = null)
    {
        var context = new RenderSurfaceContext(
            mermaidSource,
            diagramType,
            document,
            options);
        return Registry.TryRender(context, request, out output, out failure);
    }

    static DiagramRenderSurfaceRegistry CreateDefaultRegistry()
    {
        var registry = new DiagramRenderSurfaceRegistry();
        registry.Register(new SvgDiagramRenderSurfacePlugin());
        registry.Register(new XamlDiagramRenderSurfacePlugin());
        registry.Register(new ReactFlowDiagramRenderSurfacePlugin());
        return registry;
    }
}
