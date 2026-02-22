using System.Collections.ObjectModel;

namespace MermaidSharp.Rendering.Surfaces;

public enum RenderSurfaceFormat
{
    Svg,
    Png,
    Pdf,
    Jpeg,
    Webp,
    Xps,
    Xaml,
    ReactFlow,
    Console
}

public sealed record RenderSurfaceRequest(
    RenderSurfaceFormat Format,
    float Scale = 1f,
    int Quality = 100,
    string? Background = null,
    bool EmbedFonts = false);

public sealed record RenderSurfaceOutput(
    byte[]? Bytes,
    string? Text,
    string MimeType)
{
    public static RenderSurfaceOutput FromText(string text, string mimeType) =>
        new(Encoding.UTF8.GetBytes(text), text, mimeType);
}

public sealed record RenderSurfaceContext(
    string MermaidSource,
    DiagramType DiagramType,
    SvgDocument SvgDocument,
    RenderOptions? RenderOptions);

public sealed record RenderSurfaceFailure(
    string Code,
    string Message,
    string? PluginName = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public interface IDiagramRenderSurfacePlugin
{
    string Name { get; }
    IReadOnlyCollection<RenderSurfaceFormat> SupportedFormats { get; }
    IReadOnlyCollection<DiagramType>? SupportedDiagramTypes => null;
    bool Supports(RenderSurfaceFormat format);

    bool Supports(DiagramType diagramType)
    {
        var supportedDiagramTypes = SupportedDiagramTypes;
        return supportedDiagramTypes is null || supportedDiagramTypes.Contains(diagramType);
    }

    bool Supports(RenderSurfaceContext context, RenderSurfaceRequest request) =>
        Supports(request.Format) && Supports(context.DiagramType);

    RenderSurfaceOutput Render(RenderSurfaceContext context, RenderSurfaceRequest request);
}

public sealed class DiagramRenderSurfaceRegistry
{
    readonly object _sync = new();
    readonly List<IDiagramRenderSurfacePlugin> _plugins = [];

    public IReadOnlyCollection<IDiagramRenderSurfacePlugin> Plugins
    {
        get
        {
            lock (_sync)
            {
                return new ReadOnlyCollection<IDiagramRenderSurfacePlugin>([.. _plugins]);
            }
        }
    }

    public IReadOnlyCollection<RenderSurfaceFormat> SupportedFormats
    {
        get
        {
            lock (_sync)
            {
                return new ReadOnlyCollection<RenderSurfaceFormat>(
                [
                    .. _plugins.SelectMany(x => x.SupportedFormats).Distinct()
                ]);
            }
        }
    }

    public IReadOnlyCollection<RenderSurfaceFormat> SupportedFormatsFor(DiagramType diagramType)
    {
        lock (_sync)
        {
            return new ReadOnlyCollection<RenderSurfaceFormat>(
            [
                .. _plugins
                    .Where(x => x.Supports(diagramType))
                    .SelectMany(x => x.SupportedFormats)
                    .Distinct()
            ]);
        }
    }

    public bool Supports(RenderSurfaceFormat format, DiagramType diagramType)
    {
        lock (_sync)
        {
            return _plugins.Any(x => x.Supports(format) && x.Supports(diagramType));
        }
    }

    public void Register(IDiagramRenderSurfacePlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        lock (_sync)
        {
            _plugins.RemoveAll(x => string.Equals(x.Name, plugin.Name, StringComparison.OrdinalIgnoreCase));
            _plugins.Insert(0, plugin);
        }
    }

    public bool Unregister(string pluginName)
    {
        lock (_sync)
        {
            return _plugins.RemoveAll(x => string.Equals(x.Name, pluginName, StringComparison.OrdinalIgnoreCase)) > 0;
        }
    }

    public bool TryRender(
        RenderSurfaceContext context,
        RenderSurfaceRequest request,
        out RenderSurfaceOutput? output,
        out RenderSurfaceFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);

        List<IDiagramRenderSurfacePlugin> plugins;
        lock (_sync)
        {
            plugins = [.. _plugins.Where(x => x.Supports(request.Format))];
        }

        if (plugins.Count == 0)
        {
            output = null;
            failure = new RenderSurfaceFailure(
                "RS001",
                $"No render surface plugin is registered for format '{request.Format}'.",
                Metadata: new Dictionary<string, object?>
                {
                    ["requestedFormat"] = request.Format.ToString(),
                    ["diagramType"] = context.DiagramType.ToString()
                });
            return false;
        }

        var incompatiblePlugins = new List<string>();
        var attemptedPlugins = new List<string>();
        var failureMessages = new List<string>();
        IDiagramRenderSurfacePlugin? firstFailingPlugin = null;
        Exception? firstException = null;

        foreach (var plugin in plugins)
        {
            if (!plugin.Supports(context, request))
            {
                incompatiblePlugins.Add(plugin.Name);
                continue;
            }

            attemptedPlugins.Add(plugin.Name);
            try
            {
                output = plugin.Render(context, request);
                failure = null;
                return true;
            }
            catch (Exception ex)
            {
                firstFailingPlugin ??= plugin;
                firstException ??= ex;
                failureMessages.Add($"{plugin.Name}: {ex.Message}");
            }
        }

        if (firstException is not null)
        {
            output = null;
            failure = new RenderSurfaceFailure(
                "RS500",
                $"Render surface plugin '{firstFailingPlugin!.Name}' failed: {firstException.Message}",
                firstFailingPlugin.Name,
                new Dictionary<string, object?>
                {
                    ["requestedFormat"] = request.Format.ToString(),
                    ["diagramType"] = context.DiagramType.ToString(),
                    ["attemptedPlugins"] = attemptedPlugins.ToArray(),
                    ["errors"] = failureMessages.ToArray()
                });
            return false;
        }

        output = null;
        failure = new RenderSurfaceFailure(
            "RS002",
            $"No render surface plugin can handle format '{request.Format}' for diagram type '{context.DiagramType}'.",
            Metadata: new Dictionary<string, object?>
            {
                ["requestedFormat"] = request.Format.ToString(),
                ["diagramType"] = context.DiagramType.ToString(),
                ["candidatePlugins"] = plugins.Select(x => x.Name).ToArray(),
                ["incompatiblePlugins"] = incompatiblePlugins.ToArray()
            });
        return false;
    }
}
