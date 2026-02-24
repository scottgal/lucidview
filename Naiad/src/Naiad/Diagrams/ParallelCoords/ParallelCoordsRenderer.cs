using static MermaidSharp.Rendering.RenderUtils;

namespace MermaidSharp.Diagrams.ParallelCoords;

public class ParallelCoordsRenderer : IDiagramRenderer<ParallelCoordsModel>
{
    const double DefaultWidth = 700;
    const double DefaultHeight = 400;
    const double AxisMargin = 60;
    const double TitleHeight = 30;
    const double LegendWidth = 120;

    public SvgDocument Render(ParallelCoordsModel model, RenderOptions options)
    {
        var theme = DiagramTheme.Resolve(options);

        if (model.Axes.Count < 2)
        {
            return RenderEmpty(options, theme);
        }

        var builder = new SvgBuilder().Size(DefaultWidth, DefaultHeight);

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : TitleHeight;
        var chartX = AxisMargin;
        var chartY = options.Padding + titleOffset;
        var chartWidth = DefaultWidth - AxisMargin * 2 - (model.ShowLegend && model.Datasets.Count > 1 ? LegendWidth : 0);
        var chartHeight = DefaultHeight - options.Padding * 2 - titleOffset;

        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(DefaultWidth / 2, options.Padding + TitleHeight / 2, model.Title,
                anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize + 4}px", fontFamily: options.FontFamily,
                fontWeight: "bold", fill: theme.TextColor);
        }

        var colors = theme.VividPalette;
        var axisSpacing = chartWidth / (model.Axes.Count - 1);

        for (var i = 0; i < model.Axes.Count; i++)
        {
            var axis = model.Axes[i];
            var x = chartX + i * axisSpacing;

            builder.AddLine(x, chartY, x, chartY + chartHeight,
                stroke: theme.GridLine, strokeWidth: 1);

            builder.AddText(x, chartY - 10, axis.Label ?? axis.Id,
                anchor: "middle", baseline: "bottom",
                fontSize: $"{options.FontSize - 2}px", fontFamily: options.FontFamily,
                fill: theme.TextColor);

            var (min, max) = GetAxisRange(model, i);
            var tickCount = 5;

            for (var t = 0; t <= tickCount; t++)
            {
                var tickY = chartY + chartHeight - (t / (double)tickCount) * chartHeight;
                var tickValue = min + (t / (double)tickCount) * (max - min);

                if (t > 0 && t < tickCount)
                {
                    builder.AddLine(x - 3, tickY, x + 3, tickY,
                        stroke: theme.GridLine, strokeWidth: 1);
                }

                builder.AddText(x - 8, tickY, FormatTickValue(tickValue),
                    anchor: "end", baseline: "middle",
                    fontSize: $"{options.FontSize - 4}px", fontFamily: options.FontFamily,
                    fill: theme.MutedText);
            }
        }

        for (var d = 0; d < model.Datasets.Count; d++)
        {
            var dataset = model.Datasets[d];
            var color = dataset.Color ?? colors[d % colors.Length];

            var points = new List<(double X, double Y)>();
            for (var i = 0; i < model.Axes.Count && i < dataset.Values.Count; i++)
            {
                var x = chartX + i * axisSpacing;
                var (min, max) = GetAxisRange(model, i);
                var normalizedValue = (dataset.Values[i] - min) / (max - min);
                normalizedValue = Math.Clamp(normalizedValue, 0, 1);
                var y = chartY + chartHeight - normalizedValue * chartHeight;
                points.Add((x, y));
            }

            if (points.Count > 1)
            {
                var pathData = $"M {points[0].X} {points[0].Y}";
                for (var i = 1; i < points.Count; i++)
                {
                    pathData += $" L {points[i].X} {points[i].Y}";
                }

                builder.AddPath(pathData,
                    stroke: color, strokeWidth: 2, fill: "none",
                    opacity: 0.7);
            }
        }

        if (model.ShowLegend && model.Datasets.Count > 1)
        {
            var legendX = chartX + chartWidth + 20;
            var legendY = chartY + 10;

            for (var d = 0; d < model.Datasets.Count; d++)
            {
                var dataset = model.Datasets[d];
                var color = dataset.Color ?? colors[d % colors.Length];
                var y = legendY + d * 20;

                builder.AddLine(legendX, y + 5, legendX + 20, y + 5,
                    stroke: color, strokeWidth: 2);

                var label = TruncateLabel(dataset.Name, LegendWidth - 30, options.FontSize - 2);
                builder.AddText(legendX + 25, y + 5, label,
                    anchor: "start", baseline: "middle",
                    fontSize: $"{options.FontSize - 2}px", fontFamily: options.FontFamily,
                    fill: theme.TextColor);
            }
        }

        return builder.Build();
    }

    static SvgDocument RenderEmpty(RenderOptions options, DiagramTheme theme)
    {
        var builder = new SvgBuilder().Size(200, 100);
        builder.AddText(100, 50, "Parallel Coordinates\n(requires 2+ axes)",
            anchor: "middle", baseline: "middle",
            fontSize: $"{options.FontSize}px", fontFamily: options.FontFamily,
            fill: theme.MutedText);
        return builder.Build();
    }

    static (double Min, double Max) GetAxisRange(ParallelCoordsModel model, int axisIndex)
    {
        var axis = model.Axes[axisIndex];
        if (axis.Min.HasValue && axis.Max.HasValue)
            return (axis.Min.Value, axis.Max.Value);

        var values = model.Datasets
            .Where(d => d.Values.Count > axisIndex)
            .Select(d => d.Values[axisIndex])
            .ToList();

        if (values.Count == 0)
            return (0, 100);

        var min = values.Min();
        var max = values.Max();
        var padding = (max - min) * 0.1;

        return (min - padding, max + padding);
    }

    static string FormatTickValue(double value)
    {
        if (Math.Abs(value) >= 1000000)
            return (value / 1000000).ToString("0.#") + "M";
        if (Math.Abs(value) >= 1000)
            return (value / 1000).ToString("0.#") + "K";
        if (Math.Abs(value) < 0.01 && value != 0)
            return value.ToString("0.##e-0");
        return value.ToString("0.#");
    }

    static string TruncateLabel(string text, double maxWidth, double fontSize)
    {
        var charWidth = fontSize * 0.6;
        var maxChars = (int)(maxWidth / charWidth);
        if (text.Length <= maxChars)
            return text;
        return maxChars <= 3 ? "" : text[..(maxChars - 2)] + "..";
    }
}
