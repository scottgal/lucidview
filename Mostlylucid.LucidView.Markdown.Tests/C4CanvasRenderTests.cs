using Avalonia;
using Avalonia.Media.Imaging;
using MermaidSharp;
using Mostlylucid.LucidView.Markdown.Controls;
using SkiaSharp;
using Xunit;

namespace Mostlylucid.LucidView.Markdown.Tests;

[Collection("Avalonia")]
public class C4CanvasRenderTests
{
    private readonly HeadlessAvaloniaFixture _fx;
    public C4CanvasRenderTests(HeadlessAvaloniaFixture fx) => _fx = fx;

    [Fact]
    public Task Renders_c4_context_to_pixels()
    {
        return _fx.DispatchAsync(() =>
        {
            var layout = Mermaid.ParseAndLayoutC4(
                """
                C4Context
                    title Ownership
                    Person(user, "User", "A person")
                    System(auth, "Auth", "owned by alpha-")
                    System(api, "API", "owned by beta-")
                    Rel(user, auth, "signs in")
                    Rel(auth, api, "authorises")
                    UpdateElementStyle(auth, $bgColor="#4CDB6E")
                    UpdateElementStyle(api, $bgColor="#E5A05A")
                """);
            Assert.NotNull(layout);

            var canvas = new C4Canvas { Layout = layout, Width = layout!.Width, Height = layout.Height };
            canvas.Measure(new Size(layout.Width, layout.Height));
            canvas.Arrange(new Rect(0, 0, layout.Width, layout.Height));

            var pixelSize = new PixelSize(
                Math.Max(1, (int)Math.Ceiling(layout.Width)),
                Math.Max(1, (int)Math.Ceiling(layout.Height)));
            using var rtb = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
            rtb.Render(canvas);

            using var ms = new MemoryStream();
            rtb.Save(ms);
            ms.Position = 0;
            using var sk = SKBitmap.Decode(ms);
            Assert.NotNull(sk);

            var painted = 0;
            for (var y = 0; y < sk.Height; y += 2)
            for (var x = 0; x < sk.Width; x += 2)
                if (sk.GetPixel(x, y).Alpha > 10)
                    painted++;

            Assert.True(painted > 200, $"C4Canvas should paint the diagram; painted={painted}");
        });
    }
}
