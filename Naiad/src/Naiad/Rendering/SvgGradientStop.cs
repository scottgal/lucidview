namespace MermaidSharp.Rendering;

public class SvgGradientStop
{
    public double Offset { get; set; }
    public required string Color { get; init; }
    public double Opacity { get; set; } = 1.0;
}
