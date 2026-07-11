using MermaidSharp;
using MermaidSharp.Diagrams.C4;

public class C4DiffTests
{
    static C4Model Parse(string input) => Mermaid.ParseAndLayoutC4(input)!.Model;

    [Test]
    public void Added_removed_elements_and_relationships_are_detected()
    {
        var before = Parse(
            """
            C4Context
                System(core, "Core", "Main")
                System(db, "Database", "Storage")
                Rel(core, db, "Writes to")
            """);
        var after = Parse(
            """
            C4Context
                System(core, "Core", "Main")
                Component(filter, "BehaviouralAdmissionFilter", "Novelty gate")
                Rel(core, filter, "Admits via")
            """);

        var delta = C4Diff.Compare(before, after);

        Assert.That(delta.AddedElements.Select(e => e.Id), Does.Contain("filter"));
        Assert.That(delta.RemovedElements.Select(e => e.Id), Does.Contain("db"));
        Assert.That(delta.AddedRelationships, Has.Count.EqualTo(1));   // core -> filter
        Assert.That(delta.RemovedRelationships, Has.Count.EqualTo(1)); // core -> db
        Assert.That(delta.IsEmpty, Is.False);
    }

    [Test]
    public void Identical_models_produce_empty_delta()
    {
        const string m =
            """
            C4Context
                System(a, "A", "desc")
                System(b, "B", "desc")
                Rel(a, b, "uses")
            """;
        var delta = C4Diff.Compare(Parse(m), Parse(m));

        Assert.That(delta.IsEmpty, Is.True);
        Assert.That(C4Diff.FormatImpact(delta), Is.EqualTo("No architectural impact."));
    }

    [Test]
    public void Changed_element_technology_is_reported()
    {
        var before = Parse(
            """
            C4Container
                Container(api, "API", "Node")
            """);
        var after = Parse(
            """
            C4Container
                Container(api, "API", "Go")
            """);

        var delta = C4Diff.Compare(before, after);

        Assert.That(delta.AddedElements, Is.Empty);
        Assert.That(delta.RemovedElements, Is.Empty);
        Assert.That(delta.ChangedElements.Single().ChangedFields, Does.Contain("technology"));
    }

    [Test]
    public void FormatImpact_shows_added_and_removed_lines()
    {
        var before = Parse(
            """
            C4Context
                System(persist, "Direct persistence path", "old")
            """);
        var after = Parse(
            """
            C4Context
                Component(filter, "BehaviouralAdmissionFilter", "Novelty gate")
            """);

        var impact = C4Diff.FormatImpact(C4Diff.Compare(before, after));

        Assert.That(impact, Does.Contain("+ BehaviouralAdmissionFilter"));
        Assert.That(impact, Does.Contain("- Direct persistence path"));
        Assert.That(impact, Does.Contain("Impact:"));
    }
}
