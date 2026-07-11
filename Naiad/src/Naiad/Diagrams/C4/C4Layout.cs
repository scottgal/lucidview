namespace MermaidSharp.Diagrams.C4;

/// <summary>
/// Positioning-only C4 layout: turns a parsed <see cref="C4Model"/> into a fully-placed
/// <see cref="C4LayoutResult"/>. Replicates the grid algorithm the SVG <see cref="C4Renderer"/> uses
/// (group free elements by type into rows of four, then lay out each boundary with its members),
/// but emits geometry instead of drawing — so both the SVG backend and a native canvas can share it.
/// </summary>
public static class C4Layout
{
    const double ElementWidth = 200;
    const double ElementHeight = 90;
    const double PersonHeight = 110;
    const double ElementSpacing = 50;
    const double TitleHeight = 50;
    const double RowSpacing = 60;
    const double BoundaryPadding = 20;
    const double BoundaryTitleHeight = 24;
    const int MaxPerRow = 4;

    public static C4LayoutResult Compute(C4Model model, RenderOptions options)
    {
        if (model.Elements.Count == 0)
            return new C4LayoutResult(model, [], [], [], 200, 100, model.Title);

        var boundaryElementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in model.Boundaries)
            foreach (var id in b.ElementIds)
                boundaryElementIds.Add(id);

        var freeElements = model.Elements.Where(e => !boundaryElementIds.Contains(e.Id)).ToList();

        var persons = freeElements.Where(e => e.Type == C4ElementType.Person).ToList();
        var systems = freeElements.Where(e => e.Type == C4ElementType.System).ToList();
        var containers = freeElements.Where(e =>
            e.Type is C4ElementType.Container or C4ElementType.ContainerDb or C4ElementType.ContainerQueue).ToList();
        var components = freeElements.Where(e => e.Type == C4ElementType.Component).ToList();

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : TitleHeight;

        // ── Overall size (identical accounting to C4Renderer) ──────────────────────────────────
        int RowsFor(int count) => count > 0 ? (int)Math.Ceiling((double)count / MaxPerRow) : 0;
        var totalRows = RowsFor(persons.Count) + RowsFor(systems.Count) +
                        RowsFor(containers.Count) + RowsFor(components.Count);
        foreach (var boundary in model.Boundaries)
            totalRows += RowsFor(boundary.ElementIds.Count);

        var allRowCounts = new List<int>();
        void AddRowCount(int c) { if (c > 0) allRowCounts.Add(Math.Min(c, MaxPerRow)); }
        AddRowCount(persons.Count); AddRowCount(systems.Count);
        AddRowCount(containers.Count); AddRowCount(components.Count);
        foreach (var boundary in model.Boundaries) AddRowCount(boundary.ElementIds.Count);
        var maxCols = allRowCounts.Count > 0 ? allRowCounts.Max() : 1;

        var width = maxCols * (ElementWidth + ElementSpacing) + options.Padding * 2;
        var boundaryExtraHeight = model.Boundaries.Count * (BoundaryPadding * 2 + BoundaryTitleHeight);
        var height = titleOffset + totalRows * (ElementHeight + RowSpacing)
                     + boundaryExtraHeight + options.Padding * 2;

        // ── Placement ──────────────────────────────────────────────────────────────────────────
        var placed = new List<C4PositionedElement>();
        var boundaries = new List<C4PositionedBoundary>();
        var currentY = options.Padding + titleOffset;

        currentY = LayoutRows(persons, currentY, width, placed);
        currentY = LayoutRows(systems, currentY, width, placed);
        currentY = LayoutRows(containers, currentY, width, placed);
        currentY = LayoutRows(components, currentY, width, placed);

        var elementById = model.Elements.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var boundary in model.Boundaries)
        {
            var members = boundary.ElementIds
                .Where(elementById.ContainsKey)
                .Select(id => elementById[id])
                .ToList();
            if (members.Count == 0) continue;

            var boundaryStartY = currentY;
            var contentStartY = currentY + BoundaryPadding + BoundaryTitleHeight;
            var contentEndY = LayoutRows(members, contentStartY, width, placed);
            var boundaryEndY = contentEndY - RowSpacing + BoundaryPadding;

            var memberIds = new HashSet<string>(boundary.ElementIds, StringComparer.OrdinalIgnoreCase);
            var memberPlaced = placed.Where(p => memberIds.Contains(p.Element.Id)).ToList();
            double minX = double.MaxValue, maxX = double.MinValue;
            foreach (var p in memberPlaced)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X + p.Width);
            }
            if (minX == double.MaxValue) { minX = options.Padding; maxX = width - options.Padding; }

            var bx = minX - BoundaryPadding;
            var bw = maxX - minX + BoundaryPadding * 2;
            var bh = boundaryEndY - boundaryStartY;
            boundaries.Add(new C4PositionedBoundary(boundary, bx, boundaryStartY, bw, bh));

            currentY = boundaryEndY + RowSpacing;
        }

        // ── Edges (perimeter endpoints + label anchor, matching C4Renderer.DrawRelationship) ─────
        var byId = placed.ToDictionary(p => p.Element.Id, StringComparer.OrdinalIgnoreCase);
        var edges = new List<C4PositionedEdge>();
        foreach (var rel in model.Relationships)
        {
            if (!byId.TryGetValue(rel.From, out var from) || !byId.TryGetValue(rel.To, out var to))
                continue;
            edges.Add(RouteEdge(rel, from, to));
        }

        return new C4LayoutResult(model, placed, boundaries, edges, width, height, model.Title);
    }

    static double LayoutRows(List<C4Element> elements, double startY, double totalWidth, List<C4PositionedElement> placed)
    {
        var currentY = startY;
        for (var rowStart = 0; rowStart < elements.Count; rowStart += MaxPerRow)
        {
            var row = elements.Skip(rowStart).Take(MaxPerRow).ToList();
            currentY = LayoutRow(row, currentY, totalWidth, placed);
        }
        return currentY;
    }

    static double LayoutRow(List<C4Element> row, double startY, double totalWidth, List<C4PositionedElement> placed)
    {
        if (row.Count == 0) return startY;

        var rowWidth = row.Count * (ElementWidth + ElementSpacing) - ElementSpacing;
        var startX = (totalWidth - rowWidth) / 2;

        for (var i = 0; i < row.Count; i++)
        {
            var element = row[i];
            var x = startX + i * (ElementWidth + ElementSpacing);
            var h = element.Type == C4ElementType.Person ? PersonHeight : ElementHeight;
            placed.Add(new C4PositionedElement(element, x, startY, ElementWidth, h));
        }

        var maxHeight = row.Max(e => e.Type == C4ElementType.Person ? PersonHeight : ElementHeight);
        return startY + maxHeight + RowSpacing;
    }

    static C4PositionedEdge RouteEdge(C4Relationship rel, C4PositionedElement from, C4PositionedElement to)
    {
        double fcx = from.CenterX, fcy = from.CenterY, tcx = to.CenterX, tcy = to.CenterY;
        var dx = tcx - fcx;
        var dy = tcy - fcy;
        var angle = Math.Atan2(dy, dx);

        var fromX = fcx + Math.Cos(angle) * from.Width / 2;
        var fromY = fcy + Math.Sin(angle) * from.Height / 2;
        var toX = tcx - Math.Cos(angle) * to.Width / 2;
        var toY = tcy - Math.Sin(angle) * to.Height / 2;

        const double t = 0.4;
        var labelX = fromX + (toX - fromX) * t;
        var labelY = fromY + (toY - fromY) * t;
        var len = Math.Sqrt(dx * dx + dy * dy);
        labelX += len > 0 ? -dy / len * 14 : 0;
        labelY += len > 0 ? dx / len * 14 : -14;

        return new C4PositionedEdge(rel, fromX, fromY, toX, toY, labelX, labelY);
    }
}
