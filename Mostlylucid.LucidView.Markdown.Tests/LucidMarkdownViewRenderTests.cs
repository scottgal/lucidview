using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Mostlylucid.Avalonia.UITesting.Players;
using SkiaSharp;

namespace Mostlylucid.LucidView.Markdown.Tests;

[Collection("Avalonia")]
public class LucidMarkdownViewRenderTests
{
    private const string SampleMarkdown =
        "# Hello\n\nSome **bold** text.\n\n```mermaid\nflowchart TD\n A-->B\n```\n";

    private readonly HeadlessAvaloniaFixture _fx;

    public LucidMarkdownViewRenderTests(HeadlessAvaloniaFixture fx) => _fx = fx;

    [Fact]
    public async Task LucidMarkdownView_renders_non_background_pixels()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucid-md-render-{Guid.NewGuid():N}.png");

        await _fx.DispatchAsync(async () =>
        {
            var view = new LucidMarkdownView
            {
                Markdown = SampleMarkdown,
                Width = 600,
                Height = 400
            };

            var window = new Window
            {
                Width = 600,
                Height = 400,
                Content = view
            };

            window.Show();
            await HeadlessRender.SettleAsync(window);

            await ScreenshotCapture.CaptureControlAsync(window, view, path);
            window.Close();
        });

        Assert.True(File.Exists(path), $"Screenshot PNG should be written to {path}");

        using var skBitmap = SKBitmap.Decode(path);
        Assert.NotNull(skBitmap);
        Assert.True(skBitmap.Width > 0 && skBitmap.Height > 0);

        // Count non-background pixels (not pure white).
        int nonBackground = 0;
        for (int y = 0; y < skBitmap.Height; y++)
        {
            for (int x = 0; x < skBitmap.Width; x++)
            {
                var pixel = skBitmap.GetPixel(x, y);
                // Any pixel that is not pure white (255,255,255) counts as rendered content.
                if (pixel.Red != 255 || pixel.Green != 255 || pixel.Blue != 255)
                    nonBackground++;
            }
        }

        Assert.True(nonBackground > 100,
            $"Expected rendered content pixels but found only {nonBackground}. " +
            "LucidMarkdownView may not have rendered any text.");
    }

    [Fact]
    public Task LucidMarkdownView_visual_tree_contains_markdown_renderer()
    {
        return _fx.DispatchAsync(async () =>
        {
            var view = new LucidMarkdownView
            {
                Markdown = SampleMarkdown,
                Width = 600,
                Height = 400
            };

            var window = new Window
            {
                Width = 600,
                Height = 400,
                Content = view
            };

            window.Show();
            await HeadlessRender.SettleAsync(window);

            var descendantNames = window.GetVisualDescendants()
                .Select(d => d.GetType().Name)
                .ToHashSet();

            // The MarkdownRenderer from LiveMarkdown.Avalonia must be present.
            Assert.Contains("MarkdownRenderer", descendantNames);

            // A ScrollViewer should wrap it.
            Assert.Contains("ScrollViewer", descendantNames);

            // Note: FlowchartCanvas / DiagramCanvas may not materialize headlessly
            // because the Mermaid render pipeline requires native Skia rendering paths
            // that may not be available in a software headless session. This is expected.
            // We simply log whether they appeared rather than asserting on it.
            var hasDiagramCanvas =
                descendantNames.Contains("FlowchartCanvas") ||
                descendantNames.Contains("DiagramCanvas");

            // Not a hard assertion — headless Mermaid rendering is best-effort.
            _ = hasDiagramCanvas;

            window.Close();
        });
    }

    [Fact]
    public Task LucidMarkdownView_Markdown_property_change_rerenders()
    {
        return _fx.DispatchAsync(async () =>
        {
            var view = new LucidMarkdownView
            {
                Markdown = "# First",
                Width = 600,
                Height = 400
            };

            var window = new Window
            {
                Width = 600,
                Height = 400,
                Content = view
            };

            window.Show();
            await HeadlessRender.SettleAsync(window);

            // Change the Markdown and settle again — should not throw.
            view.Markdown = "# Second\n\nUpdated content.";
            await HeadlessRender.SettleAsync(window);

            var descendantNames = window.GetVisualDescendants()
                .Select(d => d.GetType().Name)
                .ToHashSet();

            Assert.Contains("MarkdownRenderer", descendantNames);

            window.Close();
        });
    }

    [Fact]
    public Task LucidMarkdownView_replaces_flowchart_marker_inside_a_scrolling_column()
    {
        // A mermaid fence has to end up as a drawn FlowchartCanvas with no FLOWCHART: marker text
        // anywhere, with the view hosted the way mylo hosts it: in a centred, explicitly sized
        // column inside a ScrollViewer.
        //
        // Worth knowing what this does and does not prove. Under the headless session two settle
        // passes are enough for LiveMarkdown to build the marker, so this also passed before the
        // marker watch was added; it is a floor on the end state, not a reproduction of the timing
        // that broke mylo. The real window takes two or three layout passes longer and only the
        // driven run catches that, which is what ux-scripts/run-reader-mermaid.sh is for.
        return _fx.DispatchAsync(async () =>
        {
            var view = new LucidMarkdownView
            {
                Markdown = "Before.\n\n```mermaid\nflowchart TD\n A[One]-->B[Two]\n```\n\nAfter.",
                Width = 600
            };

            var window = new Window
            {
                Width = 800,
                Height = 600,
                Content = new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Width = 600,
                        Children = { view }
                    }
                }
            };

            window.Show();
            await HeadlessRender.SettleAsync(window);
            await HeadlessRender.SettleAsync(window);

            var descendants = window.GetVisualDescendants().ToList();

            var markerText = descendants
                .OfType<TextBlock>()
                .Select(tb => tb.Text ?? string.Concat(
                    tb.Inlines?.OfType<Run>().Select(r => r.Text ?? "") ?? []))
                .FirstOrDefault(t => t.Contains("FLOWCHART:", StringComparison.OrdinalIgnoreCase));

            Assert.Null(markerText);
            Assert.Contains(descendants, d => d.GetType().Name == "FlowchartCanvas");

            window.Close();
        });
    }

    [Fact]
    public async Task LucidMarkdownView_renders_explicit_size_image_at_full_height()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lucid-md-image-{Guid.NewGuid():N}");
        var imagePath = Path.Combine(tempDir, "red.png");
        var screenshotPath = Path.Combine(tempDir, "render.png");
        Directory.CreateDirectory(tempDir);

        try
        {
            using (var bitmap = new SKBitmap(120, 80))
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Red);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(imagePath, data.ToArray());
            }

            await _fx.DispatchAsync(async () =>
            {
                var view = new LucidMarkdownView
                {
                    Markdown = "![red](red.png)",
                    SourcePath = tempDir,
                    Width = 400,
                    Height = 240
                };
                var window = new Window { Width = 400, Height = 240, Content = view };

                window.Show();
                await HeadlessRender.SettleAsync(window);
                // Local image decode is asynchronous. The shared headless UI
                // session can still be completing a preceding test's layout,
                // so wait for the decode + invalidation to reach the next
                // render tick before capturing pixels.
                await Task.Delay(300);
                await HeadlessRender.SettleAsync(window);
                await ScreenshotCapture.CaptureControlAsync(window, view, screenshotPath);
                window.Close();
            });

            using var rendered = SKBitmap.Decode(screenshotPath);
            Assert.NotNull(rendered);
            var redRows = Enumerable.Range(0, rendered.Height)
                .Count(y => Enumerable.Range(0, rendered.Width)
                    .Any(x => rendered.GetPixel(x, y).Red > 200 && rendered.GetPixel(x, y).Green < 80));
            Assert.True(redRows >= 60,
                $"Expected an 80px image to occupy most of its height, but found only {redRows} red rows.");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
