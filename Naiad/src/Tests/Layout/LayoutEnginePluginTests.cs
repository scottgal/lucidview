using MermaidSharp.Layout;
using MermaidSharp.Models;
using PositionModel = MermaidSharp.Models.Position;

namespace MermaidSharp.Tests.Layout;

public class LayoutEnginePluginTests
{
    [Test]
    public void Resolve_WithBuiltInNames_ReturnsExpectedEngineType()
    {
        var dagreEngine = MermaidLayoutEngines.Resolve(DiagramType.Flowchart, LayoutEngineNames.Dagre);

        Assert.That(dagreEngine, Is.TypeOf<MostlylucidDagreLayoutEngine>());
    }

    [Test]
    public void GetRecommendedLayoutEngine_ReturnsDagreForGraphFamilies()
    {
        Assert.That(Mermaid.GetRecommendedLayoutEngine(DiagramType.Flowchart), Is.EqualTo(LayoutEngineNames.Dagre));
        Assert.That(Mermaid.GetRecommendedLayoutEngine(DiagramType.Bpmn), Is.EqualTo(LayoutEngineNames.Dagre));
        Assert.That(Mermaid.GetRecommendedLayoutEngine(DiagramType.Class), Is.EqualTo(LayoutEngineNames.Dagre));
        Assert.That(Mermaid.GetRecommendedLayoutEngine(DiagramType.State), Is.EqualTo(LayoutEngineNames.Dagre));
        Assert.That(Mermaid.GetRecommendedLayoutEngine(DiagramType.EntityRelationship), Is.EqualTo(LayoutEngineNames.Dagre));
    }

    [Test]
    public void ParseAndLayoutFlowchart_DirectiveCanSelectRegisteredLayoutEngine()
    {
        const string pluginName = "test-fixed-layout";

        MermaidLayoutEngines.Register(new StaticLayoutEnginePlugin(
            pluginName,
            static () => new FixedLayoutEngine(900, 125),
            supportedDiagramTypes: [DiagramType.Flowchart]));

        try
        {
            var input =
                """
                %% naiad: layoutEngine=test-fixed-layout
                flowchart LR
                    A[Start] --> B[End]
                """;

            var layout = Mermaid.ParseAndLayoutFlowchart(input);
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout!.Model.Nodes.Min(x => x.Position.X), Is.GreaterThanOrEqualTo(900d));
        }
        finally
        {
            MermaidLayoutEngines.Unregister(pluginName);
        }
    }

    [Test]
    public void ParseAndLayoutFlowchart_LayoutEngineResolverOverridesLayoutEngineName()
    {
        var options = new RenderOptions
        {
            LayoutEngine = LayoutEngineNames.Dagre,
            LayoutEngineResolver = _ => new FixedLayoutEngine(1200, 80)
        };

        var input =
            """
            flowchart LR
                A --> B
            """;

        var layout = Mermaid.ParseAndLayoutFlowchart(input, options);
        Assert.That(layout, Is.Not.Null);
        Assert.That(layout!.Model.Nodes.Min(x => x.Position.X), Is.GreaterThanOrEqualTo(1200d));
    }

    [Test]
    public void ParseAndLayoutFlowchart_PerDiagramEngineOverridesGlobalEngine()
    {
        const string pluginName = "test-flowchart-specific-layout";

        MermaidLayoutEngines.Register(new StaticLayoutEnginePlugin(
            pluginName,
            static () => new FixedLayoutEngine(1400, 60),
            supportedDiagramTypes: [DiagramType.Flowchart]));

        try
        {
            var options = new RenderOptions
            {
                LayoutEngine = LayoutEngineNames.Dagre,
                LayoutEngines = new Dictionary<DiagramType, string>
                {
                    [DiagramType.Flowchart] = pluginName
                }
            };

            var input =
                """
                flowchart LR
                    A --> B
                """;

            var layout = Mermaid.ParseAndLayoutFlowchart(input, options);
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout!.Model.Nodes.Min(x => x.Position.X), Is.GreaterThanOrEqualTo(1400d));
        }
        finally
        {
            MermaidLayoutEngines.Unregister(pluginName);
        }
    }

    [Test]
    public void ParseAndLayoutFlowchart_PerDiagramDirectiveCanSelectEngine()
    {
        const string pluginName = "test-directive-layout";

        MermaidLayoutEngines.Register(new StaticLayoutEnginePlugin(
            pluginName,
            static () => new FixedLayoutEngine(1000, 70),
            supportedDiagramTypes: [DiagramType.Flowchart]));

        try
        {
            var input =
                """
                %% naiad: layoutEngineFlowchart=test-directive-layout
                flowchart LR
                    A --> B
                """;

            var layout = Mermaid.ParseAndLayoutFlowchart(input);
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout!.Model.Nodes.Min(x => x.Position.X), Is.GreaterThanOrEqualTo(1000d));
        }
        finally
        {
            MermaidLayoutEngines.Unregister(pluginName);
        }
    }

    [Test]
    public void RenderOptionsClone_CopiesLayoutEngineSettings()
    {
        var resolver = (DiagramType _) => (ILayoutEngine)new MostlylucidDagreLayoutEngine();

        var options = new RenderOptions
        {
            LayoutEngine = LayoutEngineNames.Dagre,
            LayoutEngineResolver = resolver,
            LayoutEngines = new Dictionary<DiagramType, string>
            {
                [DiagramType.Flowchart] = LayoutEngineNames.Dagre
            }
        };

        var clone = options.Clone();

        Assert.That(clone.LayoutEngine, Is.EqualTo(LayoutEngineNames.Dagre));
        Assert.That(clone.LayoutEngineResolver, Is.SameAs(resolver));
        Assert.That(clone.LayoutEngines, Is.Not.Null);
        Assert.That(clone.LayoutEngines![DiagramType.Flowchart], Is.EqualTo(LayoutEngineNames.Dagre));
    }

    sealed class FixedLayoutEngine(double startX, double stepX) : ILayoutEngine
    {
        public LayoutResult Layout(GraphDiagramBase diagram, LayoutOptions options)
        {
            if (diagram.Nodes.Count == 0)
                return new() { Width = 0, Height = 0 };

            var x = startX;
            const double y = 100;

            foreach (var node in diagram.Nodes)
            {
                node.Position = new PositionModel(x, y);
                x += stepX;
            }

            foreach (var edge in diagram.Edges)
            {
                edge.Points.Clear();
                var source = diagram.GetNode(edge.SourceId);
                var target = diagram.GetNode(edge.TargetId);
                if (source is null || target is null)
                    continue;

                edge.Points.Add(new PositionModel(source.Position.X, source.Position.Y));
                edge.Points.Add(new PositionModel(target.Position.X, target.Position.Y));
            }

            var width = diagram.Nodes.Max(xn => xn.Position.X + xn.Width / 2);
            var height = diagram.Nodes.Max(yn => yn.Position.Y + yn.Height / 2);

            return new LayoutResult
            {
                Width = width,
                Height = height
            };
        }
    }
}
