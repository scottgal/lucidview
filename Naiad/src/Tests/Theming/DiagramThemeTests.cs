using MermaidSharp.Diagrams.Flowchart;

namespace MermaidSharp.Tests.Theming;

[TestFixture]
public class DiagramThemeTests
{
    [Test]
    public Task Resolve_DiagramTheme_InfersDarkFromBackgroundOverride()
    {
        var options = new RenderOptions
        {
            Theme = "default",
            ThemeColors = new ThemeColorOverrides
            {
                BackgroundColor = "#1e1e1e"
            }
        };

        var theme = DiagramTheme.Resolve(options);

        var snapshot = $$"""
                         IsDark={{theme.IsDark}}
                         Background={{theme.Background}}
                         TextColor={{theme.TextColor}}
                         PrimaryFill={{theme.PrimaryFill}}
                         AxisLine={{theme.AxisLine}}
                         """;
        return Verify(snapshot);
    }

    [Test]
    public Task Resolve_DiagramTheme_InfersLightFromBackgroundOverride()
    {
        var options = new RenderOptions
        {
            Theme = "dark",
            ThemeColors = new ThemeColorOverrides
            {
                BackgroundColor = "#ffffff"
            }
        };

        var theme = DiagramTheme.Resolve(options);

        var snapshot = $$"""
                         IsDark={{theme.IsDark}}
                         Background={{theme.Background}}
                         TextColor={{theme.TextColor}}
                         PrimaryFill={{theme.PrimaryFill}}
                         AxisLine={{theme.AxisLine}}
                         """;
        return Verify(snapshot);
    }

    [Test]
    public Task Resolve_FlowchartSkin_InfersDarkFromBackgroundOverrideWhenThemeIsDefault()
    {
        var skin = FlowchartSkin.Resolve("default", new ThemeColorOverrides
        {
            BackgroundColor = "#0f0f23"
        });

        var snapshot = $$"""
                         Name={{skin.Name}}
                         Background={{skin.Background}}
                         TextColor={{skin.TextColor}}
                         NodeFill={{skin.NodeFill}}
                         EdgeStroke={{skin.EdgeStroke}}
                         """;
        return Verify(snapshot);
    }
}
