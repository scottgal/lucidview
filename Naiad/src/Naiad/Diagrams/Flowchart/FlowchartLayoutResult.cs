namespace MermaidSharp.Diagrams.Flowchart;

/// <summary>
/// Contains the positioned model and resolved skin from a flowchart layout pass,
/// suitable for rendering by any backend (SVG, Avalonia native controls, etc.).
/// </summary>
public sealed record FlowchartLayoutResult(
    FlowchartModel Model,
    FlowchartSkin Skin,
    double Width,
    double Height,
    bool CurvedEdges);
