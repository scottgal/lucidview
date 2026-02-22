using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using MermaidSharp.Diagrams.Flowchart;

namespace MermaidSharp.Wasm;

public static partial class NaiadWasmExports
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [JSExport]
    public static string RenderSvg(string mermaid, string? renderOptionsJson = null)
    {
        var options = ParseOptions(renderOptionsJson);
        return Mermaid.Render(mermaid, options);
    }

    [JSExport]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "RenderOptions and SvgDocument shapes are known at compile time in this application.")]
    [UnconditionalSuppressMessage("Aot", "IL3050", Justification = "RenderOptions and SvgDocument shapes are known at compile time in this application.")]
    public static string RenderSvgDocumentJson(string mermaid, string? renderOptionsJson = null)
    {
        var options = ParseOptions(renderOptionsJson);
        var doc = Mermaid.RenderToDocument(mermaid, options)
            ?? throw new MermaidException("Rendering returned no document");
        return JsonSerializer.Serialize(doc, JsonOptions);
    }

    [JSExport]
    public static string DetectDiagramType(string mermaid) => Mermaid.DetectDiagramType(mermaid).ToString();

    [JSExport]
    public static string Health() => "ok";

    [JSExport]
    public static string Echo(string value) => value;

    [JSExport]
    public static string DebugFlowchartParse(string mermaid)
    {
        try
        {
            var parser = new FlowchartParser();
            var parsed = parser.Parse(mermaid);
            return parsed.Success
                ? $"ok:{parsed.Value.Nodes.Count}:{parsed.Value.Edges.Count}"
                : $"parse-fail:{parsed.Error}";
        }
        catch (Exception ex)
        {
            return $"ex:{ex.GetType().Name}:{ex.Message}";
        }
    }

    [JSExport]
    public static string DebugFlowchartRender(string mermaid)
    {
        try
        {
            var parser = new FlowchartParser();
            var parsed = parser.Parse(mermaid);
            if (!parsed.Success)
            {
                return $"parse-fail:{parsed.Error}";
            }

            var renderer = new FlowchartRenderer();
            var doc = renderer.Render(parsed.Value, RenderOptions.Default);
            return doc.ToXml().Length.ToString();
        }
        catch (Exception ex)
        {
            return $"ex:{ex.GetType().Name}:{ex.Message}";
        }
    }

    static RenderOptions? ParseOptions(string? renderOptionsJson)
    {
        if (string.IsNullOrWhiteSpace(renderOptionsJson))
        {
            return null;
        }

        if (renderOptionsJson.Length > SecurityValidator.MaxRenderOptionsJsonSize)
        {
            throw new MermaidException($"Render options JSON exceeds {SecurityValidator.MaxRenderOptionsJsonSize} characters.");
        }

        using var doc = JsonDocument.Parse(renderOptionsJson, new JsonDocumentOptions
        {
            MaxDepth = SecurityValidator.MaxRenderOptionsJsonDepth,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new MermaidException("Render options JSON must be an object.");
        }

        var options = new RenderOptions();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            var name = property.Name;
            var value = property.Value;

            switch (name.ToLowerInvariant())
            {
                case "padding":
                    if (TryGetDouble(value, out var padding))
                        options.Padding = padding;
                    break;
                case "theme":
                    if (value.ValueKind == JsonValueKind.String)
                        options.Theme = value.GetString() ?? options.Theme;
                    break;
                case "fontsize":
                    if (TryGetDouble(value, out var fontSize))
                        options.FontSize = fontSize;
                    break;
                case "fontfamily":
                    if (value.ValueKind == JsonValueKind.String)
                        options.FontFamily = value.GetString() ?? options.FontFamily;
                    break;
                case "showboundingbox":
                    if (TryGetBool(value, out var showBounding))
                        options.ShowBoundingBox = showBounding;
                    break;
                case "maxnodes":
                    if (TryGetInt(value, out var maxNodes))
                        options.MaxNodes = maxNodes;
                    break;
                case "maxedges":
                    if (TryGetInt(value, out var maxEdges))
                        options.MaxEdges = maxEdges;
                    break;
                case "maxcomplexity":
                    if (TryGetInt(value, out var maxComplexity))
                        options.MaxComplexity = maxComplexity;
                    break;
                case "maxinputsize":
                    if (TryGetInt(value, out var maxInputSize))
                        options.MaxInputSize = maxInputSize;
                    break;
                case "rendertimeout":
                    if (TryGetInt(value, out var renderTimeout))
                        options.RenderTimeout = renderTimeout;
                    break;
                case "curvededges":
                    if (TryGetBool(value, out var curvedEdges))
                        options.CurvedEdges = curvedEdges;
                    break;
                case "includeexternalresources":
                    if (TryGetBool(value, out var includeExternal))
                        options.IncludeExternalResources = includeExternal;
                    break;
                case "skinpack":
                    if (value.ValueKind == JsonValueKind.String)
                        options.SkinPack = value.GetString();
                    break;
                case "themecolors":
                    var themeColors = ParseThemeColors(value);
                    if (themeColors is not null)
                        options.ThemeColors = themeColors;
                    break;
            }
        }

        SecurityValidator.NormalizeSecurityLimits(options);
        return options;
    }

    static ThemeColorOverrides? ParseThemeColors(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var colors = new ThemeColorOverrides();
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = property.Value.GetString();
            switch (property.Name.ToLowerInvariant())
            {
                case "textcolor":
                    colors.TextColor = value;
                    break;
                case "backgroundcolor":
                    colors.BackgroundColor = value;
                    break;
                case "nodefill":
                    colors.NodeFill = value;
                    break;
                case "nodestroke":
                    colors.NodeStroke = value;
                    break;
                case "edgestroke":
                    colors.EdgeStroke = value;
                    break;
                case "subgraphfill":
                    colors.SubgraphFill = value;
                    break;
                case "subgraphstroke":
                    colors.SubgraphStroke = value;
                    break;
                case "edgelabelbackground":
                    colors.EdgeLabelBackground = value;
                    break;
            }
        }

        return colors;
    }

    static bool TryGetDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDouble(out value);
        }

        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(element.GetString(), out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    static bool TryGetInt(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt32(out value);
        }

        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    static bool TryGetBool(JsonElement element, out bool value)
    {
        if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            bool.TryParse(element.GetString(), out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
