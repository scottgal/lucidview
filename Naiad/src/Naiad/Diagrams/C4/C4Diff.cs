namespace MermaidSharp.Diagrams.C4;

/// <summary>An element that exists in both models but whose attributes changed.</summary>
public sealed record C4ElementChange(C4Element Before, C4Element After, IReadOnlyList<string> ChangedFields);

/// <summary>
/// The architectural delta between two C4 models: what a proposal adds, removes, or changes.
/// The raw material for the "impact" view — <c>+ Component / − path / Impact: …</c>.
/// </summary>
public sealed record C4Delta(
    IReadOnlyList<C4Element> AddedElements,
    IReadOnlyList<C4Element> RemovedElements,
    IReadOnlyList<C4ElementChange> ChangedElements,
    IReadOnlyList<C4Relationship> AddedRelationships,
    IReadOnlyList<C4Relationship> RemovedRelationships,
    IReadOnlyList<C4Boundary> AddedBoundaries,
    IReadOnlyList<C4Boundary> RemovedBoundaries)
{
    public bool IsEmpty =>
        AddedElements.Count == 0 && RemovedElements.Count == 0 && ChangedElements.Count == 0 &&
        AddedRelationships.Count == 0 && RemovedRelationships.Count == 0 &&
        AddedBoundaries.Count == 0 && RemovedBoundaries.Count == 0;
}

/// <summary>
/// Diffs two parsed C4 models by stable identity (element/boundary id, relationship endpoints) so the
/// architecture panel can show each proposal's impact as it lands. Pure and total.
/// </summary>
public static class C4Diff
{
    public static C4Delta Compare(C4Model before, C4Model after)
    {
        var beforeElems = ById(before.Elements, e => e.Id);
        var afterElems = ById(after.Elements, e => e.Id);

        var added = after.Elements.Where(e => !beforeElems.ContainsKey(e.Id)).ToList();
        var removed = before.Elements.Where(e => !afterElems.ContainsKey(e.Id)).ToList();

        var changed = new List<C4ElementChange>();
        foreach (var a in after.Elements)
        {
            if (!beforeElems.TryGetValue(a.Id, out var b)) continue;
            var fields = ChangedFields(b, a);
            if (fields.Count > 0) changed.Add(new C4ElementChange(b, a, fields));
        }

        var beforeRels = new HashSet<string>(before.Relationships.Select(RelKey), StringComparer.OrdinalIgnoreCase);
        var afterRels = new HashSet<string>(after.Relationships.Select(RelKey), StringComparer.OrdinalIgnoreCase);
        var addedRels = after.Relationships.Where(r => !beforeRels.Contains(RelKey(r))).ToList();
        var removedRels = before.Relationships.Where(r => !afterRels.Contains(RelKey(r))).ToList();

        var beforeBounds = ById(before.Boundaries, b => b.Id);
        var afterBounds = ById(after.Boundaries, b => b.Id);
        var addedBounds = after.Boundaries.Where(b => !beforeBounds.ContainsKey(b.Id)).ToList();
        var removedBounds = before.Boundaries.Where(b => !afterBounds.ContainsKey(b.Id)).ToList();

        return new C4Delta(added, removed, changed, addedRels, removedRels, addedBounds, removedBounds);
    }

    /// <summary>Renders a delta as the human-facing impact block (the <c>+ / − / Impact:</c> view).</summary>
    public static string FormatImpact(C4Delta delta)
    {
        if (delta.IsEmpty) return "No architectural impact.";

        var sb = new System.Text.StringBuilder();
        foreach (var e in delta.AddedElements) sb.AppendLine($"+ {Name(e)}");
        foreach (var b in delta.AddedBoundaries) sb.AppendLine($"+ {b.Label} boundary");
        foreach (var e in delta.RemovedElements) sb.AppendLine($"- {Name(e)}");
        foreach (var b in delta.RemovedBoundaries) sb.AppendLine($"- {b.Label} boundary");
        foreach (var c in delta.ChangedElements) sb.AppendLine($"~ {Name(c.After)} ({string.Join(", ", c.ChangedFields)})");

        sb.AppendLine();
        sb.AppendLine("Impact:");
        if (delta.AddedElements.Count > 0 || delta.RemovedElements.Count > 0 || delta.ChangedElements.Count > 0)
            sb.AppendLine($"• Components: {delta.AddedElements.Count} added, {delta.RemovedElements.Count} removed, {delta.ChangedElements.Count} changed");
        if (delta.AddedRelationships.Count > 0 || delta.RemovedRelationships.Count > 0)
            sb.AppendLine($"• Runtime flow: {delta.AddedRelationships.Count} relationship(s) added, {delta.RemovedRelationships.Count} removed");
        if (delta.AddedBoundaries.Count > 0 || delta.RemovedBoundaries.Count > 0)
            sb.AppendLine($"• Boundaries: {delta.AddedBoundaries.Count} added, {delta.RemovedBoundaries.Count} removed");

        return sb.ToString().TrimEnd();
    }

    static string Name(C4Element e) => string.IsNullOrWhiteSpace(e.Label) ? e.Id : e.Label;

    static string RelKey(C4Relationship r) => $"{r.From}→{r.To}";

    static Dictionary<string, T> ById<T>(IEnumerable<T> items, Func<T, string> id)
    {
        var map = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items) map[id(item)] = item;   // last-wins on dup ids
        return map;
    }

    static IReadOnlyList<string> ChangedFields(C4Element b, C4Element a)
    {
        var fields = new List<string>();
        if (!string.Equals(b.Label, a.Label, StringComparison.Ordinal)) fields.Add("label");
        if (b.Type != a.Type) fields.Add("type");
        if (!string.Equals(b.Technology ?? "", a.Technology ?? "", StringComparison.Ordinal)) fields.Add("technology");
        if (!string.Equals(b.Description ?? "", a.Description ?? "", StringComparison.Ordinal)) fields.Add("description");
        if (b.IsExternal != a.IsExternal) fields.Add("external");
        return fields;
    }
}
