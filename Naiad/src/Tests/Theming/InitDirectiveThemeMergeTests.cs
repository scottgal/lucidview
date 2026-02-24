namespace MermaidSharp.Tests.Theming;

[TestFixture]
public class InitDirectiveThemeMergeTests
{
    [Test]
    public Task ApplyInitDirectives_PreservesHostThemeOverrides_WhenProvided()
    {
        var input =
            """
            %%{init: {"theme":"default","themeVariables":{"primaryTextColor":"#010203","lineColor":"#040506","mainBkg":"#070809","nodeBorder":"#0a0b0c"}}}%%
            flowchart TD
                A[Database] --> B[API]
            """;

        var svg = Mermaid.Render(input, new RenderOptions
        {
            Theme = "dark",
            ThemeColors = new ThemeColorOverrides
            {
                TextColor = "#e6edf3",
                BackgroundColor = "#0d1117",
                NodeFill = "#1c2333",
                NodeStroke = "#7c6bbd",
                EdgeStroke = "#c9d1d9",
                EdgeLabelBackground = "rgba(13,17,23,0.85)"
            }
        });

        var snapshot = $$"""
                         HostTextApplied={{svg.Contains("#e6edf3", StringComparison.OrdinalIgnoreCase)}}
                         HostEdgeApplied={{svg.Contains("#c9d1d9", StringComparison.OrdinalIgnoreCase)}}
                         HostNodeFillApplied={{svg.Contains("#1c2333", StringComparison.OrdinalIgnoreCase)}}
                         InitTextIgnored={{!svg.Contains("#010203", StringComparison.OrdinalIgnoreCase)}}
                         InitEdgeIgnored={{!svg.Contains("#040506", StringComparison.OrdinalIgnoreCase)}}
                         InitNodeIgnored={{!svg.Contains("#070809", StringComparison.OrdinalIgnoreCase)}}
                         """;
        return Verify(snapshot);
    }

    [Test]
    public Task ApplyInitDirectives_AppliesThemeVariables_WhenHostOverridesAreMissing()
    {
        var input =
            """
            %%{init: {"themeVariables":{"primaryTextColor":"#112233","lineColor":"#223344","mainBkg":"#334455","nodeBorder":"#445566"}}}%%
            flowchart TD
                A[Database] --> B[API]
            """;

        var svg = Mermaid.Render(input, new RenderOptions
        {
            Theme = "default"
        });

        var snapshot = $$"""
                         InitTextApplied={{svg.Contains("#112233", StringComparison.OrdinalIgnoreCase)}}
                         InitEdgeApplied={{svg.Contains("#223344", StringComparison.OrdinalIgnoreCase)}}
                         InitNodeFillApplied={{svg.Contains("#334455", StringComparison.OrdinalIgnoreCase)}}
                         InitNodeStrokeApplied={{svg.Contains("#445566", StringComparison.OrdinalIgnoreCase)}}
                         """;
        return Verify(snapshot);
    }
}
