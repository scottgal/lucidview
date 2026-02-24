using System.Text;
using MermaidSharp.Diagrams.Flowchart;
using MermaidSharp.Models;

namespace MermaidSharp.Formats;

public class TlpExporter
{
    public static string Export(FlowchartModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"(tlp \"2.3\"");

        if (!string.IsNullOrEmpty(model.Title))
            sb.AppendLine($"  (author \"{EscapeString(model.Title)}\")");

        sb.AppendLine($"  (date \"{DateTime.Now:yyyy-MM-dd}\")");

        var nodeIds = new Dictionary<string, int>();
        var nodeId = 0;
        foreach (var node in model.Nodes)
        {
            nodeIds[node.Id] = nodeId++;
        }

        sb.Append("  (nodes");
        for (var i = 0; i < nodeId; i++)
            sb.Append($" {i}");
        sb.AppendLine(")");

        var edgeId = 0;
        foreach (var edge in model.Edges)
        {
            var srcId = nodeIds.GetValueOrDefault(edge.SourceId, 0);
            var tgtId = nodeIds.GetValueOrDefault(edge.TargetId, 0);
            sb.AppendLine($"  (edge {edgeId} {srcId} {tgtId})");
            edgeId++;
        }

        if (model.Subgraphs.Count > 0)
        {
            var clusterId = 1;
            foreach (var subgraph in model.Subgraphs)
            {
                ExportCluster(sb, subgraph, ref clusterId, nodeIds);
            }
        }

        sb.AppendLine("  (property 0 string \"viewLabel\"");
        sb.AppendLine("    (default \"\" \"\")");
        foreach (var node in model.Nodes)
        {
            if (!string.IsNullOrEmpty(node.Label) && nodeIds.TryGetValue(node.Id, out var id))
                sb.AppendLine($"    (node {id} \"{EscapeString(node.Label)}\")");
        }
        sb.AppendLine("  )");

        sb.AppendLine(")");
        return sb.ToString();
    }

    static void ExportCluster(StringBuilder sb, Subgraph subgraph, ref int clusterId, Dictionary<string, int> nodeIds)
    {
        sb.AppendLine($"  (cluster {clusterId}");
        sb.Append("    (nodes");
        foreach (var nodeId in subgraph.NodeIds)
        {
            if (nodeIds.TryGetValue(nodeId, out var id))
                sb.Append($" {id}");
        }
        sb.AppendLine(")");
        sb.AppendLine("  )");

        sb.AppendLine($"  (graph_attributes {clusterId}");
        sb.AppendLine($"    (string \"name\" \"{EscapeString(subgraph.Title ?? "cluster")}\")");
        sb.AppendLine("  )");

        clusterId++;
    }

    static string EscapeString(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\n", "\\n")
         .Replace("\r", "\\r")
         .Replace("\t", "\\t");
}
