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

        return JsonSerializer.Deserialize<RenderOptions>(renderOptionsJson, JsonOptions);
    }
}
