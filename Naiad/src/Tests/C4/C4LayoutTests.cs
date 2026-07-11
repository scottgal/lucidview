using MermaidSharp;
using MermaidSharp.Diagrams.C4;

public class C4LayoutTests
{
    [Test]
    public void Simple_context_places_elements_edges_and_size()
    {
        const string input =
            """
            C4Context
                title System Context
                Person(user, "User", "A user of the system")
                System(system, "System", "The main system")
                Rel(user, system, "Uses")
            """;

        var layout = Mermaid.ParseAndLayoutC4(input);

        Assert.That(layout, Is.Not.Null);
        Assert.That(layout!.Title, Is.EqualTo("System Context"));
        Assert.That(layout.Width, Is.GreaterThan(0));
        Assert.That(layout.Height, Is.GreaterThan(0));
        Assert.That(layout.Elements, Has.Count.EqualTo(2));
        Assert.That(layout.Edges, Has.Count.EqualTo(1));

        foreach (var e in layout.Elements)
        {
            Assert.That(e.Width, Is.GreaterThan(0));
            Assert.That(e.Height, Is.GreaterThan(0));
            Assert.That(e.X, Is.GreaterThanOrEqualTo(0));
            Assert.That(e.Y, Is.GreaterThanOrEqualTo(0));
            Assert.That(e.X + e.Width, Is.LessThanOrEqualTo(layout.Width + 0.01));
        }
    }

    [Test]
    public void Elements_in_a_row_do_not_overlap()
    {
        const string input =
            """
            C4Context
                System(a, "A")
                System(b, "B")
                System(c, "C")
            """;

        var layout = Mermaid.ParseAndLayoutC4(input)!;
        Assert.That(layout.Elements, Has.Count.EqualTo(3));

        var b = layout.Elements;
        for (var i = 0; i < b.Count; i++)
        for (var j = i + 1; j < b.Count; j++)
        {
            var overlap = b[i].X < b[j].X + b[j].Width && b[i].X + b[i].Width > b[j].X &&
                          b[i].Y < b[j].Y + b[j].Height && b[i].Y + b[i].Height > b[j].Y;
            Assert.That(overlap, Is.False, $"elements {i} and {j} overlap");
        }
    }

    [Test]
    public void Edge_endpoints_are_finite_and_distinct()
    {
        const string input =
            """
            C4Context
                System(a, "A")
                System(b, "B")
                Rel(a, b, "calls")
            """;

        var layout = Mermaid.ParseAndLayoutC4(input)!;
        var edge = layout.Edges.Single();

        Assert.That(double.IsFinite(edge.FromX) && double.IsFinite(edge.FromY), Is.True);
        Assert.That(double.IsFinite(edge.ToX) && double.IsFinite(edge.ToY), Is.True);
        Assert.That(Math.Abs(edge.FromY - edge.ToY) + Math.Abs(edge.FromX - edge.ToX), Is.GreaterThan(1));
        Assert.That(edge.Relationship.Label, Is.EqualTo("calls"));
    }

    [Test]
    public void Boundary_members_are_placed_and_a_boundary_box_is_produced()
    {
        const string input =
            """
            C4Container
                title Bank
                System_Boundary(bank, "Internet Banking") {
                    Container(web, "Web App", "React")
                    Container(api, "API", "Node")
                }
            """;

        var layout = Mermaid.ParseAndLayoutC4(input)!;

        Assert.That(layout.Elements, Has.Count.EqualTo(2));
        Assert.That(layout.Boundaries, Has.Count.EqualTo(1));

        var boundary = layout.Boundaries.Single();
        Assert.That(boundary.Width, Is.GreaterThan(0));
        Assert.That(boundary.Height, Is.GreaterThan(0));
        // members sit inside the boundary box horizontally
        foreach (var m in layout.Elements)
        {
            Assert.That(m.X, Is.GreaterThanOrEqualTo(boundary.X - 0.01));
            Assert.That(m.X + m.Width, Is.LessThanOrEqualTo(boundary.X + boundary.Width + 0.01));
        }
    }

    [Test]
    public void Non_c4_input_returns_null()
        => Assert.That(Mermaid.ParseAndLayoutC4("flowchart TD\n A --> B"), Is.Null);

    [Test]
    public void UpdateElementStyle_is_captured_as_per_element_bg_colour()
    {
        const string input =
            """
            C4Context
                System(core, "Core", "Main")
                System(ext, "Ext", "Other")
                UpdateElementStyle(core, $bgColor="#438DD5")
            """;

        var layout = Mermaid.ParseAndLayoutC4(input);

        Assert.That(layout, Is.Not.Null);                       // directive did not break parsing
        Assert.That(layout!.Elements, Has.Count.EqualTo(2));
        var core = layout.Elements.Single(e => e.Element.Id == "core");
        Assert.That(core.Element.BgColor, Is.EqualTo("#438DD5"));
        var ext = layout.Elements.Single(e => e.Element.Id == "ext");
        Assert.That(ext.Element.BgColor, Is.Null);
    }

    [Test]
    public void Unknown_update_directives_do_not_break_parsing()
    {
        const string input =
            """
            C4Context
                System(a, "A", "x")
                System(b, "B", "y")
                Rel(a, b, "uses")
                UpdateRelStyle(a, b, $textColor="red")
                UpdateLayoutConfig($c4ShapeInRow="2")
            """;

        var layout = Mermaid.ParseAndLayoutC4(input);

        Assert.That(layout, Is.Not.Null);
        Assert.That(layout!.Elements, Has.Count.EqualTo(2));
        Assert.That(layout.Edges, Has.Count.EqualTo(1));
    }
}
