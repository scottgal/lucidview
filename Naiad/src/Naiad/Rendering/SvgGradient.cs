using System.Globalization;
using System.Net;

namespace MermaidSharp.Rendering;

public class SvgGradient
{
    public required string Id { get; init; }
    public List<SvgGradientStop> Stops { get; } = [];
    public bool IsRadial { get; set; }

    // Linear gradient direction (defaults: left-to-right)
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; } = 1;
    public double Y2 { get; set; }

    // Radial gradient center/radius
    public double Cx { get; set; } = 0.5;
    public double Cy { get; set; } = 0.5;
    public double R { get; set; } = 0.5;

    public string ToXml()
    {
        var sb = new StringBuilder();
        if (IsRadial)
        {
            sb.Append($"<radialGradient id=\"{WebUtility.HtmlEncode(Id)}\" cx=\"{Fmt(Cx)}\" cy=\"{Fmt(Cy)}\" r=\"{Fmt(R)}\">");
        }
        else
        {
            sb.Append($"<linearGradient id=\"{WebUtility.HtmlEncode(Id)}\" x1=\"{Fmt(X1)}\" y1=\"{Fmt(Y1)}\" x2=\"{Fmt(X2)}\" y2=\"{Fmt(Y2)}\">");
        }

        foreach (var stop in Stops)
        {
            sb.Append($"<stop offset=\"{stop.Offset}%\" style=\"stop-color:{WebUtility.HtmlEncode(stop.Color)}");
            if (stop.Opacity < 1.0)
                sb.Append($";stop-opacity:{Fmt(stop.Opacity)}");
            sb.Append("\" />");
        }

        sb.Append(IsRadial ? "</radialGradient>" : "</linearGradient>");
        return sb.ToString();
    }

    static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
