using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;
using MsaglCurve = Microsoft.Msagl.Core.Geometry.Curves.Curve;
using MsaglDrawingEdge = Microsoft.Msagl.Drawing.Edge;
using MsaglDrawingNode = Microsoft.Msagl.Drawing.Node;
using MsaglDrawingSubgraph = Microsoft.Msagl.Drawing.Subgraph;
using MsaglPoint = Microsoft.Msagl.Core.Geometry.Point;
using MsaglRectangle = Microsoft.Msagl.Core.Geometry.Rectangle;
using MsaglBundlingSettings = Microsoft.Msagl.Core.Routing.BundlingSettings;
using Direction = MermaidSharp.Models.Direction;
using Edge = MermaidSharp.Models.Edge;
using Subgraph = MermaidSharp.Models.Subgraph;

namespace MermaidSharp.Layout;

public class MsaglLayoutEngine(
    bool enableBundlingForDenseGraphs = true,
    double curveInterpolationTolerance = 1.5) : ILayoutEngine
{
    readonly bool _enableBundlingForDenseGraphs = enableBundlingForDenseGraphs;
    readonly double _curveInterpolationTolerance = curveInterpolationTolerance;

    public LayoutResult Layout(GraphDiagramBase diagram, LayoutOptions options)
    {
        if (diagram.Nodes.Count == 0)
        {
            return new() { Width = 0, Height = 0 };
        }

        var graph = new Graph();
        ConfigureGraphAttributes(graph, options);

        var drawingNodes = BuildDrawingNodes(diagram, graph);
        var drawingEdges = BuildDrawingEdges(diagram, graph, drawingNodes);
        AddSubgraphs(diagram, graph, drawingNodes);

        graph.CreateGeometryGraph();
        ApplyNodeBoundaries(diagram, drawingNodes);
        ApplyEdgeLabelSizing(drawingEdges);

        var settings = CreateLayoutSettings(diagram, graph, options);
        LayoutHelpers.CalculateLayout(graph.GeometryGraph, settings, null);

        var bounds = graph.GeometryGraph.BoundingBox;
        var offsetX = -bounds.Left;
        // MSAGL uses math coordinates (Y up), SVG uses screen coordinates (Y down).
        // Flip Y by mapping msaglY → (bounds.Top - msaglY) so Top→0 and Bottom→height.
        var flipY = bounds.Top;
        var offsetY = 0.0;

        ApplyNodePositions(diagram, drawingNodes, offsetX, offsetY, flipY);
        ApplyEdgeRouting(diagram, drawingEdges, offsetX, offsetY, flipY);

        return CalculateLayoutResult(diagram);
    }

    static void ConfigureGraphAttributes(Graph graph, LayoutOptions options)
    {
        graph.Attr.LayerDirection = options.Direction switch
        {
            Direction.LeftToRight => LayerDirection.LR,
            Direction.RightToLeft => LayerDirection.RL,
            Direction.BottomToTop => LayerDirection.BT,
            _ => LayerDirection.TB
        };
        graph.Attr.NodeSeparation = Math.Max(options.NodeSeparation, 20);
        graph.Attr.LayerSeparation = Math.Max(options.RankSeparation, 20);
    }

    static Dictionary<string, MsaglDrawingNode> BuildDrawingNodes(GraphDiagramBase diagram, Graph graph)
    {
        var result = new Dictionary<string, MsaglDrawingNode>(StringComparer.Ordinal);
        foreach (var node in diagram.Nodes)
        {
            var drawingNode = graph.AddNode(node.Id);
            drawingNode.LabelText = node.DisplayLabel;
            result[node.Id] = drawingNode;
        }

        return result;
    }

    static Dictionary<Edge, MsaglDrawingEdge> BuildDrawingEdges(
        GraphDiagramBase diagram,
        Graph graph,
        IReadOnlyDictionary<string, MsaglDrawingNode> drawingNodes)
    {
        var result = new Dictionary<Edge, MsaglDrawingEdge>();
        foreach (var edge in diagram.Edges)
        {
            if (!drawingNodes.ContainsKey(edge.SourceId) || !drawingNodes.ContainsKey(edge.TargetId))
            {
                continue;
            }

            var drawingEdge = graph.AddEdge(edge.SourceId, edge.TargetId);
            if (!string.IsNullOrWhiteSpace(edge.Label))
            {
                drawingEdge.LabelText = edge.Label;
            }

            result[edge] = drawingEdge;
        }

        return result;
    }

    static void AddSubgraphs(
        GraphDiagramBase diagram,
        Graph graph,
        IReadOnlyDictionary<string, MsaglDrawingNode> drawingNodes)
    {
        foreach (var subgraph in diagram.Subgraphs)
        {
            var drawingSubgraph = BuildSubgraphRecursive(subgraph, drawingNodes);
            graph.RootSubgraph.AddSubgraph(drawingSubgraph);
        }
    }

    static MsaglDrawingSubgraph BuildSubgraphRecursive(
        Subgraph subgraph,
        IReadOnlyDictionary<string, MsaglDrawingNode> drawingNodes)
    {
        var drawingSubgraph = new MsaglDrawingSubgraph(subgraph.Id)
        {
            LabelText = string.IsNullOrWhiteSpace(subgraph.Title) ? subgraph.Id : subgraph.Title
        };

        foreach (var nodeId in subgraph.NodeIds)
        {
            if (drawingNodes.TryGetValue(nodeId, out var node))
            {
                drawingSubgraph.AddNode(node);
            }
        }

        foreach (var nested in subgraph.NestedSubgraphs)
        {
            drawingSubgraph.AddSubgraph(BuildSubgraphRecursive(nested, drawingNodes));
        }

        return drawingSubgraph;
    }

    static void ApplyNodeBoundaries(
        GraphDiagramBase diagram,
        IReadOnlyDictionary<string, MsaglDrawingNode> drawingNodes)
    {
        foreach (var node in diagram.Nodes)
        {
            if (!drawingNodes.TryGetValue(node.Id, out var drawingNode) || drawingNode.GeometryNode is null)
            {
                continue;
            }

            var width = Math.Max(node.Width, 12);
            var height = Math.Max(node.Height, 12);
            drawingNode.GeometryNode.BoundaryCurve = CreateBoundary(node.Shape, width, height);

            if (drawingNode.Label?.GeometryLabel is { } geometryLabel)
            {
                geometryLabel.Width = Math.Max(width - 18, 8);
                geometryLabel.Height = Math.Max(height - 10, 8);
            }
        }
    }

    static ICurve CreateBoundary(NodeShape shape, double width, double height) =>
        shape switch
        {
            NodeShape.Circle => CurveFactory.CreateCircle(Math.Max(width, height) / 2, new MsaglPoint(0, 0)),
            NodeShape.DoubleCircle => CurveFactory.CreateCircle(Math.Max(width, height) / 2, new MsaglPoint(0, 0)),
            NodeShape.Diamond => CurveFactory.CreateDiamond(width / 2, height / 2, new MsaglPoint(0, 0)),
            NodeShape.RoundedRectangle => CurveFactory.CreateRectangleWithRoundedCorners(width, height, 8, 8, new MsaglPoint(0, 0)),
            NodeShape.Stadium => CurveFactory.CreateRectangleWithRoundedCorners(width, height, height / 2, height / 2, new MsaglPoint(0, 0)),
            _ => CurveFactory.CreateRectangle(width, height, new MsaglPoint(0, 0))
        };

    static void ApplyEdgeLabelSizing(IReadOnlyDictionary<Edge, MsaglDrawingEdge> drawingEdges)
    {
        foreach (var (edge, drawingEdge) in drawingEdges)
        {
            if (string.IsNullOrWhiteSpace(edge.Label) || drawingEdge.Label?.GeometryLabel is null)
            {
                continue;
            }

            drawingEdge.Label.GeometryLabel.Width = Math.Max(edge.Label.Length * 7 + 12, 20);
            drawingEdge.Label.GeometryLabel.Height = 18;
        }
    }

    SugiyamaLayoutSettings CreateLayoutSettings(GraphDiagramBase diagram, Graph graph, LayoutOptions options)
    {
        var settings = graph.CreateLayoutSettings();
        settings.NodeSeparation = Math.Max(options.NodeSeparation, 20);
        settings.LayerSeparation = Math.Max(options.RankSeparation, 20);
        settings.NoGainAdjacentSwapStepsBound = 12;
        settings.MaxNumberOfPassesInOrdering = 32;
        settings.RepetitionCoefficientForOrdering = 8;
        settings.BrandesThreshold = 2000;
        settings.EdgeRoutingSettings.Padding = Math.Max(options.NodeSeparation * 0.08, 2);
        settings.EdgeRoutingSettings.PolylinePadding = Math.Max(options.EdgeSeparation * 0.12, 1.5);
        settings.EdgeRoutingSettings.EdgeSeparationRectilinear = Math.Max(options.EdgeSeparation * 0.5, 1.0);
        settings.EdgeRoutingSettings.ConeAngle = 25 * Math.PI / 180.0;
        settings.EdgeRoutingSettings.RouteMultiEdgesAsBundles = true;

        var useBundling = _enableBundlingForDenseGraphs &&
                          diagram.Edges.Count >= Math.Max(12, diagram.Nodes.Count * 2);
        if (useBundling)
        {
            settings.EdgeRoutingSettings.EdgeRoutingMode = EdgeRoutingMode.SplineBundling;
            settings.EdgeRoutingSettings.BundlingSettings = new MsaglBundlingSettings
            {
                HighestQuality = true,
                KeepOverlaps = false,
                EdgeSeparation = Math.Max(options.EdgeSeparation * 0.06, MsaglBundlingSettings.DefaultEdgeSeparation)
            };
        }
        else
        {
            settings.EdgeRoutingSettings.EdgeRoutingMode = EdgeRoutingMode.SugiyamaSplines;
        }

        return settings;
    }

    static void ApplyNodePositions(
        GraphDiagramBase diagram,
        IReadOnlyDictionary<string, MsaglDrawingNode> drawingNodes,
        double offsetX,
        double offsetY,
        double flipY)
    {
        foreach (var node in diagram.Nodes)
        {
            if (!drawingNodes.TryGetValue(node.Id, out var drawingNode) || drawingNode.GeometryNode is null)
            {
                continue;
            }

            var center = drawingNode.GeometryNode.Center;
            node.Position = new(center.X + offsetX, flipY - center.Y + offsetY);
        }
    }

    void ApplyEdgeRouting(
        GraphDiagramBase diagram,
        IReadOnlyDictionary<Edge, MsaglDrawingEdge> drawingEdges,
        double offsetX,
        double offsetY,
        double flipY)
    {
        foreach (var edge in diagram.Edges)
        {
            edge.Points.Clear();

            if (!drawingEdges.TryGetValue(edge, out var drawingEdge) || drawingEdge.GeometryEdge?.Curve is null)
            {
                AddFallbackEdgePoints(diagram, edge);
                continue;
            }

            var curvePoints = ExtractCurvePoints(drawingEdge.GeometryEdge.Curve);
            foreach (var point in curvePoints)
            {
                edge.Points.Add(new(point.X + offsetX, flipY - point.Y + offsetY));
            }

            if (edge.Points.Count < 2)
            {
                AddFallbackEdgePoints(diagram, edge);
            }
        }
    }

    List<MsaglPoint> ExtractCurvePoints(ICurve curve)
    {
        IEnumerable<MsaglPoint> points = curve switch
        {
            Polyline polyline => polyline,
            MsaglCurve => SampleCurvePoints(curve),
            _ => [curve.Start, curve.End]
        };

        var deduped = new List<MsaglPoint>();
        foreach (var point in points)
        {
            if (deduped.Count == 0 || (deduped[^1] - point).Length > 0.1)
            {
                deduped.Add(point);
            }
        }

        return deduped;
    }

    List<MsaglPoint> SampleCurvePoints(ICurve curve)
    {
        var result = new List<MsaglPoint>();
        var parStart = curve.ParStart;
        var parEnd = curve.ParEnd;
        var range = parEnd - parStart;

        // Sample enough points to approximate the curve within tolerance
        var steps = Math.Max(8, (int)(range / _curveInterpolationTolerance));
        for (var i = 0; i <= steps; i++)
        {
            var t = parStart + range * i / steps;
            result.Add(curve[t]);
        }

        return result;
    }

    static void AddFallbackEdgePoints(GraphDiagramBase diagram, Edge edge)
    {
        var source = diagram.GetNode(edge.SourceId);
        var target = diagram.GetNode(edge.TargetId);
        if (source is null || target is null)
        {
            return;
        }

        edge.Points.Add(source.Position);
        edge.Points.Add(target.Position);
    }

    static LayoutResult CalculateLayoutResult(GraphDiagramBase diagram)
    {
        var maxX = 0d;
        var maxY = 0d;

        foreach (var node in diagram.Nodes)
        {
            maxX = Math.Max(maxX, node.Position.X + node.Width / 2);
            maxY = Math.Max(maxY, node.Position.Y + node.Height / 2);
        }

        foreach (var edge in diagram.Edges)
        {
            foreach (var point in edge.Points)
            {
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        return new()
        {
            Width = maxX,
            Height = maxY
        };
    }
}
