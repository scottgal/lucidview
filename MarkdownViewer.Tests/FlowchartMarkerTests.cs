using MarkdownViewer.Services;
using Xunit;
using Xunit.Abstractions;

namespace MarkdownViewer.Tests;

public class FlowchartMarkerTests(ITestOutputHelper output)
{
    private readonly MarkdownService _service = new();

    [Fact]
    public void ProcessMarkdown_FlowchartMarker_ContainsExpectedText()
    {
        var content = """
            # Test

            ```mermaid
            flowchart LR
                A[Start] --> B{Decision}
                B -->|Yes| C[OK]
                B -->|No| D[Cancel]
            ```

            After diagram.
            """;

        var processed = _service.ProcessMarkdown(content);

        output.WriteLine("=== Processed markdown ===");
        output.WriteLine(processed);
        output.WriteLine("=== End ===");

        // Show hex of the marker area
        var flowchartIdx = processed.IndexOf("FLOWCHART:", StringComparison.Ordinal);
        if (flowchartIdx >= 0)
        {
            var start = Math.Max(0, flowchartIdx - 10);
            var end = Math.Min(processed.Length, flowchartIdx + 30);
            var snippet = processed[start..end];
            output.WriteLine($"Marker region [{start}..{end}]: \"{snippet}\"");
            output.WriteLine("Hex: " + string.Join(" ", snippet.Select(c => $"U+{(int)c:X4}")));
        }
        else
        {
            output.WriteLine("ERROR: No FLOWCHART: marker found in processed output!");
        }

        // Check layouts stored
        output.WriteLine($"FlowchartLayouts count: {_service.FlowchartLayouts.Count}");
        foreach (var (key, layout) in _service.FlowchartLayouts)
        {
            output.WriteLine($"  Layout key='{key}' nodes={layout.Model.Nodes.Count} size={layout.Width:F0}x{layout.Height:F0}");
        }

        Assert.Contains("FLOWCHART:", processed);
        Assert.True(_service.FlowchartLayouts.Count > 0, "Should have at least one flowchart layout");
    }
}
