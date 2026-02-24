using static MermaidSharp.Rendering.RenderUtils;

namespace MermaidSharp.Diagrams.C4;

public class C4Renderer : IDiagramRenderer<C4Model>
{
    const double ElementWidth = 200;
    const double ElementHeight = 90;
    const double PersonHeight = 110;
    const double ElementSpacing = 50;
    const double TitleHeight = 50;
    const double RowSpacing = 60;
    const double BoundaryPadding = 20;
    const double BoundaryTitleHeight = 24;

    public SvgDocument Render(C4Model model, RenderOptions options)
    {
        var theme = DiagramTheme.Resolve(options);
        if (model.Elements.Count == 0)
        {
            var emptyBuilder = new SvgBuilder().Size(200, 100);
            emptyBuilder.AddText(100, 50, "Empty C4 diagram", anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize}px", fontFamily: options.FontFamily);
            return emptyBuilder.Build();
        }

        // Separate elements by boundary membership vs free-standing
        var boundaryElementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in model.Boundaries)
            foreach (var id in b.ElementIds)
                boundaryElementIds.Add(id);

        var freeElements = model.Elements.Where(e => !boundaryElementIds.Contains(e.Id)).ToList();

        // Group free elements by type for layout
        var persons = freeElements.Where(e => e.Type == C4ElementType.Person).ToList();
        var systems = freeElements.Where(e => e.Type == C4ElementType.System).ToList();
        var containers = freeElements.Where(e =>
            e.Type is C4ElementType.Container or C4ElementType.ContainerDb or C4ElementType.ContainerQueue).ToList();
        var components = freeElements.Where(e => e.Type == C4ElementType.Component).ToList();

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : TitleHeight;

        // Calculate layout - account for multi-row categories correctly
        var maxPerRow = 4;
        var personRows = persons.Count > 0 ? (int) Math.Ceiling((double) persons.Count / maxPerRow) : 0;
        var systemRows = systems.Count > 0 ? (int) Math.Ceiling((double) systems.Count / maxPerRow) : 0;
        var containerRows = containers.Count > 0 ? (int) Math.Ceiling((double) containers.Count / maxPerRow) : 0;
        var componentRows = components.Count > 0 ? (int) Math.Ceiling((double) components.Count / maxPerRow) : 0;

        // Count boundary rows: each boundary gets its own row(s) for its elements
        var boundaryRows = 0;
        var boundaryCount = model.Boundaries.Count;
        foreach (var boundary in model.Boundaries)
        {
            var memberCount = boundary.ElementIds.Count;
            boundaryRows += memberCount > 0 ? (int) Math.Ceiling((double) memberCount / maxPerRow) : 0;
        }

        var totalRows = personRows + systemRows + containerRows + componentRows + boundaryRows;

        // Width based on actual max elements in any single row
        var allRowCounts = new List<int>();
        if (persons.Count > 0) allRowCounts.Add(Math.Min(persons.Count, maxPerRow));
        if (systems.Count > 0) allRowCounts.Add(Math.Min(systems.Count, maxPerRow));
        if (containers.Count > 0) allRowCounts.Add(Math.Min(containers.Count, maxPerRow));
        if (components.Count > 0) allRowCounts.Add(Math.Min(components.Count, maxPerRow));
        foreach (var boundary in model.Boundaries)
            if (boundary.ElementIds.Count > 0)
                allRowCounts.Add(Math.Min(boundary.ElementIds.Count, maxPerRow));

        var maxCols = allRowCounts.Count > 0 ? allRowCounts.Max() : 1;

        var width = maxCols * (ElementWidth + ElementSpacing) + options.Padding * 2;
        // Extra height for boundary labels and padding
        var boundaryExtraHeight = boundaryCount * (BoundaryPadding * 2 + BoundaryTitleHeight);
        var height = titleOffset + totalRows * (ElementHeight + RowSpacing)
                     + boundaryExtraHeight + options.Padding * 2;

        var builder = new SvgBuilder().Size(width, height);

        // Add arrow marker
        builder.AddArrowMarker("c4arrow", theme.MutedText);

        // Draw title
        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(width / 2, options.Padding + TitleHeight / 2, model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: $"{options.FontSize + 6}px",
                fontFamily: options.FontFamily,
                fontWeight: "bold");
        }

        // Position tracking
        var elementPositions = new Dictionary<string, (double x, double y, double w, double h)>();
        var currentY = options.Padding + titleOffset;

        // Draw persons
        currentY = DrawElementRows(builder, persons, currentY, width, maxPerRow, options, elementPositions, theme);

        // Draw systems
        currentY = DrawElementRows(builder, systems, currentY, width, maxPerRow, options, elementPositions, theme);

        // Draw containers
        currentY = DrawElementRows(builder, containers, currentY, width, maxPerRow, options, elementPositions, theme);

        // Draw components
        currentY = DrawElementRows(builder, components, currentY, width, maxPerRow, options, elementPositions, theme);

        // Draw boundaries with their contained elements
        var elementById = model.Elements.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var boundary in model.Boundaries)
        {
            var memberElements = boundary.ElementIds
                .Where(id => elementById.ContainsKey(id))
                .Select(id => elementById[id])
                .ToList();
            if (memberElements.Count == 0) continue;

            currentY = DrawBoundary(builder, boundary, memberElements, currentY, width, maxPerRow,
                options, elementPositions, theme);
        }

        // Draw relationships with staggered labels to avoid overlap
        var relIndex = 0;
        foreach (var rel in model.Relationships)
        {
            if (elementPositions.TryGetValue(rel.From, out var fromPos) &&
                elementPositions.TryGetValue(rel.To, out var toPos))
            {
                DrawRelationship(builder, fromPos, toPos, rel.Label, options, theme, relIndex);
                relIndex++;
            }
        }

        // Export hit regions and metadata to the document for interactive use
        foreach (var (id, (cx, cy, w, h)) in elementPositions)
            builder.Document.HitRegions[id] = (cx - w / 2, cy - h / 2, w, h);

        builder.Document.Metadata["c4Type"] = model.Type.ToString();

        foreach (var boundary in model.Boundaries)
            builder.Document.Metadata[$"boundary:{boundary.Id}"] = boundary.Label;

        foreach (var element in model.Elements.Where(e => e.Link is not null))
            builder.Document.Metadata[$"link:{element.Id}"] = element.Link!;

        return builder.Build();
    }

    /// <summary>
    /// Draw elements in rows of up to maxPerRow, handling overflow to multiple rows.
    /// </summary>
    static double DrawElementRows(
        SvgBuilder builder,
        List<C4Element> elements,
        double startY,
        double totalWidth,
        int maxPerRow,
        RenderOptions options,
        Dictionary<string, (double x, double y, double w, double h)> positions,
        DiagramTheme theme)
    {
        if (elements.Count == 0) return startY;

        var currentY = startY;
        for (var rowStart = 0; rowStart < elements.Count; rowStart += maxPerRow)
        {
            var rowElements = elements.Skip(rowStart).Take(maxPerRow).ToList();
            currentY = DrawElementRow(builder, rowElements, currentY, totalWidth, options, positions, theme);
        }

        return currentY;
    }

    static double DrawElementRow(
        SvgBuilder builder,
        List<C4Element> elements,
        double startY,
        double totalWidth,
        RenderOptions options,
        Dictionary<string, (double x, double y, double w, double h)> positions,
        DiagramTheme theme)
    {
        if (elements.Count == 0) return startY;

        var rowWidth = elements.Count * (ElementWidth + ElementSpacing) - ElementSpacing;
        var startX = (totalWidth - rowWidth) / 2;

        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            var x = startX + i * (ElementWidth + ElementSpacing);
            var h = element.Type == C4ElementType.Person ? PersonHeight : ElementHeight;

            positions[element.Id] = (x + ElementWidth / 2, startY + h / 2, ElementWidth, h);

            builder.BeginGroup(id: $"c4-{element.Id}", cssClass: "c4-element");
            DrawElement(builder, element, x, startY, options, theme);
            builder.EndGroup();
        }

        var maxHeight = elements.Max(e => e.Type == C4ElementType.Person ? PersonHeight : ElementHeight);
        return startY + maxHeight + RowSpacing;
    }

    static double DrawBoundary(
        SvgBuilder builder,
        C4Boundary boundary,
        List<C4Element> members,
        double startY,
        double totalWidth,
        int maxPerRow,
        RenderOptions options,
        Dictionary<string, (double x, double y, double w, double h)> positions,
        DiagramTheme theme)
    {
        var boundaryStartY = startY;

        // Reserve space for boundary title
        var contentStartY = startY + BoundaryPadding + BoundaryTitleHeight;

        // Draw elements inside boundary
        var contentEndY = DrawElementRows(builder, members, contentStartY, totalWidth, maxPerRow,
            options, positions, theme);

        var boundaryEndY = contentEndY - RowSpacing + BoundaryPadding;

        // Calculate the horizontal extent of contained elements
        var minX = double.MaxValue;
        var maxX = double.MinValue;
        foreach (var member in members)
        {
            if (positions.TryGetValue(member.Id, out var pos))
            {
                minX = Math.Min(minX, pos.x - pos.w / 2);
                maxX = Math.Max(maxX, pos.x + pos.w / 2);
            }
        }

        // Fallback if no positions found
        if (minX == double.MaxValue)
        {
            minX = options.Padding;
            maxX = totalWidth - options.Padding;
        }

        var boundaryX = minX - BoundaryPadding;
        var boundaryWidth = maxX - minX + BoundaryPadding * 2;
        var boundaryHeight = boundaryEndY - boundaryStartY;

        // Draw boundary box (dashed border)
        builder.AddRect(boundaryX, boundaryStartY, boundaryWidth, boundaryHeight,
            rx: 8,
            fill: "none",
            stroke: theme.MutedText,
            strokeWidth: 1.5,
            style: "stroke-dasharray:8,4");

        // Draw boundary label
        builder.AddText(boundaryX + BoundaryPadding, boundaryStartY + BoundaryPadding + 8,
            boundary.Label,
            anchor: "start",
            baseline: "middle",
            fontSize: $"{options.FontSize - 1}px",
            fontFamily: options.FontFamily,
            fill: theme.MutedText,
            fontWeight: "bold");

        return boundaryEndY + RowSpacing;
    }

    static void DrawElement(SvgBuilder builder, C4Element element, double x, double y, RenderOptions options, DiagramTheme theme)
    {
        var color = GetElementColor(element, theme);
        var textColor = theme.IsDark ? theme.TextColor : "#FFFFFF";

        if (element.Type == C4ElementType.Person)
        {
            // Draw person shape (head + body)
            var headRadius = 18;
            var bodyWidth = ElementWidth - 20;
            var bodyTop = y + headRadius * 2 + 8;
            var bodyHeight = PersonHeight - headRadius * 2 - 8;

            // Head
            builder.AddCircle(x + ElementWidth / 2, y + headRadius + 4, headRadius,
                fill: color, stroke: "none");

            // Body (rounded rect - wide enough to contain text)
            builder.AddRect(x + (ElementWidth - bodyWidth) / 2, bodyTop,
                bodyWidth, bodyHeight, rx: 8,
                fill: color, stroke: "none");

            // Label (centered in body)
            var bodyCenter = bodyTop + bodyHeight / 2;
            var hasDesc = !string.IsNullOrEmpty(element.Description);
            var labelY = hasDesc ? bodyCenter - 9 : bodyCenter;
            builder.AddText(x + ElementWidth / 2, labelY, element.Label,
                anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize - 1}px", fontFamily: options.FontFamily,
                fill: textColor, fontWeight: "bold");

            // Description (inside body - fits wider body)
            if (hasDesc)
            {
                builder.AddText(x + ElementWidth / 2, bodyCenter + 10,
                    TruncateText(element.Description!, 28),
                    anchor: "middle", baseline: "middle",
                    fontSize: $"{options.FontSize - 4}px", fontFamily: options.FontFamily,
                    fill: textColor);
            }
        }
        else if (element.Type == C4ElementType.ContainerDb)
        {
            // Draw database shape (cylinder)
            var ellipseHeight = 15;

            // Top ellipse
            builder.AddEllipse(x + ElementWidth / 2, y + ellipseHeight,
                ElementWidth / 2 - 5, ellipseHeight,
                fill: color, stroke: "none");

            // Body
            builder.AddRect(x + 5, y + ellipseHeight, ElementWidth - 10, ElementHeight - ellipseHeight * 2,
                fill: color, stroke: "none");

            // Bottom ellipse
            builder.AddEllipse(x + ElementWidth / 2, y + ElementHeight - ellipseHeight,
                ElementWidth / 2 - 5, ellipseHeight,
                fill: color, stroke: "none");

            DrawElementText(builder, element, x, y, options, textColor);
        }
        else
        {
            // Standard box
            builder.AddRect(x, y, ElementWidth, ElementHeight, rx: 8,
                fill: color, stroke: "none");

            DrawElementText(builder, element, x, y, options, textColor);
        }
    }

    static void DrawElementText(SvgBuilder builder, C4Element element, double x, double y,
        RenderOptions options, string textColor)
    {
        var centerX = x + ElementWidth / 2;

        // Count text lines to center vertically
        var lineCount = 1; // label
        if (!string.IsNullOrEmpty(element.Technology)) lineCount++;
        if (!string.IsNullOrEmpty(element.Description)) lineCount++;

        var lineSpacing = 17;
        var totalTextHeight = lineCount * lineSpacing;
        var textY = y + (ElementHeight - totalTextHeight) / 2 + lineSpacing / 2.0 + 2;

        // Label
        builder.AddText(centerX, textY, element.Label,
            anchor: "middle", baseline: "middle",
            fontSize: $"{options.FontSize - 1}px", fontFamily: options.FontFamily,
            fill: textColor, fontWeight: "bold");

        // Technology
        if (!string.IsNullOrEmpty(element.Technology))
        {
            textY += lineSpacing;
            builder.AddText(centerX, textY, $"[{element.Technology}]",
                anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize - 3}px", fontFamily: options.FontFamily,
                fill: textColor);
        }

        // Description
        if (!string.IsNullOrEmpty(element.Description))
        {
            textY += lineSpacing;
            builder.AddText(centerX, textY, TruncateText(element.Description, 35),
                anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize - 3}px", fontFamily: options.FontFamily,
                fill: textColor);
        }
    }

    static void DrawRelationship(SvgBuilder builder,
        (double x, double y, double w, double h) from,
        (double x, double y, double w, double h) to,
        string? label, RenderOptions options, DiagramTheme theme, int index = 0)
    {
        // Calculate connection points
        var dx = to.x - from.x;
        var dy = to.y - from.y;
        var angle = Math.Atan2(dy, dx);

        var fromX = from.x + Math.Cos(angle) * from.w / 2;
        var fromY = from.y + Math.Sin(angle) * from.h / 2;
        var toX = to.x - Math.Cos(angle) * to.w / 2;
        var toY = to.y - Math.Sin(angle) * to.h / 2;

        // Draw line
        builder.AddLine(fromX, fromY, toX, toY,
            stroke: theme.MutedText, strokeWidth: 1.5, strokeDasharray: "5,5");

        // Draw arrowhead manually
        var arrowSize = 8;
        var arrowAngle = Math.PI / 6;
        var ax1 = toX - arrowSize * Math.Cos(angle - arrowAngle);
        var ay1 = toY - arrowSize * Math.Sin(angle - arrowAngle);
        var ax2 = toX - arrowSize * Math.Cos(angle + arrowAngle);
        var ay2 = toY - arrowSize * Math.Sin(angle + arrowAngle);

        builder.AddPath($"M {Fmt(toX)} {Fmt(toY)} L {Fmt(ax1)} {Fmt(ay1)} L {Fmt(ax2)} {Fmt(ay2)} Z",
            fill: theme.MutedText, stroke: "none");

        // Draw label between the elements, offset to avoid overlap
        if (!string.IsNullOrEmpty(label))
        {
            // Place label at 40% along the line (closer to source) to reduce overlap at targets
            var t = 0.4;
            var labelX = fromX + (toX - fromX) * t;
            var labelY = fromY + (toY - fromY) * t;

            // Offset perpendicular to the line
            var len = Math.Sqrt(dx * dx + dy * dy);
            var perpX = len > 0 ? -dy / len * 14 : 0;
            var perpY = len > 0 ? dx / len * 14 : -14;

            builder.AddText(labelX + perpX, labelY + perpY, label,
                anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize - 3}px", fontFamily: options.FontFamily,
                fill: theme.MutedText);
        }
    }

    static string GetElementColor(C4Element element, DiagramTheme theme)
    {
        if (element.IsExternal)
        {
            return theme.MutedText;
        }

        return element.Type switch
        {
            C4ElementType.Person => theme.TertiaryStroke,
            C4ElementType.System => theme.SecondaryStroke,
            C4ElementType.Container => theme.PrimaryStroke,
            C4ElementType.ContainerDb => theme.PrimaryStroke,
            C4ElementType.ContainerQueue => theme.PrimaryStroke,
            C4ElementType.Component => theme.TertiaryFill,
            _ => theme.SecondaryStroke
        };
    }

    static string TruncateText(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, maxLength - 3), "...");
    }

}
