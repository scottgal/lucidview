using static MermaidSharp.Rendering.RenderUtils;

namespace MermaidSharp.Diagrams.Voronoi;

public class VoronoiRenderer : IDiagramRenderer<VoronoiModel>
{
    const double DefaultWidth = 600;
    const double DefaultHeight = 400;
    const double TitleHeight = 30;

    static readonly Random SharedRandom = new();

    public SvgDocument Render(VoronoiModel model, RenderOptions options)
    {
        var theme = DiagramTheme.Resolve(options);

        if (model.Sites.Count < 2)
            return RenderEmpty(options, theme);

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : TitleHeight;

        var builder = new SvgBuilder().Size(DefaultWidth, DefaultHeight);

        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(DefaultWidth / 2, options.Padding + TitleHeight / 2, model.Title,
                anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize + 4}px", fontFamily: options.FontFamily,
                fontWeight: "bold", fill: theme.TextColor);
        }

        var chartX = options.Padding;
        var chartY = options.Padding + titleOffset;
        var chartWidth = DefaultWidth - options.Padding * 2;
        var chartHeight = DefaultHeight - options.Padding * 2 - titleOffset;

        var sites = PositionSites(model, chartX, chartY, chartWidth, chartHeight);

        if (model.ShowCells)
            DrawVoronoiCells(builder, sites, chartX, chartY, chartWidth, chartHeight, theme);

        foreach (var site in sites)
        {
            var sx = site.X ?? 0;
            var sy = site.Y ?? 0;
            builder.AddCircle(sx, sy, 5, fill: theme.TextColor, stroke: theme.Background, strokeWidth: 2);
            builder.AddText(sx, sy - 12, site.Label,
                anchor: "middle", baseline: "bottom",
                fontSize: $"{options.FontSize - 2}px", fontFamily: options.FontFamily,
                fill: theme.TextColor, fontWeight: "bold");
        }

        return builder.Build();
    }

    static SvgDocument RenderEmpty(RenderOptions options, DiagramTheme theme)
    {
        var builder = new SvgBuilder().Size(200, 100);
        builder.AddText(100, 50, "Voronoi\n(requires 2+ sites)",
            anchor: "middle", baseline: "middle",
            fontSize: $"{options.FontSize}px", fontFamily: options.FontFamily,
            fill: theme.MutedText);
        return builder.Build();
    }

    static List<VoronoiSite> PositionSites(VoronoiModel model, double chartX, double chartY, double chartWidth, double chartHeight)
    {
        var sites = new List<VoronoiSite>(model.Sites.Count);
        var total = model.Sites.Count;
        var cols = (int)Math.Ceiling(Math.Sqrt(total));
        var rows = (int)Math.Ceiling((double)total / cols);
        var cellWidth = chartWidth / cols;
        var cellHeight = chartHeight / rows;

        var idx = 0;
        foreach (var site in model.Sites)
        {
            var positionedSite = new VoronoiSite
            {
                Id = site.Id,
                Label = site.Label,
                X = site.X,
                Y = site.Y,
                Weight = site.Weight,
                Color = site.Color
            };

            if (!positionedSite.X.HasValue || !positionedSite.Y.HasValue)
            {
                var row = idx / cols;
                var col = idx % cols;
                var offsetX = (SharedRandom.NextDouble() - 0.5) * cellWidth * 0.6;
                var offsetY = (SharedRandom.NextDouble() - 0.5) * cellHeight * 0.6;
                positionedSite.X = chartX + cellWidth * (col + 0.5) + offsetX;
                positionedSite.Y = chartY + cellHeight * (row + 0.5) + offsetY;
            }

            sites.Add(positionedSite);
            idx++;
        }

        return sites;
    }

    static void DrawVoronoiCells(SvgBuilder builder, List<VoronoiSite> sites, double chartX, double chartY, double chartWidth, double chartHeight, DiagramTheme theme)
    {
        var colors = theme.ChartPalette;
        var resolution = 2;
        var width = (int)(chartWidth / resolution);
        var height = (int)(chartHeight / resolution);
        var cellMap = new int[width, height];
        var siteCount = sites.Count;

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var px = chartX + x * resolution;
                var py = chartY + y * resolution;

                var nearestSite = 0;
                var minDistSq = double.MaxValue;

                for (var s = 0; s < siteCount; s++)
                {
                    var site = sites[s];
                    var dx = px - site.X!.Value;
                    var dy = py - site.Y!.Value;
                    var distSq = dx * dx + dy * dy;
                    if (distSq < minDistSq)
                    {
                        minDistSq = distSq;
                        nearestSite = s;
                    }
                }

                cellMap[x, y] = nearestSite;
            }
        }

        var sb = new System.Text.StringBuilder();
        for (var s = 0; s < siteCount; s++)
        {
            var polygon = TraceCell(cellMap, s, resolution, chartX, chartY, width, height);
            if (polygon.Count > 2)
            {
                var color = sites[s].Color ?? colors[s % colors.Length];
                sb.Clear();
                sb.Append($"M {polygon[0].X} {polygon[0].Y}");
                for (var i = 1; i < polygon.Count; i++)
                    sb.Append($" L {polygon[i].X} {polygon[i].Y}");
                sb.Append(" Z");
                builder.AddPath(sb.ToString(), fill: color, stroke: theme.MutedText, strokeWidth: 1);
            }
        }
    }

    static List<Position> TraceCell(int[,] cellMap, int siteIndex, int resolution, double offsetX, double offsetY, int width, int height)
    {
        var points = new List<Position>();

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                if (cellMap[x, y] != siteIndex) continue;

                var isEdge = x == 0 || x == width - 1 || y == 0 || y == height - 1 ||
                             cellMap[x - 1, y] != siteIndex || cellMap[x + 1, y] != siteIndex ||
                             cellMap[x, y - 1] != siteIndex || cellMap[x, y + 1] != siteIndex;

                if (isEdge)
                    points.Add(new Position(offsetX + x * resolution + resolution / 2, offsetY + y * resolution + resolution / 2));
            }
        }

        return points.Count > 2 ? ConvexHull(points) : points;
    }

    static List<Position> ConvexHull(List<Position> points)
    {
        if (points.Count < 3)
            return points;

        points.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

        var lower = new List<Position>();
        foreach (var p in points)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }

        var upper = new List<Position>();
        for (var i = points.Count - 1; i >= 0; i--)
        {
            var p = points[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    static double Cross(Position o, Position a, Position b) =>
        (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
}
