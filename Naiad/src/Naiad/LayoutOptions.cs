namespace MermaidSharp;

public class LayoutOptions
{
    public static LayoutOptions Default => new();

    public Direction Direction { get; set; } = Direction.TopToBottom;
    public double NodeSeparation { get; set; } = 50;
    public double RankSeparation { get; set; } = 50;
    public double EdgeSeparation { get; set; } = 10;
    public double MarginX { get; set; } = 8;
    public double MarginY { get; set; } = 8;
    public RankerType Ranker { get; set; } = RankerType.TightTree;

    /// <summary>
    /// Controls how aggressively edge waypoints are straightened toward the ideal
    /// source→target line after BK coordinate assignment. Range: 0.0 (no straightening,
    /// raw BK output) to 1.0 (force waypoints onto the straight line).
    /// Default 0.7 balances smoothness with BK's ordering constraints.
    /// Set to 0.0 to disable post-processing entirely.
    /// </summary>
    public double EdgeStraighteningStrength { get; set; } = 0.7;
}