using System.Globalization;
using System.Linq;
using System.Text;
using MermaidSharp.Diagrams.Flowchart;
using MermaidSharp.Models;

namespace MermaidSharp.Rendering.Surfaces;

public sealed class ConsoleDiagramRenderSurfacePlugin : IDiagramRenderSurfacePlugin
{
    const ushort North = 1 << 0;
    const ushort East = 1 << 1;
    const ushort South = 1 << 2;
    const ushort West = 1 << 3;
    const ushort NorthEast = 1 << 4;
    const ushort NorthWest = 1 << 5;
    const ushort SouthEast = 1 << 6;
    const ushort SouthWest = 1 << 7;

    const int CanvasPadding = 2;

    enum ConsoleGlyphMode
    {
        Ascii,
        Unicode
    }

    enum ConsoleColorMode
    {
        Auto,
        Ansi,
        None
    }

    enum ConsoleSpacingMode
    {
        Preserve,
        Compact,
        Aggressive
    }

    readonly record struct ConsoleRenderOptions(
        ConsoleGlyphMode GlyphMode,
        ConsoleColorMode ColorMode,
        ConsoleSpacingMode SpacingMode,
        int MaxColumns,
        int MaxRows,
        int MaxBlankRows,
        int MaxBlankColumns,
        double XScaleFactor,
        double YScaleFactor,
        double StaggerThreshold)
    {
        public static ConsoleRenderOptions CreateDefault()
        {
            var columns = ReadIntEnv("COLUMNS", 160);
            var rows = ReadIntEnv("LINES", 60);
            return new ConsoleRenderOptions(
                GlyphMode: ConsoleGlyphMode.Ascii,
                ColorMode: ConsoleColorMode.Auto,
                SpacingMode: ConsoleSpacingMode.Compact,
                MaxColumns: columns,
                MaxRows: rows,
                MaxBlankRows: 1,
                MaxBlankColumns: 3,
                XScaleFactor: 1d,
                YScaleFactor: 0.55d,
                StaggerThreshold: 0.35d);
        }

        static int ReadIntEnv(string name, int fallback)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : fallback;
        }
    }

    static readonly IReadOnlyCollection<RenderSurfaceFormat> Formats = [RenderSurfaceFormat.Console];
    static readonly IReadOnlyCollection<DiagramType> DiagramTypes = [DiagramType.Flowchart];

    public string Name => "console";
    public IReadOnlyCollection<RenderSurfaceFormat> SupportedFormats => Formats;
    public IReadOnlyCollection<DiagramType>? SupportedDiagramTypes => DiagramTypes;
    public bool Supports(RenderSurfaceFormat format) => format == RenderSurfaceFormat.Console;

    public RenderSurfaceOutput Render(RenderSurfaceContext context, RenderSurfaceRequest request)
    {
        if (context.DiagramType != DiagramType.Flowchart)
            throw new MermaidException("Console surface currently supports flowchart diagrams only.");

        var layout = Mermaid.ParseAndLayoutFlowchart(context.MermaidSource, context.RenderOptions)
            ?? throw new MermaidException("Failed to parse flowchart.");

        var options = ParseConsoleRenderOptions(request);
        var text = RenderToText(layout, request.Scale, options);
        return RenderSurfaceOutput.FromText(text, "text/plain");
    }

    static string RenderToText(FlowchartLayoutResult layout, float requestedScale, ConsoleRenderOptions options)
    {
        var nodes = layout.Model.Nodes;
        var edges = layout.Model.Edges;

        if (nodes.Count == 0)
            return "(empty diagram)\n";

        var bounds = CalculateBounds(nodes, edges);
        var (xScale, yScale) = ResolveEffectiveScale(layout, bounds, requestedScale, options);
        var useAnsi = ResolveAnsiColor(options.ColorMode);

        var canvasWidth = Math.Max(4, (int)Math.Ceiling(bounds.Width * xScale) + CanvasPadding * 2);
        var canvasHeight = Math.Max(4, (int)Math.Ceiling(bounds.Height * yScale) + CanvasPadding * 2);

        var canvas = new char[canvasHeight, canvasWidth];
        var strokeMask = new ushort[canvasHeight, canvasWidth];
        var bgColors = new string?[canvasHeight, canvasWidth];
        var fgColors = new string?[canvasHeight, canvasWidth];

        Fill(canvas, ' ');

        var offsetX = CanvasPadding - bounds.MinX * xScale;
        var offsetY = CanvasPadding - bounds.MinY * yScale;
        var useStaggeredApproximation = Math.Min(xScale, yScale) < options.StaggerThreshold;

        foreach (var edge in edges)
        {
            DrawEdge(
                canvas,
                strokeMask,
                bgColors,
                fgColors,
                edge,
                xScale,
                yScale,
                offsetX,
                offsetY,
                layout.Skin,
                options.GlyphMode,
                useStaggeredApproximation);
        }

        ApplyStrokeMask(canvas, strokeMask, options.GlyphMode);

        foreach (var node in nodes)
            DrawNode(canvas, bgColors, fgColors, node, xScale, yScale, offsetX, offsetY, layout.Skin);

        return RenderCanvas(canvas, bgColors, fgColors, useAnsi, options);
    }

    static (double XScale, double YScale) ResolveEffectiveScale(
        FlowchartLayoutResult layout,
        (double MinX, double MinY, double Width, double Height) bounds,
        float requestedScale,
        ConsoleRenderOptions options)
    {
        var baseScale = requestedScale > 0
            ? requestedScale
            : ResolveAutoScale(layout);

        var xScale = Math.Max(0.02d, baseScale * options.XScaleFactor);
        var yScale = Math.Max(0.02d, baseScale * options.YScaleFactor);

        var projectedWidth = bounds.Width * xScale + CanvasPadding * 2;
        var projectedHeight = bounds.Height * yScale + CanvasPadding * 2;
        var fitScale = Math.Min(
            options.MaxColumns / Math.Max(1d, projectedWidth),
            options.MaxRows / Math.Max(1d, projectedHeight));

        if (fitScale < 1d)
        {
            xScale *= fitScale;
            yScale *= fitScale;
        }

        return (xScale, yScale);
    }

    static float ResolveAutoScale(FlowchartLayoutResult layout)
    {
        var nodes = layout.Model.Nodes;
        if (nodes.Count == 0)
            return 0.6f;

        var longestLabelLine = nodes
            .SelectMany(n => (n.DisplayLabel ?? string.Empty).Split('\n'))
            .DefaultIfEmpty(string.Empty)
            .Max(static x => x.Length);

        var widestNode = Math.Max(1d, nodes.Max(n => n.Width));
        var tallestNode = Math.Max(1d, nodes.Max(n => n.Height));

        // Default behavior targets "label mostly fits", then allows graceful degradation
        // if terminal bounds force a smaller scale.
        var labelFitScale = Math.Max(0.12d, (longestLabelLine + 2d) / widestNode);
        var heightFitScale = Math.Max(0.12d, 3d / tallestNode);
        return (float)Math.Max(labelFitScale, heightFitScale);
    }

    static (double MinX, double MinY, double Width, double Height) CalculateBounds(List<Node> nodes, List<Edge> edges)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var n in nodes)
        {
            var b = n.Bounds;
            if (b.X < minX) minX = b.X;
            if (b.Y < minY) minY = b.Y;
            if (b.X + b.Width > maxX) maxX = b.X + b.Width;
            if (b.Y + b.Height > maxY) maxY = b.Y + b.Height;
        }
        foreach (var e in edges)
            foreach (var p in e.Points)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
        return (minX, minY, maxX - minX, maxY - minY);
    }

    static ConsoleRenderOptions ParseConsoleRenderOptions(RenderSurfaceRequest request) => ConsoleRenderOptions.CreateDefault();
    static void DrawEdge(char[,] canvas, ushort[,] strokeMask, string?[,] bgColors, string?[,] fgColors, Edge edge, double xScale, double yScale, double offsetX, double offsetY, FlowchartSkin skin, ConsoleGlyphMode glyphMode, bool useStagger) { }
    static void ApplyStrokeMask(char[,] canvas, ushort[,] strokeMask, ConsoleGlyphMode glyphMode) { }
    static void DrawNode(char[,] canvas, string?[,] bgColors, string?[,] fgColors, Node node, double xScale, double yScale, double offsetX, double offsetY, FlowchartSkin skin) { }
    static string RenderCanvas(char[,] canvas, string?[,] bgColors, string?[,] fgColors, bool useAnsi, ConsoleRenderOptions options) => string.Empty;
    static void Fill(char[,] arr, char c) { for (int r = 0; r < arr.GetLength(0); r++) for (int col = 0; col < arr.GetLength(1); col++) arr[r, col] = c; }
}
