using MermaidSharp.Rendering.Surfaces;

namespace MermaidSharp.Tests.Fluent;

[NonParallelizable]
public class RenderSurfaceAllDiagramSamplesTests
{
    [SetUp]
    public void SetUp()
    {
        MermaidRenderSurfaces.Unregister("imagesharp");
        MermaidRenderSurfaces.Unregister("skia");
    }

    [TearDown]
    public void TearDown()
    {
        MermaidRenderSurfaces.Unregister("imagesharp");
        MermaidRenderSurfaces.Unregister("skia");
    }

    [Test]
    public void CoreSurfaces_SvgAndXaml_Succeed_ForAllDiagramSamples()
    {
        var samples = LoadSamples();
        var failures = new List<string>();

        foreach (var sample in samples)
        {
            Validate(sample, RenderSurfaceFormat.Svg, "image/svg+xml", failures);
            Validate(sample, RenderSurfaceFormat.Xaml, "application/xaml+xml", failures);
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    [Test]
    public void CoreSurface_ReactFlow_Succeeds_ForFlowchartSample()
    {
        var flowchart = LoadSamples().Single(x => x.Name.Equals("Flowchart", StringComparison.OrdinalIgnoreCase));
        var request = new RenderSurfaceRequest(RenderSurfaceFormat.ReactFlow);
        var success = MermaidRenderSurfaces.TryRender(flowchart.Source, request, out var output, out RenderSurfaceFailure? failure);

        Assert.That(success, Is.True, failure?.Message ?? "unknown error");
        Assert.That(output?.Text, Does.Contain("\"nodes\""));
        Assert.That(output?.Text, Does.Contain("\"edges\""));
        Assert.That(output?.MimeType, Is.EqualTo("application/json"));
    }

    [Test]
    public void ImageSharpSurfaces_RasterFormats_Succeed_ForAllDiagramSamples()
    {
        MermaidRenderSurfacesImageSharpExtensions.RegisterImageSharpSurface();
        var samples = LoadSamples();
        var failures = new List<string>();

        foreach (var sample in samples)
        {
            Validate(sample, RenderSurfaceFormat.Png, "image/png", failures);
            Validate(sample, RenderSurfaceFormat.Jpeg, "image/jpeg", failures);
            Validate(sample, RenderSurfaceFormat.Webp, "image/webp", failures);
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    [Test]
    public void SkiaSurfaces_RasterAndPdf_Succeed_ForAllDiagramSamples()
    {
        MermaidRenderSurfacesSkiaExtensions.RegisterSkiaSurface();
        var samples = LoadSamples();
        var failures = new List<string>();

        foreach (var sample in samples)
        {
            Validate(sample, RenderSurfaceFormat.Png, "image/png", failures);
            Validate(sample, RenderSurfaceFormat.Jpeg, "image/jpeg", failures);
            Validate(sample, RenderSurfaceFormat.Webp, "image/webp", failures);
            Validate(sample, RenderSurfaceFormat.Pdf, "application/pdf", failures);
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    static void Validate(
        DiagramSample sample,
        RenderSurfaceFormat format,
        string mimeType,
        ICollection<string> failures)
    {
        var request = new RenderSurfaceRequest(format);
        var success = MermaidRenderSurfaces.TryRender(sample.Source, request, out var output, out RenderSurfaceFailure? failure);
        if (!success)
        {
            failures.Add($"{sample.Name}: {format} failed -> {failure?.Code}: {failure?.Message}");
            return;
        }

        if (output is null)
        {
            failures.Add($"{sample.Name}: {format} returned null output");
            return;
        }

        if (!string.Equals(output.MimeType, mimeType, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{sample.Name}: {format} mime mismatch expected '{mimeType}' actual '{output.MimeType}'");
            return;
        }

        if (format is RenderSurfaceFormat.Svg or RenderSurfaceFormat.Xaml)
        {
            if (string.IsNullOrWhiteSpace(output.Text))
            {
                failures.Add($"{sample.Name}: {format} produced empty text output");
                return;
            }
        }
        else if (output.Bytes is null || output.Bytes.Length == 0)
        {
            failures.Add($"{sample.Name}: {format} produced empty binary output");
        }
    }

    static IReadOnlyList<DiagramSample> LoadSamples()
    {
        var folder = Path.Combine(ProjectFiles.SolutionDirectory, "test-renders");
        var files = Directory
            .GetFiles(folder, "*.md", SearchOption.TopDirectoryOnly)
            .Where(x => !x.EndsWith("renders.include.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var samples = new List<DiagramSample>();
        foreach (var file in files)
        {
            var markdown = File.ReadAllText(file);
            var source = TryGetFirstCodeFence(markdown);
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            samples.Add(new DiagramSample(
                Path.GetFileNameWithoutExtension(file),
                source));
        }

        return samples;
    }

    static string? TryGetFirstCodeFence(string markdown)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            markdown,
            "```(?:[^\\r\\n`]*)\\r?\\n(?<code>[\\s\\S]*?)```",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            return null;
        }

        return match.Groups["code"].Value.Trim();
    }

    sealed record DiagramSample(string Name, string Source);
}
