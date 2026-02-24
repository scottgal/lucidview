using static MermaidSharp.Rendering.RenderUtils;

namespace MermaidSharp.Diagrams.XYChart;

public class XYChartRenderer : IDiagramRenderer<XYChartModel>
{
    const double ChartWidth = 500;
    const double ChartHeight = 300;
    const double LeftMargin = 60;
    const double RightMargin = 20;
    const double TopMargin = 60;
    const double BottomMargin = 60;
    const double TitleHeight = 30;

    // Bar and line colors are now provided by theme.VividPalette

    public SvgDocument Render(XYChartModel model, RenderOptions options)
    {
        var theme = DiagramTheme.Resolve(options);

        if (model.Series.Count == 0)
        {
            var emptyBuilder = new SvgBuilder().Size(200, 100);
            emptyBuilder.AddText(100, 50, "Empty chart", anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize}px", fontFamily: options.FontFamily, fill: theme.TextColor);
            return emptyBuilder.Build();
        }

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : TitleHeight;
        var width = ChartWidth + LeftMargin + RightMargin + options.Padding * 2;
        var height = ChartHeight + TopMargin + BottomMargin + titleOffset + options.Padding * 2;

        var builder = new SvgBuilder().Size(width, height);

        var chartLeft = options.Padding + LeftMargin;
        var chartTop = options.Padding + titleOffset + TopMargin;
        var chartRight = chartLeft + ChartWidth;
        var chartBottom = chartTop + ChartHeight;

        // Draw title
        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(width / 2, options.Padding + TitleHeight / 2, model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: $"{options.FontSize + 4}px",
                fontFamily: options.FontFamily,
                fontWeight: "bold",
                fill: theme.TextColor);
        }

        // Calculate data range
        var allData = model.Series.SelectMany(s => s.Data).ToList();
        var dataMin = model.YAxisMin ?? (allData.Count > 0 ? allData.Min() : 0);
        var dataMax = model.YAxisMax ?? (allData.Count > 0 ? allData.Max() : 100);
        var dataRange = dataMax - dataMin;
        if (dataRange == 0) dataRange = 1;

        // Calculate category count
        var categoryCount = model.XAxisCategories.Count > 0
            ? model.XAxisCategories.Count
            : model.Series.Count > 0 ? model.Series.Max(s => s.Data.Count) : 1;
        var categoryWidth = ChartWidth / categoryCount;

        // Draw grid lines
        var gridLines = 5;
        for (var i = 0; i <= gridLines; i++)
        {
            var y = chartBottom - ChartHeight * i / gridLines;
            var value = dataMin + dataRange * i / gridLines;

            // Grid line
            builder.AddLine(chartLeft, y, chartRight, y,
                stroke: theme.GridLine, strokeWidth: 1);

            // Y-axis label
            builder.AddText(chartLeft - 10, y, Fmt(value),
                anchor: "end", baseline: "middle",
                fontSize: $"{options.FontSize - 2}px", fontFamily: options.FontFamily,
                fill: theme.MutedText);
        }

        // Draw axes
        builder.AddLine(chartLeft, chartTop, chartLeft, chartBottom,
            stroke: theme.AxisLine, strokeWidth: 2);
        builder.AddLine(chartLeft, chartBottom, chartRight, chartBottom,
            stroke: theme.AxisLine, strokeWidth: 2);

        // Draw X-axis categories
        for (var i = 0; i < categoryCount; i++)
        {
            var x = chartLeft + (i + 0.5) * categoryWidth;
            var label = i < model.XAxisCategories.Count ? model.XAxisCategories[i] : $"{i + 1}";

            builder.AddText(x, chartBottom + 20, label,
                anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize - 2}px", fontFamily: options.FontFamily,
                fill: theme.TextColor);
        }

        // Draw axis labels
        if (!string.IsNullOrEmpty(model.YAxisLabel))
        {
            var labelX = options.Padding + 15;
            var labelY = chartTop + ChartHeight / 2;
            builder.BeginGroup(transform: $"rotate(-90, {Fmt(labelX)}, {Fmt(labelY)})");
            builder.AddText(labelX, labelY, model.YAxisLabel,
                anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize}px", fontFamily: options.FontFamily,
                fill: theme.TextColor);
            builder.EndGroup();
        }

        if (!string.IsNullOrEmpty(model.XAxisLabel))
        {
            builder.AddText(chartLeft + ChartWidth / 2, chartBottom + 45, model.XAxisLabel,
                anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize}px", fontFamily: options.FontFamily,
                fill: theme.TextColor);
        }

        // Draw series
        var barSeriesIndex = 0;
        var lineSeriesIndex = 0;
        var barSeries = model.Series.Where(s => s.Type == ChartSeriesType.Bar).ToList();
        var barWidth = categoryWidth * 0.8 / Math.Max(1, barSeries.Count);

        foreach (var series in model.Series)
        {
            if (series.Type == ChartSeriesType.Bar)
            {
                var color = theme.VividPalette[barSeriesIndex % theme.VividPalette.Length];
                var barOffset = (barSeriesIndex - barSeries.Count / 2.0 + 0.5) * barWidth;

                for (var i = 0; i < series.Data.Count && i < categoryCount; i++)
                {
                    var value = series.Data[i];
                    var barHeight = (value - dataMin) / dataRange * ChartHeight;
                    var x = chartLeft + (i + 0.5) * categoryWidth + barOffset - barWidth / 2;
                    var y = chartBottom - barHeight;

                    builder.AddRect(x, y, barWidth - 2, barHeight,
                        fill: color, stroke: "none", rx: 2);
                }
                barSeriesIndex++;
            }
            else if (series.Type == ChartSeriesType.Line)
            {
                var color = theme.VividPalette[(lineSeriesIndex + 4) % theme.VividPalette.Length];
                var points = new List<(double x, double y)>();

                for (var i = 0; i < series.Data.Count && i < categoryCount; i++)
                {
                    var value = series.Data[i];
                    var x = chartLeft + (i + 0.5) * categoryWidth;
                    var y = chartBottom - (value - dataMin) / dataRange * ChartHeight;
                    points.Add((x, y));
                }

                // Draw line
                if (points.Count >= 2)
                {
                    var pathData = $"M {Fmt(points[0].x)} {Fmt(points[0].y)}";
                    for (var i = 1; i < points.Count; i++)
                    {
                        pathData += $" L {Fmt(points[i].x)} {Fmt(points[i].y)}";
                    }
                    builder.AddPath(pathData, stroke: color, strokeWidth: 2, fill: "none");
                }

                // Draw points
                foreach (var (x, y) in points)
                {
                    builder.AddCircle(x, y, 4, fill: color, stroke: theme.Background, strokeWidth: 2);
                }

                lineSeriesIndex++;
            }
        }

        return builder.Build();
    }

}
