using MermaidSharp.Rendering.Skins.Showcase;
using MermaidSharp.Rendering.Surfaces;

namespace MermaidSharp.Tests.Flowchart;

public class ShowcaseSkinPackTests
{
    [SetUp]
    public void SetUp() => MermaidSkinPacksShowcaseExtensions.UnregisterShowcaseSkinPacks();

    [TearDown]
    public void TearDown() => MermaidSkinPacksShowcaseExtensions.UnregisterShowcaseSkinPacks();

    [Test]
    public void RegisterShowcaseSkinPacks_ShouldExposePackNames()
    {
        MermaidSkinPacksShowcaseExtensions.RegisterShowcaseSkinPacks();

        var packs = Mermaid.GetAvailableSkinPacks();

        Assert.That(packs, Does.Contain("prism3d"));
        Assert.That(packs, Does.Contain("neon"));
        Assert.That(packs, Does.Contain("sunset"));
    }

    [Test]
    public void Prism3dSkin_ShouldRenderLayeredDefs()
    {
        MermaidSkinPacksShowcaseExtensions.RegisterShowcaseSkinPacks();

        var svg = Mermaid.Render(
            """
            %% naiad: {"skinPack":"prism3d","theme":"dark"}
            flowchart LR
                A[One] --> B[Two]
            """);

        Assert.That(svg, Does.Contain("skin-rectangle-prism-grad"));
        Assert.That(svg, Does.Contain("skin-rectangle-prism-shadow"));
        Assert.That(svg, Does.Contain("clip-path:url(#skin-rectangle-prism-clip)"));
    }

    [TestCase("3d", "skin-rectangle-prism-grad")]
    [TestCase("cyber", "skin-rectangle-neon-grad")]
    [TestCase("vibrant", "skin-rectangle-sunset-grad")]
    public void ShowcaseAliases_ShouldResolveToExpectedPack(string alias, string expectedToken)
    {
        MermaidSkinPacksShowcaseExtensions.RegisterShowcaseSkinPacks();

        var svg = Mermaid.Render(
            $$"""
              %% naiad: {"skinPack":"{{alias}}","theme":"dark"}
              flowchart LR
                  A[Alias] --> B[Resolved]
              """);

        Assert.That(svg, Does.Contain(expectedToken));
    }

    [Test]
    public void NeonSkin_ShouldRenderViaSkiaPngSurface()
    {
        MermaidSkinPacksShowcaseExtensions.RegisterShowcaseSkinPacks();
        MermaidRenderSurfaces.Unregister("skia");
        MermaidRenderSurfacesSkiaExtensions.RegisterSkiaSurface();
        try
        {
            var success = MermaidRenderSurfaces.TryRender(
                """
                %% naiad: {"skinPack":"neon","theme":"dark"}
                flowchart LR
                    A[API] --> B[Queue]
                    B --> C[Store]
                """,
                new RenderSurfaceRequest(RenderSurfaceFormat.Png),
                out var output,
                out RenderSurfaceFailure? failure);

            Assert.That(success, Is.True, failure?.Message);
            Assert.That(output, Is.Not.Null);
            Assert.That(output!.Bytes, Is.Not.Null);
            Assert.That(output.Bytes!.Length, Is.GreaterThan(0));
        }
        finally
        {
            MermaidRenderSurfaces.Unregister("skia");
        }
    }

    [TestCase(RenderSurfaceFormat.Png)]
    [TestCase(RenderSurfaceFormat.Jpeg)]
    [TestCase(RenderSurfaceFormat.Webp)]
    public void Prism3dSkin_ShouldRenderViaImageSharpSurface(RenderSurfaceFormat format)
    {
        MermaidSkinPacksShowcaseExtensions.RegisterShowcaseSkinPacks();
        MermaidRenderSurfaces.Unregister("imagesharp");
        MermaidRenderSurfacesImageSharpExtensions.RegisterImageSharpSurface();
        try
        {
            var success = MermaidRenderSurfaces.TryRender(
                """
                %% naiad: {"skinPack":"prism3d","theme":"light"}
                flowchart LR
                    A[Node A] --> B[Node B]
                """,
                new RenderSurfaceRequest(format),
                out var output,
                out RenderSurfaceFailure? failure);

            Assert.That(success, Is.True, failure?.Message);
            Assert.That(output, Is.Not.Null);
            Assert.That(output!.Bytes, Is.Not.Null);
            Assert.That(output.Bytes!.Length, Is.GreaterThan(0));
        }
        finally
        {
            MermaidRenderSurfaces.Unregister("imagesharp");
        }
    }

    [Test]
    public void UnregisterShowcaseSkinPacks_ShouldRemovePackNames()
    {
        MermaidSkinPacksShowcaseExtensions.RegisterShowcaseSkinPacks();
        MermaidSkinPacksShowcaseExtensions.UnregisterShowcaseSkinPacks();

        var packs = Mermaid.GetAvailableSkinPacks();

        Assert.That(packs, Does.Not.Contain("prism3d"));
        Assert.That(packs, Does.Not.Contain("neon"));
        Assert.That(packs, Does.Not.Contain("sunset"));
    }
}
