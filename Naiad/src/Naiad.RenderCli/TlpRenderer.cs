using MermaidSharp.Formats;
using System.Text;

namespace Naiad.RenderCli;

public static class TlpRenderer
{
    public static string ConvertToMermaid(TlpGraph graph)
    {
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");

        var labelProp = graph.Properties.GetValueOrDefault("viewLabel");
        var colorProp = graph.Properties.GetValueOrDefault("viewColor");

        foreach (var node in graph.Nodes)
        {
            var label = labelProp?.NodeValues?.GetValueOrDefault(node.Id)?.ToString() ?? $"N{node.Id}";
            var colorRgba = colorProp?.NodeValues?.GetValueOrDefault(node.Id);
            var colorHex = RgbaToHex(colorRgba);
            
            var nodeId = SanitizeId(node.Id);
            var escapedLabel = EscapeLabel(label);
            
            if (!string.IsNullOrEmpty(colorHex))
            {
                sb.AppendLine($"    {nodeId}[\"{escapedLabel}\"]:::n{node.Id}");
            }
            else
            {
                sb.AppendLine($"    {nodeId}[\"{escapedLabel}\"]");
            }
        }

        sb.AppendLine();

        foreach (var edge in graph.Edges)
        {
            var sourceId = SanitizeId(edge.Source);
            var targetId = SanitizeId(edge.Target);
            sb.AppendLine($"    {sourceId} --> {targetId}");
        }

        if (colorProp != null)
        {
            sb.AppendLine();
            foreach (var node in graph.Nodes)
            {
                var colorRgba = colorProp.NodeValues?.GetValueOrDefault(node.Id);
                var colorHex = RgbaToHex(colorRgba);
                if (!string.IsNullOrEmpty(colorHex))
                {
                    sb.AppendLine($"    classDef n{node.Id} fill:{colorHex},stroke:#333,stroke-width:1px,color:#fff");
                }
            }
        }

        return sb.ToString();
    }

    static string SanitizeId(int id) => $"n{id}";

    static string EscapeLabel(string label) =>
        label.Replace("\"", "\\\"")
             .Replace("\n", "\\n")
             .Replace("<", "&lt;")
             .Replace(">", "&gt;");

    static string? RgbaToHex(object? rgba) =>
        rgba is ValueTuple<double, double, double, double> tuple
            ? $"#{(int)tuple.Item1:X2}{(int)tuple.Item2:X2}{(int)tuple.Item3:X2}"
            : null;
}
