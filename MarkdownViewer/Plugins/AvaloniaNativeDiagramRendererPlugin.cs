using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using MarkdownViewer.Controls;
using MarkdownViewer.Services;
using VisualExtensions = Avalonia.VisualTree.VisualExtensions;

namespace MarkdownViewer.Plugins;

public sealed class AvaloniaNativeDiagramRendererPlugin(
    MarkdownService markdownService,
    Func<IBrush?> resolveDiagramTextBrush,
    Func<string, string, Task> saveDiagramAs,
    Action<string>? openExternalLink = null,
    Action<string>? scrollToDiagram = null) : IDiagramRendererPlugin
{
    public string Name => "avalonia-native";

    public void ReplaceDiagramMarkers(Visual root)
    {
        var flowchartLayouts = markdownService.FlowchartLayouts;
        var diagramDocs = markdownService.DiagramDocuments;
        if (flowchartLayouts.Count == 0 && diagramDocs.Count == 0) return;

        var markers = new List<(TextBlock TextBlock, string Prefix, string Key)>();
        FindDiagramMarkers(root, markers);

        Debug.WriteLine(
            $"[DiagramCanvas:{Name}] Found {markers.Count} markers, {flowchartLayouts.Count} flowcharts, {diagramDocs.Count} diagrams");

        foreach (var (textBlock, prefix, key) in markers)
        {
            Control? replacement = null;

            if (prefix == MarkdownService.FlowchartMarkerPrefix)
            {
                var layout = markdownService.GetFlowchartLayout(key);
                if (layout is null)
                {
                    Debug.WriteLine($"[DiagramCanvas:{Name}] No flowchart layout for key '{key}'");
                    continue;
                }

                Debug.WriteLine($"[DiagramCanvas:{Name}] Replacing flowchart '{key}' — {layout.Width:F0}x{layout.Height:F0}");

                var canvas = new FlowchartCanvas
                {
                    Layout = layout,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };

                canvas.LinkClicked += (_, link) => OpenLink(link);
                replacement = canvas;
            }
            else if (prefix == MarkdownService.DiagramMarkerPrefix)
            {
                if (!diagramDocs.TryGetValue(key, out var doc))
                {
                    Debug.WriteLine($"[DiagramCanvas:{Name}] No document for key '{key}'");
                    continue;
                }

                Debug.WriteLine($"[DiagramCanvas:{Name}] Replacing diagram '{key}' — {doc.Width:F0}x{doc.Height:F0}");

                var canvas = new DiagramCanvas
                {
                    Document = doc,
                    DefaultTextBrush = resolveDiagramTextBrush(),
                    ZoomTargets = markdownService.C4ZoomTargets,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };
                canvas.LinkClicked += (_, target) =>
                {
                    if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        OpenLink(target);
                    else
                        scrollToDiagram?.Invoke(target);
                };
                replacement = canvas;
            }

            if (replacement is null) continue;

            var mermaidCode = markdownService.MermaidDiagrams.GetValueOrDefault(key);
            if (mermaidCode is not null)
            {
                var contextMenu = new ContextMenu();
                var savePng = new MenuItem { Header = "Save Diagram as PNG..." };
                savePng.Click += (_, _) => _ = saveDiagramAs(mermaidCode, "png");
                var saveSvg = new MenuItem { Header = "Save Diagram as SVG..." };
                saveSvg.Click += (_, _) => _ = saveDiagramAs(mermaidCode, "svg");
                contextMenu.Items.Add(savePng);
                contextMenu.Items.Add(saveSvg);
                replacement.ContextMenu = contextMenu;
            }

            ReplaceControlInVisualTree(textBlock, replacement);
        }
    }

    void OpenLink(string link)
    {
        if (openExternalLink is not null)
        {
            try
            {
                openExternalLink(link);
            }
            catch
            {
                // Ignore failed link navigation.
            }

            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch
        {
            // Ignore failed link navigation.
        }
    }

    static void FindDiagramMarkers(Visual parent, List<(TextBlock, string Prefix, string Key)> results)
    {
        foreach (var child in VisualExtensions.GetVisualChildren(parent))
        {
            if (child is TextBlock textBlock)
            {
                var match = ExtractDiagramMarkerKey(textBlock);
                if (match is not null)
                {
                    results.Add((textBlock, match.Value.Prefix, match.Value.Key));
                    continue;
                }
            }

            if (child is Visual visual)
            {
                FindDiagramMarkers(visual, results);
            }
        }
    }

    static (string Prefix, string Key)? ExtractDiagramMarkerKey(TextBlock textBlock)
    {
        var text = textBlock.Text;

        if (string.IsNullOrEmpty(text) && textBlock.Inlines is { Count: > 0 } inlines)
        {
            text = string.Concat(inlines.OfType<Run>().Select(r => r.Text ?? ""));
        }

        if (string.IsNullOrEmpty(text)) return null;

        var idx = text.IndexOf(MarkdownService.FlowchartMarkerPrefix, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var key = text[(idx + MarkdownService.FlowchartMarkerPrefix.Length)..].Trim();
            return string.IsNullOrEmpty(key) ? null : (MarkdownService.FlowchartMarkerPrefix, key);
        }

        idx = text.IndexOf(MarkdownService.DiagramMarkerPrefix, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var key = text[(idx + MarkdownService.DiagramMarkerPrefix.Length)..].Trim();
            return string.IsNullOrEmpty(key) ? null : (MarkdownService.DiagramMarkerPrefix, key);
        }

        return null;
    }

    static void ReplaceControlInVisualTree(Control target, Control replacement)
    {
        Visual? current = target;
        while (current is not null)
        {
            var parent = VisualExtensions.GetVisualParent(current);
            if (parent is null) break;

            if (parent is Panel panel && current is Control ctrl)
            {
                var index = panel.Children.IndexOf(ctrl);
                if (index >= 0)
                {
                    panel.Children[index] = replacement;
                    panel.InvalidateMeasure();
                    panel.InvalidateArrange();
                    return;
                }
            }
            else if (parent is ContentControl contentControl && contentControl.Content == current)
            {
                contentControl.Content = replacement;
                return;
            }
            else if (parent is Decorator decorator && decorator.Child == current as Control)
            {
                decorator.Child = replacement;
                return;
            }

            current = parent;
        }
    }
}
