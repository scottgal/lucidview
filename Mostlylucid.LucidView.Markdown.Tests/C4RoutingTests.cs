using Mostlylucid.LucidView.Markdown.Services;
using Xunit;

namespace Mostlylucid.LucidView.Markdown.Tests;

public class C4RoutingTests
{
    [Fact]
    public void C4_block_routes_to_native_C4_layout_not_svg()
    {
        var svc = new MarkdownService();
        const string md =
            """
            # Architecture

            ```mermaid
            C4Context
                title System Context
                Person(user, "User", "A user")
                System(system, "System", "Main system")
                Rel(user, system, "Uses")
            ```
            """;

        var (processed, _) = svc.ProcessMarkdownFast(md);

        Assert.Single(svc.C4Layouts);                 // parsed + laid out natively
        Assert.Empty(svc.DiagramDocuments);           // did NOT fall through to the static SVG path
        Assert.Empty(svc.FlowchartLayouts);
        Assert.Contains("C4:", processed);            // native C4 marker inserted (not a DIAGRAM:/SVG marker)

        var layout = svc.C4Layouts.Values.Single();
        Assert.Equal(2, layout.Elements.Count);
        Assert.Single(layout.Edges);
        Assert.Equal("System Context", layout.Title);
    }

    [Fact]
    public void Flowchart_still_routes_to_flowchart_not_c4()
    {
        var svc = new MarkdownService();
        const string md =
            """
            ```mermaid
            flowchart TD
                A --> B
            ```
            """;

        svc.ProcessMarkdownFast(md);

        Assert.Empty(svc.C4Layouts);
        Assert.Single(svc.FlowchartLayouts);
    }
}
