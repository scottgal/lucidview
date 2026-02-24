using static MermaidSharp.Rendering.RenderUtils;

namespace MermaidSharp.Diagrams.Dendrogram;

public class DendrogramRenderer : IDiagramRenderer<DendrogramModel>
{
    const double DefaultWidth = 600;
    const double DefaultHeight = 400;
    const double TitleHeight = 30;
    const double LeafLabelWidth = 80;
    const double NodeRadius = 4;

    public SvgDocument Render(DendrogramModel model, RenderOptions options)
    {
        if (model.Leaves.Count == 0)
        {
            return RenderEmpty(options);
        }

        var builder = new SvgBuilder().Size(DefaultWidth, DefaultHeight);

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : TitleHeight;

        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(DefaultWidth / 2, options.Padding + TitleHeight / 2, model.Title,
                anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize + 4}px", fontFamily: options.FontFamily,
                fontWeight: "bold");
        }

        var chartX = options.Padding + (model.Horizontal ? 0 : LeafLabelWidth);
        var chartY = options.Padding + titleOffset;
        var chartWidth = DefaultWidth - options.Padding * 2 - (model.Horizontal ? 0 : LeafLabelWidth);
        var chartHeight = DefaultHeight - options.Padding * 2 - titleOffset;

        var positions = CalculatePositions(model, chartX, chartY, chartWidth, chartHeight);

        DrawDendrogram(builder, model, positions, options);

        foreach (var leaf in model.Leaves)
        {
            if (positions.TryGetValue(leaf.Id, out var pos))
            {
                if (model.Horizontal)
                {
                    builder.AddText(pos.X + 8, pos.Y, leaf.Label,
                        anchor: "start", baseline: "middle",
                        fontSize: $"{options.FontSize - 2}px", fontFamily: options.FontFamily);
                }
                else
                {
                    builder.AddText(chartX - 5, pos.Y, leaf.Label,
                        anchor: "end", baseline: "middle",
                        fontSize: $"{options.FontSize - 2}px", fontFamily: options.FontFamily);
                }
            }
        }

        return builder.Build();
    }

    static SvgDocument RenderEmpty(RenderOptions options)
    {
        var builder = new SvgBuilder().Size(200, 100);
        builder.AddText(100, 50, "Dendrogram\n(requires leaf nodes)",
            anchor: "middle", baseline: "middle",
            fontSize: $"{options.FontSize}px", fontFamily: options.FontFamily,
            fill: "#999");
        return builder.Build();
    }

    static Dictionary<string, (double X, double Y, double Height)> CalculatePositions(
        DendrogramModel model, double chartX, double chartY, double chartWidth, double chartHeight)
    {
        var positions = new Dictionary<string, (double X, double Y, double Height)>();
        var maxHeight = model.Merges.Count > 0 ? model.Merges.Max(m => m.Height) : 1;

        if (model.Horizontal)
        {
            var leafSpacing = chartHeight / (model.Leaves.Count + 1);
            for (var i = 0; i < model.Leaves.Count; i++)
            {
                var leaf = model.Leaves[i];
                positions[leaf.Id] = (chartX, chartY + (i + 1) * leafSpacing, 0);
            }

            foreach (var merge in model.Merges)
            {
                var (leftX, leftY, _) = positions.GetValueOrDefault(merge.Left, (chartX, chartY, 0.0));
                var (rightX, rightY, _) = positions.GetValueOrDefault(merge.Right, (chartX, chartY, 0.0));

                var x = chartX + (merge.Height / maxHeight) * chartWidth;
                var y = (leftY + rightY) / 2;

                positions[$"{merge.Left}{merge.Right}"] = (x, y, merge.Height);
            }
        }
        else
        {
            var leafSpacing = chartWidth / (model.Leaves.Count + 1);
            for (var i = 0; i < model.Leaves.Count; i++)
            {
                var leaf = model.Leaves[i];
                positions[leaf.Id] = (chartX + (i + 1) * leafSpacing, chartY + chartHeight, 0);
            }

            foreach (var merge in model.Merges)
            {
                var (leftX, leftY, _) = positions.GetValueOrDefault(merge.Left, (chartX, chartY + chartHeight, 0.0));
                var (rightX, rightY, _) = positions.GetValueOrDefault(merge.Right, (chartX, chartY + chartHeight, 0.0));

                var x = (leftX + rightX) / 2;
                var y = chartY + chartHeight - (merge.Height / maxHeight) * chartHeight;

                positions[$"{merge.Left}{merge.Right}"] = (x, y, merge.Height);
            }
        }

        return positions;
    }

    static void DrawDendrogram(SvgBuilder builder, DendrogramModel model,
        Dictionary<string, (double X, double Y, double Height)> positions, RenderOptions options)
    {
        foreach (var merge in model.Merges)
        {
            var (leftX, leftY, _) = positions.GetValueOrDefault(merge.Left, (0.0, 0.0, 0.0));
            var (rightX, rightY, _) = positions.GetValueOrDefault(merge.Right, (0.0, 0.0, 0.0));
            var mergeKey = $"{merge.Left}{merge.Right}";
            var (mergeX, mergeY, _) = positions.GetValueOrDefault(mergeKey, (0.0, 0.0, 0.0));

            if (model.Horizontal)
            {
                builder.AddLine(leftX, leftY, mergeX, leftY,
                    stroke: "#666", strokeWidth: 1.5);
                builder.AddLine(rightX, rightY, mergeX, rightY,
                    stroke: "#666", strokeWidth: 1.5);
                builder.AddLine(mergeX, leftY, mergeX, rightY,
                    stroke: "#666", strokeWidth: 1.5);
            }
            else
            {
                builder.AddLine(leftX, leftY, leftX, mergeY,
                    stroke: "#666", strokeWidth: 1.5);
                builder.AddLine(rightX, rightY, rightX, mergeY,
                    stroke: "#666", strokeWidth: 1.5);
                builder.AddLine(leftX, mergeY, rightX, mergeY,
                    stroke: "#666", strokeWidth: 1.5);
            }

            builder.AddCircle(mergeX, mergeY, NodeRadius,
                fill: "#4a90d9", stroke: "#2c5282", strokeWidth: 1);
        }

        foreach (var leaf in model.Leaves)
        {
            if (positions.TryGetValue(leaf.Id, out var pos))
            {
                builder.AddCircle(pos.X, pos.Y, NodeRadius,
                    fill: "#68d391", stroke: "#2f855a", strokeWidth: 1);
            }
        }
    }
}
