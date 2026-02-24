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

    readonly record struct MarkerTarget(Visual Visual, string Prefix, string Key);

    public void ReplaceDiagramMarkers(Visual root)
    {
        var flowchartLayouts = markdownService.FlowchartLayouts;
        var diagramDocs = markdownService.DiagramDocuments;
        if (flowchartLayouts.Count == 0 && diagramDocs.Count == 0) return;

        var markers = new List<MarkerTarget>();
        FindDiagramMarkers(root, markers);

        Debug.WriteLine(
            $"[DiagramCanvas:{Name}] Found {markers.Count} markers, {flowchartLayouts.Count} flowcharts, {diagramDocs.Count} diagrams");

        foreach (var marker in markers)
        {
            Control? replacement = null;

            if (marker.Prefix == MarkdownService.FlowchartMarkerPrefix)
            {
                var layout = markdownService.GetFlowchartLayout(marker.Key);
                if (layout is null)
                {
                    Debug.WriteLine($"[DiagramCanvas:{Name}] No flowchart layout for key '{marker.Key}'");
                    continue;
                }

                Debug.WriteLine($"[DiagramCanvas:{Name}] Replacing flowchart '{marker.Key}' - {layout.Width:F0}x{layout.Height:F0}");

                var canvas = new FlowchartCanvas
                {
                    Layout = layout,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };

                canvas.LinkClicked += (_, link) => OpenLink(link);
                replacement = canvas;
            }
            else if (marker.Prefix == MarkdownService.DiagramMarkerPrefix)
            {
                if (!diagramDocs.TryGetValue(marker.Key, out var doc))
                {
                    Debug.WriteLine($"[DiagramCanvas:{Name}] No document for key '{marker.Key}'");
                    continue;
                }

                Debug.WriteLine($"[DiagramCanvas:{Name}] Replacing diagram '{marker.Key}' - {doc.Width:F0}x{doc.Height:F0}");

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

            var mermaidCode = markdownService.MermaidDiagrams.GetValueOrDefault(marker.Key);
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

            if (!ReplaceControlInVisualTree(marker.Visual, replacement))
            {
                Debug.WriteLine($"[DiagramCanvas:{Name}] Failed to replace marker for key '{marker.Key}' ({marker.Visual.GetType().Name})");
            }
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

    static void FindDiagramMarkers(Visual parent, ICollection<MarkerTarget> results)
    {
        FindDiagramMarkers(parent, results, new HashSet<Visual>());
    }

    static void FindDiagramMarkers(Visual parent, ICollection<MarkerTarget> results, HashSet<Visual> visited)
    {
        if (!visited.Add(parent))
            return;

        var selfMatch = ExtractDiagramMarker(parent);
        if (selfMatch is not null)
            results.Add(new MarkerTarget(parent, selfMatch.Value.Prefix, selfMatch.Value.Key));

        foreach (var child in VisualExtensions.GetVisualChildren(parent))
        {
            var match = ExtractDiagramMarker(child);
            if (match is not null)
            {
                results.Add(new MarkerTarget(child, match.Value.Prefix, match.Value.Key));
            }

            if (child is Visual visual)
            {
                FindDiagramMarkers(visual, results, visited);
            }
        }

        if (parent is ContentControl contentControl && contentControl.Content is Visual contentVisual)
        {
            FindDiagramMarkers(contentVisual, results, visited);
        }
    }

    static (string Prefix, string Key)? ExtractDiagramMarker(Visual visual)
    {
        var text = TryGetVisualText(visual);
        if (string.IsNullOrEmpty(text)) return null;

        var idx = text.IndexOf(MarkdownService.FlowchartMarkerPrefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var key = ExtractMarkerKey(text, idx + MarkdownService.FlowchartMarkerPrefix.Length);
            return string.IsNullOrEmpty(key) ? null : (MarkdownService.FlowchartMarkerPrefix, key);
        }

        idx = text.IndexOf(MarkdownService.DiagramMarkerPrefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var key = ExtractMarkerKey(text, idx + MarkdownService.DiagramMarkerPrefix.Length);
            return string.IsNullOrEmpty(key) ? null : (MarkdownService.DiagramMarkerPrefix, key);
        }

        return null;
    }

    static string? TryGetVisualText(Visual visual)
    {
        if (visual is TextBlock textBlock)
        {
            if (!string.IsNullOrEmpty(textBlock.Text))
                return textBlock.Text;
            if (textBlock.Inlines is { Count: > 0 } inlines)
                return string.Concat(inlines.OfType<Run>().Select(r => r.Text ?? ""));
        }

        return null;
    }

    static string ExtractMarkerKey(string text, int startIndex)
    {
        var i = startIndex;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;

        var keyStart = i;
        while (i < text.Length)
        {
            var ch = text[i];
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_')
            {
                i++;
                continue;
            }

            break;
        }

        return i > keyStart ? text[keyStart..i] : string.Empty;
    }

    static bool ReplaceControlInVisualTree(Visual target, Control replacement)
    {
        Visual? current = target;
        while (current is not null)
        {
            var parent = GetParentVisual(current);
            if (current is Control ctrl && parent is not null)
            {
                if (parent is Panel panel)
                {
                    var index = panel.Children.IndexOf(ctrl);
                    if (index >= 0)
                    {
                        panel.Children[index] = replacement;
                        panel.InvalidateMeasure();
                        panel.InvalidateArrange();
                        return true;
                    }
                }
                else if (parent is ContentControl contentControl && contentControl.Content == ctrl)
                {
                    contentControl.Content = replacement;
                    return true;
                }
                else if (parent is Decorator decorator && decorator.Child == ctrl)
                {
                    decorator.Child = replacement;
                    return true;
                }
            }

            current = parent;
        }

        return false;
    }

    static Visual? GetParentVisual(Visual visual) =>
        VisualExtensions.GetVisualParent(visual) ??
        (visual as StyledElement)?.Parent as Visual;

}
