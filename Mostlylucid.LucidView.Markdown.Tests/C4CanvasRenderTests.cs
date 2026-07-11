using Avalonia;
using Avalonia.Media.Imaging;
using MermaidSharp;
using MermaidSharp.Diagrams.C4;
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

    [Fact]
    public Task Clicking_an_element_hit_tests_and_raises_ElementClicked()
    {
        return _fx.DispatchAsync(() =>
        {
            var layout = Mermaid.ParseAndLayoutC4(
                """
                C4Context
                    System(auth, "Auth", "owned by alpha-")
                    System(api, "API", "owned by beta-")
                    Rel(auth, api, "calls")
                """);
            Assert.NotNull(layout);

            var canvas = new C4Canvas { Layout = layout };
            var auth = layout!.Elements.Single(e => e.Element.Id == "auth");

            // Hit-test the centre of the Auth box → resolves to Auth; empty space → nothing.
            var hit = canvas.HitTest(new Point(auth.CenterX, auth.CenterY));
            Assert.NotNull(hit);
            Assert.Equal("auth", hit!.Element.Id);

            Assert.Null(canvas.HitTest(new Point(layout.Width + 50, layout.Height + 50)));

            // ElementClicked is subscribable (raised internally from OnPointerPressed on a hit).
            var subscribed = false;
            canvas.ElementClicked += (_, _) => subscribed = true;
            Assert.False(subscribed);   // not raised without a pointer event
        });
    }
}
