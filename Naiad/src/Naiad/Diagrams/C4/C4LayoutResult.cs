namespace MermaidSharp.Diagrams.C4;

/// <summary>
/// The positioned model from a C4 layout pass, backend-agnostic (SVG, Avalonia native canvas, etc.).
/// Mirrors <see cref="MermaidSharp.Diagrams.Flowchart.FlowchartLayoutResult"/>: geometry is fully
/// resolved so a renderer only has to draw — no layout math at draw time (which lets a native canvas
/// animate/overlay live activity cheaply).
/// </summary>
public sealed record C4LayoutResult(
    C4Model Model,
    IReadOnlyList<C4PositionedElement> Elements,
    IReadOnlyList<C4PositionedBoundary> Boundaries,
    IReadOnlyList<C4PositionedEdge> Edges,
    double Width,
    double Height,
    string? Title);

/// <summary>A C4 element placed by layout. <see cref="X"/>/<see cref="Y"/> are the top-left corner.</summary>
public sealed record C4PositionedElement(
    C4Element Element,
    double X,
    double Y,
    double Width,
    double Height)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

/// <summary>A C4 boundary box placed by layout (dashed container around its members).</summary>
public sealed record C4PositionedBoundary(
    C4Boundary Boundary,
    double X,
    double Y,
    double Width,
    double Height);

/// <summary>
/// A relationship arrow with resolved endpoints (on the box perimeters) and a label anchor point.
/// </summary>
public sealed record C4PositionedEdge(
    C4Relationship Relationship,
    double FromX,
    double FromY,
    double ToX,
    double ToY,
    double LabelX,
    double LabelY);
