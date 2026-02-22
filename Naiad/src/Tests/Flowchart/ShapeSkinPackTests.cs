using System.IO.Compression;
using MermaidSharp.Rendering.Skins.Cats;
using MermaidSharp.Rendering.Surfaces;

namespace MermaidSharp.Tests.Flowchart;

public class ShapeSkinPackTests
{
    const string FlowchartInput =
        """
        flowchart LR
            A[Start] --> B[Done]
        """;

    [SetUp]
    public void SetUp() => MermaidSkinPacksCatsExtensions.UnregisterCatsSkinPack();

    [TearDown]
    public void TearDown() => MermaidSkinPacksCatsExtensions.UnregisterCatsSkinPack();

    [Test]
    public void BuiltInSkinPacks_ExposeNamedEmbeddedOptions()
    {
        var packs = Mermaid.GetBuiltInSkinPacks();

        Assert.That(packs, Does.Contain("default"));
        Assert.That(packs, Does.Contain("daisyui"));
        Assert.That(packs, Does.Contain("material"));
        Assert.That(packs, Does.Contain("material3"));
        Assert.That(packs, Does.Contain("fluent2"));
        Assert.That(packs, Does.Contain("glass"));
        Assert.That(packs, Does.Not.Contain("cats"));
    }

    [Test]
    public void NaiadCommentDirective_CanSelectBuiltInSkinPack()
    {
        var input =
            """
            %% naiad: skinPack=daisyui
            flowchart LR
                A[Start] --> B[Done]
            """;

        var svg = Mermaid.Render(input);

        Assert.That(svg, Does.Contain("M10 0H90Q100 0 100 10V50Q100 60 90 60H10Q0 60 0 50V10Q0 0 10 0Z"));
    }

    [Test]
    public void NaiadCommentDirective_CanUseBuiltInSkinAlias()
    {
        var input =
            """
            %% naiad: skinPack=fluent
            flowchart LR
                A[Start] --> B[Done]
            """;

        var svg = Mermaid.Render(input);

        Assert.That(svg, Does.Contain("M6 0H94Q100 0 100 6V54Q100 60 94 60H6Q0 60 0 54V6Q0 0 6 0Z"));
    }

    [Test]
    public void BuiltInGlassSkin_AppliesGlassStyleAttributes()
    {
        var input =
            """
            %% naiad: skinPack=glass
            flowchart LR
                A[Start] --> B[Done]
            """;

        var svg = Mermaid.Render(input);

        Assert.That(svg, Does.Contain("M8 0H92Q100 0 100 8V52Q100 60 92 60H8Q0 60 0 52V8Q0 0 8 0Z"));
        Assert.That(svg, Does.Contain("fill:rgba(125,211,252,0.35)"));
        Assert.That(svg, Does.Contain("stroke:#0284C7"));
        Assert.That(svg, Does.Contain("fill-opacity:0.75"));
    }

    [Test]
    public void NaiadCommentDirective_CanSelectCatsSkinPack()
    {
        MermaidSkinPacksCatsExtensions.RegisterCatsSkinPack();

        var input =
            """
            %% naiad: skinPack=cats
            flowchart LR
                A[Start] --> B[Done]
            """;

        var svg = Mermaid.Render(input);

        Assert.That(svg, Does.Contain("M10 24L22 8L35 20H65L78 8L90 24V50Q90 60 80 60H20Q10 60 10 50Z"));
    }

    [Test]
    public void NaiadCommentDirective_CanMapCatsCustomShapes()
    {
        MermaidSkinPacksCatsExtensions.RegisterCatsSkinPack();

        var input =
            """
            %% naiad: {"skinPack":"cats","shapes":"A=cat-paw,B=cat-fish"}
            flowchart LR
                A[Cat] --> B[Fish]
            """;

        var svg = Mermaid.Render(input);

        Assert.That(svg, Does.Contain("M50 24A11 11 0 1 1 50 46A11 11 0 1 1 50 24Z"));
        Assert.That(svg, Does.Contain("M4 30L20 18H56L70 8V20L96 12V48L70 40V52L56 42H20Z"));
    }

    [Test]
    public void AvailableSkinPacks_IncludeRegisteredCatsPlugin()
    {
        MermaidSkinPacksCatsExtensions.RegisterCatsSkinPack();
        var packs = Mermaid.GetAvailableSkinPacks();

        Assert.That(packs, Does.Contain("cats"));
    }

    [Test]
    public void ShapeSkinPack_ZipArchive_CanOverrideShapes_WhenEnabled()
    {
        var (tempDir, skinArchivePath) = CreateTempRectangleSkinArchive("M0 0H100V60H0Z");
        try
        {
            var svg = Mermaid.Render(FlowchartInput, new RenderOptions
            {
                SkinPack = skinArchivePath,
                AllowFileSystemSkinPacks = true
            });

            Assert.That(svg, Does.Contain("M0 0H100V60H0Z"));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void ShapeSkinPack_SvgStyleAttributes_AreAppliedInline()
    {
        const string styledRectanglePath = "M0 0H100V60H0Z";
        const string styledRectangleSvg =
            "<svg viewBox=\"0 0 100 60\" xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0H100V60H0Z\" fill=\"#123456\" stroke=\"#abcdef\" stroke-width=\"2\"/></svg>";
        var (tempDir, skinArchivePath) = CreateTempRectangleSkinArchive(styledRectanglePath, styledRectangleSvg);
        try
        {
            var svg = Mermaid.Render(FlowchartInput, new RenderOptions
            {
                SkinPack = skinArchivePath,
                AllowFileSystemSkinPacks = true
            });

            Assert.That(svg, Does.Contain(styledRectanglePath));
            Assert.That(svg, Does.Contain("fill:#123456"));
            Assert.That(svg, Does.Contain("stroke:#abcdef"));
            Assert.That(svg, Does.Contain("stroke-width:2"));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void ShapeSkinPack_MultiPathDefsAndFilter_ArePreserved()
    {
        const string basePath = "M0 0H100V60H0Z";
        const string shinePath = "M8 8H92V20H8Z";
        const string svgWithDefs =
            """
            <svg viewBox="0 0 100 60" xmlns="http://www.w3.org/2000/svg">
              <defs>
                <linearGradient id="glass-grad" x1="0%" y1="0%" x2="0%" y2="100%">
                  <stop offset="0%" stop-color="#8CD7FF"/>
                  <stop offset="100%" stop-color="#1E88E5"/>
                </linearGradient>
                <filter id="glass-shadow">
                  <feDropShadow dx="0" dy="1.5" stdDeviation="1.2" flood-color="#0F172A" flood-opacity="0.32"/>
                </filter>
              </defs>
              <path d="M0 0H100V60H0Z" style="fill:url(#glass-grad);stroke:#0B4A6F;stroke-width:1.5;filter:url(#glass-shadow)"/>
              <path d="M8 8H92V20H8Z" style="fill:rgba(255,255,255,0.35)"/>
            </svg>
            """;

        var (tempDir, skinArchivePath) = CreateTempRectangleSkinArchive(basePath, svgWithDefs);
        try
        {
            var svg = Mermaid.Render(FlowchartInput, new RenderOptions
            {
                SkinPack = skinArchivePath,
                AllowFileSystemSkinPacks = true
            });

            Assert.That(svg, Does.Contain(basePath));
            Assert.That(svg, Does.Contain(shinePath));
            Assert.That(svg, Does.Contain("<linearGradient id=\"skin-rectangle-glass-grad\""));
            Assert.That(svg, Does.Contain("<filter id=\"skin-rectangle-glass-shadow\""));
            Assert.That(svg, Does.Contain("fill:url(#skin-rectangle-glass-grad)"));
            Assert.That(svg, Does.Contain("filter:url(#skin-rectangle-glass-shadow)"));

            var gradientDefsCount = CountOccurrences(svg, "<linearGradient id=\"skin-rectangle-glass-grad\"");
            Assert.That(gradientDefsCount, Is.EqualTo(1),
                "Shared defs should be emitted once even when shape appears on multiple nodes.");
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void ShapeSkinPack_DefsGradientAndFilter_CanRasterizeWithSkia()
    {
        const string svgWithDefs =
            """
            <svg viewBox="0 0 100 60" xmlns="http://www.w3.org/2000/svg">
              <defs>
                <linearGradient id="glass-grad" x1="0%" y1="0%" x2="0%" y2="100%">
                  <stop offset="0%" stop-color="#8CD7FF"/>
                  <stop offset="100%" stop-color="#1E88E5"/>
                </linearGradient>
                <filter id="glass-shadow">
                  <feDropShadow dx="0" dy="1.5" stdDeviation="1.2" flood-color="#0F172A" flood-opacity="0.32"/>
                </filter>
              </defs>
              <path d="M0 0H100V60H0Z" style="fill:url(#glass-grad);stroke:#0B4A6F;stroke-width:1.5;filter:url(#glass-shadow)"/>
            </svg>
            """;

        var (tempDir, skinArchivePath) = CreateTempRectangleSkinArchive("M0 0H100V60H0Z", svgWithDefs);
        MermaidRenderSurfaces.Unregister("skia");
        MermaidRenderSurfacesSkiaExtensions.RegisterSkiaSurface();
        try
        {
            var success = MermaidRenderSurfaces.TryRender(
                FlowchartInput,
                new RenderSurfaceRequest(RenderSurfaceFormat.Png),
                out var output,
                out RenderSurfaceFailure? failure,
                new RenderOptions
                {
                    SkinPack = skinArchivePath,
                    AllowFileSystemSkinPacks = true
                });

            Assert.That(success, Is.True, failure?.Message);
            Assert.That(output, Is.Not.Null);
            Assert.That(output!.Bytes, Is.Not.Null);
            Assert.That(output.Bytes!.Length, Is.GreaterThan(0));
        }
        finally
        {
            MermaidRenderSurfaces.Unregister("skia");
            tempDir.Delete(true);
        }
    }

    [Test]
    public void ShapeSkinPack_FileSystemDirective_IsIgnored_WhenNotExplicitlyEnabled()
    {
        var (tempDir, skinArchivePath) = CreateTempRectangleSkinArchive("M5 0H95V60H5Z");
        try
        {
            var input =
                $"""
                 %% naiad: skinPack={skinArchivePath.Replace("\\", "/", StringComparison.Ordinal)}
                 flowchart LR
                     A[Start] --> B[Done]
                 """;

            var svg = Mermaid.Render(input);

            Assert.That(svg, Does.Not.Contain("M5 0H95V60H5Z"));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    static (DirectoryInfo TempDir, string ArchivePath) CreateTempRectangleSkinArchive(
        string rectanglePath,
        string? rectangleSvgContent = null)
    {
        var tempDir = Directory.CreateTempSubdirectory("naiad-skin-pack-");
        var packDir = tempDir.CreateSubdirectory("pack");
        File.WriteAllText(
            Path.Combine(packDir.FullName, "rectangle.svg"),
            rectangleSvgContent ?? $"<svg viewBox=\"0 0 100 60\" xmlns=\"http://www.w3.org/2000/svg\"><path d=\"{rectanglePath}\"/></svg>");

        var archivePath = Path.Combine(tempDir.FullName, "pack.naiadskin");
        ZipFile.CreateFromDirectory(packDir.FullName, archivePath);
        return (tempDir, archivePath);
    }

    static int CountOccurrences(string text, string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
