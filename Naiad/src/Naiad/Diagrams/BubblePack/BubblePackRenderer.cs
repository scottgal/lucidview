using static MermaidSharp.Rendering.RenderUtils;

namespace MermaidSharp.Diagrams.BubblePack;

public class BubblePackRenderer : IDiagramRenderer<BubblePackModel>
{
    const double DefaultSize = 600;
    const double TitleHeight = 30;
    const double Padding = 20;

    public SvgDocument Render(BubblePackModel model, RenderOptions options)
    {
        var theme = DiagramTheme.Resolve(options);

        if (model.RootNodes.Count == 0)
            return RenderEmpty(options, theme);

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : TitleHeight;
        var size = DefaultSize;

        var builder = new SvgBuilder().Size(size, size + titleOffset);

        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(size / 2, options.Padding + TitleHeight / 2, model.Title,
                anchor: "middle", baseline: "middle",
                fontSize: $"{options.FontSize + 4}px", fontFamily: options.FontFamily,
                fontWeight: "bold", fill: theme.TextColor);
        }

        var centerX = size / 2;
        var centerY = size / 2 + titleOffset;
        var maxRadius = (size - Padding * 2) / 2;

        var colorIndex = 0;
        foreach (var root in model.RootNodes)
        {
            PackCircles(root, centerX, centerY, maxRadius);
            DrawBubble(builder, root, ref colorIndex, options, theme);
        }

        return builder.Build();
    }

    static SvgDocument RenderEmpty(RenderOptions options, DiagramTheme theme)
    {
        var builder = new SvgBuilder().Size(200, 100);
        builder.AddText(100, 50, "Bubble Pack\n(empty)",
            anchor: "middle", baseline: "middle",
            fontSize: $"{options.FontSize}px", fontFamily: options.FontFamily,
            fill: theme.MutedText);
        return builder.Build();
    }

    static void PackCircles(BubbleNode node, double cx, double cy, double radius)
    {
        node.X = cx;
        node.Y = cy;
        node.Radius = radius;

        if (node.Children.Count == 0)
            return;

        var totalValue = node.TotalValue;
        if (totalValue <= 0)
            return;

        var childRadiusScale = radius * 0.9;
        var maxRadiusForChild = radius - Padding;
        var positions = new List<(BubbleNode child, double x, double y, double r)>(node.Children.Count);

        node.Children.Sort((a, b) => b.TotalValue.CompareTo(a.TotalValue));

        foreach (var child in node.Children)
        {
            var childRadius = Math.Sqrt(child.TotalValue / totalValue) * childRadiusScale;
            if (childRadius < 5) childRadius = 5;

            var (x, y) = FindPosition(positions, cx, cy, maxRadiusForChild, childRadius);

            positions.Add((child, x, y, childRadius));
            PackCircles(child, x, y, childRadius);
        }
    }

    static (double x, double y) FindPosition(
        List<(BubbleNode child, double x, double y, double r)> placed,
        double cx, double cy, double maxRadius, double newRadius)
    {
        if (placed.Count == 0)
            return (cx, cy);

        var angle = 0.0;
        var angleStep = Math.PI / 18;
        var distance = newRadius + 5;
        var newRadiusPlus2 = newRadius + 2;
        var limit = maxRadius - newRadius;

        while (distance < limit)
        {
            for (var i = 0; i < 36; i++)
            {
                var x = cx + Math.Cos(angle) * distance;
                var y = cy + Math.Sin(angle) * distance;

                if (!Overlaps(placed, x, y, newRadiusPlus2))
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var distFromCenter = Math.Sqrt(dx * dx + dy * dy);
                    if (distFromCenter + newRadius <= maxRadius)
                        return (x, y);
                }

                angle += angleStep;
            }

            distance += newRadius / 2;
        }

        return (cx + (maxRadius - newRadius) * 0.5, cy);
    }

    static bool Overlaps(
        List<(BubbleNode child, double x, double y, double r)> placed,
        double x, double y, double minDist)
    {
        foreach (var (_, px, py, pr) in placed)
        {
            var dx = x - px;
            var dy = y - py;
            var distSq = dx * dx + dy * dy;
            var minDistTotal = minDist + pr;
            if (distSq < minDistTotal * minDistTotal)
                return true;
        }
        return false;
    }

    static void DrawBubble(SvgBuilder builder, BubbleNode node, ref int colorIndex, RenderOptions options, DiagramTheme theme, int depth = 0)
    {
        var colors = theme.VividPalette;
        var color = node.Color ?? colors[colorIndex % colors.Length];
        colorIndex++;

        var adjustedColor = AdjustOpacity(color, node.IsLeaf ? 0.8 : 0.3 + (depth * 0.1));

        builder.AddCircle(node.X, node.Y, node.Radius,
            fill: adjustedColor, stroke: DarkenColor(color, 0.2), strokeWidth: node.IsLeaf ? 2 : 1);

        if (node.Radius > 20)
        {
            var fontSize = Math.Clamp(node.Radius / 5, 10, options.FontSize);
            var label = TruncateLabel(node.Label, node.Radius * 1.6, fontSize);

            builder.AddText(node.X, node.Y, label,
                anchor: "middle", baseline: "middle",
                fontSize: $"{fontSize:0}px", fontFamily: options.FontFamily,
                fill: GetContrastColor(color), fontWeight: node.IsLeaf ? "normal" : "bold");
        }

        foreach (var child in node.Children)
            DrawBubble(builder, child, ref colorIndex, options, theme, depth + 1);
    }

    static string AdjustOpacity(string hexColor, double opacity)
    {
        if (!hexColor.StartsWith('#') || hexColor.Length != 7)
            return hexColor;

        var r = Convert.ToInt32(hexColor.Substring(1, 2), 16);
        var g = Convert.ToInt32(hexColor.Substring(3, 2), 16);
        var b = Convert.ToInt32(hexColor.Substring(5, 2), 16);

        return $"rgba({r},{g},{b},{opacity:F2})";
    }

    static string TruncateLabel(string text, double maxWidth, double fontSize)
    {
        var charWidth = fontSize * 0.6;
        var maxChars = (int)(maxWidth / charWidth);
        if (text.Length <= maxChars)
            return text;
        return maxChars <= 3 ? "" : text[..Math.Max(0, maxChars - 2)] + "..";
    }

    static string DarkenColor(string hexColor, double factor)
    {
        if (!hexColor.StartsWith('#') || hexColor.Length != 7)
            return hexColor;

        var r = (int)(Convert.ToInt32(hexColor.Substring(1, 2), 16) * (1 - factor));
        var g = (int)(Convert.ToInt32(hexColor.Substring(3, 2), 16) * (1 - factor));
        var b = (int)(Convert.ToInt32(hexColor.Substring(5, 2), 16) * (1 - factor));

        return $"#{Math.Clamp(r, 0, 255):X2}{Math.Clamp(g, 0, 255):X2}{Math.Clamp(b, 0, 255):X2}";
    }

    static string GetContrastColor(string hexColor)
    {
        if (!hexColor.StartsWith('#') || hexColor.Length != 7)
            return "#fff";

        var r = Convert.ToInt32(hexColor.Substring(1, 2), 16);
        var g = Convert.ToInt32(hexColor.Substring(3, 2), 16);
        var b = Convert.ToInt32(hexColor.Substring(5, 2), 16);

        var luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
        return luminance > 0.5 ? "#333" : "#fff";
    }
}
