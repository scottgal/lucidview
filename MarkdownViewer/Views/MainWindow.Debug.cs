using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media.Imaging;
using VisualExtensions = Avalonia.VisualTree.VisualExtensions;

namespace MarkdownViewer.Views;

// Ctrl+F12 utility: dumps a window screenshot, visual tree, and flowchart
// layout summary into %AppData%/MarkdownViewer/debug/. Always present so
// the keybinding works in Release too; output is non-fatal on failure.
public partial class MainWindow
{
    private async Task DebugScreenshot()
    {
        try
        {
            var debugDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MarkdownViewer", "debug");
            Directory.CreateDirectory(debugDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var pixelSize = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            if (pixelSize.Width > 0 && pixelSize.Height > 0)
            {
                var bitmap = new RenderTargetBitmap(pixelSize);
                bitmap.Render(this);
                var screenshotPath = Path.Combine(debugDir, $"screenshot_{timestamp}.png");
                bitmap.Save(screenshotPath);
                Debug.WriteLine($"[Debug] Screenshot saved: {screenshotPath}");
            }

            var treePath = Path.Combine(debugDir, $"vtree_{timestamp}.txt");
            var sb = new System.Text.StringBuilder();
            DumpVisualTreeToString(MdViewer, 0, 12, sb);
            await File.WriteAllTextAsync(treePath, sb.ToString());
            Debug.WriteLine($"[Debug] Visual tree saved: {treePath}");

            var infoPath = Path.Combine(debugDir, $"flowchart_info_{timestamp}.txt");
            var infoSb = new System.Text.StringBuilder();
            infoSb.AppendLine($"FlowchartLayouts count: {_markdownService.FlowchartLayouts.Count}");
            foreach (var (key, layout) in _markdownService.FlowchartLayouts)
            {
                infoSb.AppendLine($"  Key='{key}' Nodes={layout.Model.Nodes.Count} Size={layout.Width:F0}x{layout.Height:F0}");
            }
            await File.WriteAllTextAsync(infoPath, infoSb.ToString());

            Debug.WriteLine($"[Debug] All debug files saved to: {debugDir}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Debug] Screenshot failed: {ex.Message}");
        }
    }

    private static void DumpVisualTreeToString(Visual parent, int depth, int maxDepth, System.Text.StringBuilder sb)
    {
        if (depth > maxDepth) return;
        var indent = new string(' ', depth * 2);
        foreach (var child in VisualExtensions.GetVisualChildren(parent))
        {
            var typeName = child.GetType().Name;
            var extra = "";
            if (child is TextBlock tb)
            {
                var text = tb.Text ?? "(null Text)";
                if (tb.Text is null && tb.Inlines is { Count: > 0 } inl)
                {
                    var inlineTexts = string.Concat(inl.OfType<Run>().Select(r => r.Text ?? ""));
                    text = $"(Inlines[{inl.Count}]: {inlineTexts})";
                }
                var hexPrefix = text.Length > 0 ? $" hex[0..3]={string.Join(" ", text.Take(3).Select(c => $"U+{(int)c:X4}"))}" : "";
                extra = $" Text=\"{(text.Length > 100 ? text[..100] + "..." : text)}\"{hexPrefix}";
            }
            else if (child is Image img)
            {
                extra = $" Source={img.Source?.GetType().Name} W={img.Width} H={img.Height}";
            }
            else if (child is Control ctrl)
            {
                extra = $" W={ctrl.Bounds.Width:F0} H={ctrl.Bounds.Height:F0}";
            }

            sb.AppendLine($"{indent}{typeName}{extra}");

            if (child is Visual v)
            {
                DumpVisualTreeToString(v, depth + 1, maxDepth, sb);
            }
        }
    }
}
