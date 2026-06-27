using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LiveMarkdown.Avalonia;
using VisualExtensions = Avalonia.VisualTree.VisualExtensions;

namespace MarkdownViewer.Views;

public partial class MainWindow
{
    // Markdown image references that look animated. We treat .gif, .webp,
    // and .apng as the targets — AnimatedImage.Avalonia handles all three.
    private static readonly Regex AnimatedImageMarkdownRegex =
        new(@"!\[[^\]]*\]\(([^)\s]+\.(?:gif|webp|apng)(?:\?[^)\s]*)?)\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // LiveMarkdown.Avalonia decodes Images via static Bitmap (first frame
    // only); we walk its post-render tree and swap the static Source for
    // AnimatedImage's AnimatedSource to actually play GIF/WebP/APNG.
    private void SchedulePromoteAnimatedImages(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return;
        var animatedUrls = ExtractAnimatedImageUrls(markdown);
        if (animatedUrls.Count == 0) return;

        // LiveMarkdown materializes images asynchronously well after the
        // first layout pass, so a single Dispatcher.Post finds zero Images.
        // Retry on a timer until we find at least one Image (or give up
        // after a few seconds).
        _ = PromoteAnimatedImagesWithRetry(animatedUrls);
    }

    // Cached SVG→PNG is rasterized at 2× for hi-DPI; without this the
    // on-screen size doubles because LiveMarkdown treats physical pixels
    // as DIPs.
    private void ScheduleConstrainCachedImages(string processedMarkdown)
    {
        if (string.IsNullOrEmpty(processedMarkdown)) return;
        var imageUrls = ExtractAllImageUrls(processedMarkdown);
        if (imageUrls.Count == 0) return;
        _ = ConstrainCachedImagesWithRetry(imageUrls);
    }

    private async Task ConstrainCachedImagesWithRetry(List<string> markdownImageUrls)
    {
        const int maxAttempts = 30;
        const int delayMs = 100;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            await Task.Delay(delayMs);

            var images = VisualExtensions
                .GetVisualDescendants(MdViewer)
                .OfType<Image>()
                .ToList();

            if (images.Count > 0)
            {
                ApplyCachedImageConstraints(images, markdownImageUrls);
                return;
            }
        }
    }

    private void ApplyCachedImageConstraints(List<Image> images, List<string> markdownImageUrls)
    {
        // Match the i-th Image control with the i-th markdown URL. This pairing
        // can drift when LiveMarkdown emits extra Image controls (inline icons,
        // list decorations) but on most pages the alignment holds. Mitigation:
        // if the Image's Source already exposes a natural-size matching the
        // cached entry, we trust the pairing; otherwise skip to avoid pinning
        // a content image to a shield's tiny dimensions.
        for (int i = 0; i < images.Count && i < markdownImageUrls.Count; i++)
        {
            var size = _imageCacheService.GetCachedDisplaySizeByLocalPath(markdownImageUrls[i]);
            if (size == null) continue;

            var img = images[i];

            // Defence against the mis-alignment bug: if the Image already has a
            // Source loaded and its bitmap dimensions don't match the looked-up
            // cache entry, skip (different image — pairing is off). Let
            // AppStyles' Stretch=Uniform DownOnly handle it from the Source.
            if (img.Source is Avalonia.Media.Imaging.Bitmap bmp
                && (Math.Abs(bmp.PixelSize.Width - size.Value.Width) > 2
                    || Math.Abs(bmp.PixelSize.Height - size.Value.Height) > 2))
                continue;

            img.Width = size.Value.Width;
            img.Height = size.Value.Height;
            img.Stretch = Stretch.Uniform;
            EnableClickToZoom(img);
        }
    }

    private void EnableClickToZoom(Image img)
    {
        // LiveMarkdown's inline image rendering can squash photos to line-height
        // regardless of the Width/Height set on the Image control. Workaround
        // until a renderer-side fix: any image you click pops open at full
        // bitmap size in a dedicated Window so the cached content is at least
        // inspectable.
        if (img.Tag as string == "click-to-zoom-wired") return;
        img.Tag = "click-to-zoom-wired";
        img.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        img.PointerPressed += (_, e) =>
        {
            if (img.Source is null) return;
            if (e.ClickCount < 1) return;
            var w = new Avalonia.Controls.Window
            {
                Title = "Image preview",
                Width = Math.Min(1400, img.Width > 0 ? img.Width * 2 : 800),
                Height = Math.Min(900, img.Height > 0 ? img.Height * 2 : 600),
                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                Content = new Avalonia.Controls.ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = new Image
                    {
                        Source = img.Source,
                        Stretch = Stretch.None,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    }
                }
            };
            w.Show(this);
        };
    }

    private static List<string> ExtractAllImageUrls(string markdown)
    {
        var urls = new List<string>();
        foreach (Match m in Regex.Matches(markdown, @"!\[[^\]]*\]\(([^)\s]+)(?:\s+""[^""]*"")?\)"))
        {
            urls.Add(m.Groups[1].Value);
        }
        return urls;
    }

    private async Task PromoteAnimatedImagesWithRetry(List<string> animatedUrls)
    {
        const int maxAttempts = 30;
        const int delayMs = 100;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            await Task.Delay(delayMs);

            var images = VisualExtensions
                .GetVisualDescendants(MdViewer)
                .OfType<Image>()
                .ToList();

            if (images.Count > 0)
            {
                PromoteAnimatedImages(animatedUrls);
                return;
            }
        }
    }

    private static List<string> ExtractAnimatedImageUrls(string markdown)
    {
        var urls = new List<string>();
        foreach (Match m in AnimatedImageMarkdownRegex.Matches(markdown))
        {
            var url = m.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(url)) urls.Add(url);
        }
        return urls;
    }

    private void PromoteAnimatedImages(List<string> animatedUrls)
    {
        if (MdViewer is null) return;

        // LiveMarkdown emits markdown images as Image controls in source order
        // and drops none, so the i-th Image maps 1-to-1 with the i-th markdown
        // image reference; we walk both in lockstep and promote where the URL
        // is animated.
        var images = VisualExtensions
            .GetVisualDescendants(MdViewer)
            .OfType<Image>()
            .ToList();

        if (images.Count == 0) return;

        var allMarkdownImageUrls = new List<string>();
        foreach (Match m in Regex.Matches(_rawContent, @"!\[[^\]]*\]\(([^)\s]+)(?:\s+""[^""]*"")?\)"))
            allMarkdownImageUrls.Add(m.Groups[1].Value);

        var animatedSet = new HashSet<string>(animatedUrls, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < images.Count && i < allMarkdownImageUrls.Count; i++)
        {
            var url = allMarkdownImageUrls[i];
            if (!animatedSet.Contains(url)) continue;

            try
            {
                var resolvedUri = ResolveImageUriForAnimation(url);
                if (resolvedUri is null) continue;

                AnimatedImage.Avalonia.ImageBehavior.SetAnimatedSource(images[i], resolvedUri);
                AttachGifPlaybackOverlay(images[i], resolvedUri);
            }
            catch (Exception ex) when (ex is IOException or UriFormatException)
            {
            }
        }
    }

    // Restart-only overlay — AnimatedImage.Avalonia has no public pause/resume,
    // and AdornerLayer is unreliable for Images inside LiveMarkdown's content
    // tree before the first arrange, so wrap inline instead.
    private static void AttachGifPlaybackOverlay(Image image, Uri uri)
        => WrapImageWithRestartButton(image, uri);

    private static readonly AttachedProperty<bool> GifOverlayAttachedProperty =
        AvaloniaProperty.RegisterAttached<MainWindow, Image, bool>("GifOverlayAttached");

    private static void WrapImageWithRestartButton(Image image, Uri uri)
    {
        if (image.GetValue(GifOverlayAttachedProperty) is true) return;

        // Find a Panel ancestor we can replace children of.
        Visual? current = image;
        Panel? parent = null;
        Visual? child = image;
        while (current != null)
        {
            current = VisualExtensions.GetVisualParent(current);
            if (current is Panel p)
            {
                parent = p;
                break;
            }
            if (current is Visual v) child = v;
        }

        if (parent is null || child is null) return;

        var idx = parent.Children.IndexOf((Control)child);
        if (idx < 0) return;

        var restartButton = BuildRestartButton(image, uri);
        var overlay = new Panel
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        overlay.Children.Add(restartButton);

        parent.Children.RemoveAt(idx);

        var wrapper = new Grid
        {
            HorizontalAlignment = ((Control)child).HorizontalAlignment,
            VerticalAlignment = ((Control)child).VerticalAlignment,
        };
        wrapper.Children.Add((Control)child);
        wrapper.Children.Add(overlay);

        parent.Children.Insert(idx, wrapper);
        image.SetValue(GifOverlayAttachedProperty, true);

        // Register the button in EVERY NameScope on the way up to the root
        // so any FindControl<>(name) call finds it regardless of which scope
        // it walks. The local NameScope of LiveMarkdown's TextBlock template
        // has its own scope and doesn't propagate up to the Window's scope.
        var visited = new HashSet<INameScope>();
        for (Visual? walk = parent; walk != null; walk = VisualExtensions.GetVisualParent(walk))
        {
            var ns = NameScope.GetNameScope(walk);
            if (ns != null && visited.Add(ns))
            {
                // Duplicate-name on re-render is expected and benign.
                try { ns.Register("GifRestartBtn", restartButton); }
                catch (ArgumentException) { }
            }
        }
    }

    private static Button BuildRestartButton(Image image, Uri uri)
    {
        var btn = new Button
        {
            Name = "GifRestartBtn",
            Width = 28,
            Height = 28,
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x00, 0x00, 0x00)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            [ToolTip.TipProperty] = "Restart animation",
            Content = new FluentIcons.Avalonia.Fluent.SymbolIcon
            {
                Symbol = FluentIcons.Common.Symbol.ArrowClockwise,
                FontSize = 16,
                Foreground = Brushes.White
            }
        };
        btn.Click += (_, _) =>
        {
            AnimatedImage.Avalonia.ImageBehavior.SetAnimatedSource(image, null!);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AnimatedImage.Avalonia.ImageBehavior.SetAnimatedSource(image, uri);
            });
        };
        return btn;
    }

    private Uri? ResolveImageUriForAnimation(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme == "http" || absolute.Scheme == "https" ||
                absolute.Scheme == "file" || absolute.Scheme == "avares")
                return absolute;
        }

        // Document-relative path: resolve against the current file's directory.
        var basePath = _currentFilePath != null ? Path.GetDirectoryName(_currentFilePath) : null;
        if (!string.IsNullOrEmpty(basePath))
        {
            var combined = Path.GetFullPath(Path.Combine(basePath, url));
            if (File.Exists(combined))
                return new Uri(combined);
        }

        return null;
    }
}
