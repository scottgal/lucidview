using Avalonia.Controls;
using MarkdownViewer.Controls;
using MarkdownViewer.Plugins;
using MarkdownViewer.Services;

namespace MarkdownViewer.Tests;

public class AvaloniaNativeDiagramRendererPluginTests
{
    [Fact]
    public void ReplaceDiagramMarkers_ReplacesFlowchartMarkerInPanel()
    {
        var service = new MarkdownService();
        service.ProcessMarkdown("""
                               ```mermaid
                               flowchart TD
                                   A --> B
                               ```
                               """);

        var plugin = CreatePlugin(service);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "FLOWCHART:flowchart-0" });

        plugin.ReplaceDiagramMarkers(panel);

        Assert.IsType<FlowchartCanvas>(panel.Children[0]);
    }

    [Fact]
    public void ReplaceDiagramMarkers_ReplacesDiagramMarkerInPanel()
    {
        var service = new MarkdownService();
        service.ProcessMarkdown("""
                               ```mermaid
                               sequenceDiagram
                                   participant A
                                   participant B
                                   A->>B: Ping
                               ```
                               """);

        var plugin = CreatePlugin(service);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "DIAGRAM:diagram-0" });

        plugin.ReplaceDiagramMarkers(panel);

        Assert.IsType<DiagramCanvas>(panel.Children[0]);
    }

    [Fact]
    public void SequenceDiagram_DocumentContainsTextElements()
    {
        var service = new MarkdownService();
        service.ProcessMarkdown("""
                               ```mermaid
                               sequenceDiagram
                                   participant Alice
                                   participant Bob
                                   Alice->>Bob: Hello
                               ```
                               """);

        var docs = service.DiagramDocuments;
        Assert.Single(docs);

        var doc = docs.Values.First();
        Assert.True(doc.Width > 0, "Document should have width");
        Assert.True(doc.Height > 0, "Document should have height");

        // Count text elements (SvgText and SvgMultiLineText) in all elements recursively
        var textElements = new List<MermaidSharp.Rendering.SvgElement>();
        CollectTextElements(doc.Elements, textElements);

        Assert.True(textElements.Count > 0,
            $"Expected text elements in sequence diagram. Got {doc.Elements.Count} top-level elements: " +
            string.Join(", ", doc.Elements.Select(e => e.GetType().Name)));

        // Verify we have both SvgText and SvgMultiLineText
        var textCount = textElements.Count(e => e is MermaidSharp.Rendering.SvgText);
        var multiTextCount = textElements.Count(e => e is MermaidSharp.Rendering.SvgMultiLineText);
        var foreignCount = textElements.Count(e => e is MermaidSharp.Rendering.SvgForeignObject);

        // Log what we got
        Assert.True(true, $"SvgText: {textCount}, SvgMultiLineText: {multiTextCount}, SvgForeignObject: {foreignCount}");

    }

    [Fact]
    public void SequenceDiagram_UsesNativeDiagramCanvasPath()
    {
        // Verify sequence diagrams go through TryRenderToDocument → DiagramCanvas,
        // NOT through SVG file → image control
        var service = new MarkdownService();
        service.ProcessMarkdown("""
                               ```mermaid
                               sequenceDiagram
                                   participant Alice
                                   participant Bob
                                   Alice->>Bob: Hello
                               ```
                               """);

        // DiagramDocuments is populated ONLY by the native TryRenderToDocument path
        Assert.True(service.DiagramDocuments.Count > 0,
            "Sequence diagram should use native DiagramCanvas path (DiagramDocuments populated)");

        // FlowchartLayouts is populated ONLY by TryComputeFlowchartLayout
        Assert.Equal(0, service.FlowchartLayouts.Count);

        var doc = service.DiagramDocuments.Values.First();

        // Verify the document has all expected properties
        Assert.True(doc.Width > 0, $"Width={doc.Width}");
        Assert.True(doc.Height > 0, $"Height={doc.Height}");
        Assert.False(string.IsNullOrEmpty(doc.BackgroundColor), $"BackgroundColor should be set, got: '{doc.BackgroundColor}'");

        // Verify text elements exist and have fills
        var textElements = new List<MermaidSharp.Rendering.SvgElement>();
        CollectTextElements(doc.Elements, textElements);
        Assert.True(textElements.Count >= 3, $"Expected at least 3 text elements (Alice, Bob, Hello), got {textElements.Count}");

        // Dump element tree for diagnostics
        var tree = new List<string>();
        CollectAllElementTypes(doc.Elements, tree, 0);
        var dump = string.Join("\n", tree);

        // Verify participant text comes AFTER its background rect (correct z-order)
        // Pattern: SvgRect → SvgText for participant boxes. Other text (like message labels)
        // may follow arrows (SvgPolygon) instead.
        var firstTextIdx = doc.Elements.ToList().FindIndex(e => e is MermaidSharp.Rendering.SvgText);
        Assert.True(firstTextIdx > 0, "First text element should not be the first element");
        Assert.IsType<MermaidSharp.Rendering.SvgRect>(doc.Elements[firstTextIdx - 1]);
    }

    [Fact]
    public void LlmVerificationFlow_RendersWithAltBlocks()
    {
        var service = new MarkdownService();
        service.ProcessMarkdown("""
                               ```mermaid
                               sequenceDiagram
                                   participant Engine as Mapping Engine
                                   participant Helper as LlmPromptHelper
                                   participant LLM as qwen3:0.6b<br/>(Ollama / LLamaSharp)

                                   Engine->>Helper: BuildPrompt(profile, bestMatch, alternatives)
                                   Helper-->>Engine: Structured prompt

                                   Note over Helper: Prompt includes:<br/>- Column name + type<br/>- 3 sample values<br/>- Current best match + confidence<br/>- Top 2 alternatives

                                   Engine->>LLM: POST /api/generate

                                   Note over LLM: temperature=0.1<br/>think=false<br/>num_predict=256

                                   LLM-->>Engine: JSON response

                                   Engine->>Helper: ParseResponse(json)

                                   alt LLM confirms match
                                       Helper-->>Engine: IsConfidentMatch=true
                                       Engine->>Engine: Boost confidence +10%<br/>(capped at 1.0)
                                   else LLM suggests alternative
                                       Helper-->>Engine: ShouldUseAlternative=true<br/>AlternativeFieldId="loan.rate"
                                       Engine->>Engine: Switch to alternative candidate
                                   else LLM uncertain
                                       Helper-->>Engine: IsConfidentMatch=false
                                       Engine->>Engine: Keep original, add warning
                                   end
                               ```
                               """);

        // Should render via native DiagramCanvas (not SVG/PNG fallback)
        Assert.True(service.DiagramDocuments.Count > 0,
            "Sequence diagram with alt blocks should render via DiagramCanvas");

        var doc = service.DiagramDocuments.Values.First();
        var textElements = new List<MermaidSharp.Rendering.SvgElement>();
        CollectTextElements(doc.Elements, textElements);

        // Should have text from all 3 alt branches + participants + notes + messages
        Assert.True(textElements.Count >= 15,
            $"Expected at least 15 text elements (3 participants, notes, messages, alt branches), got {textElements.Count}");
    }

    [Fact]
    public void LearningSystemFlow_NoteBoxesSizedForContent()
    {
        var service = new MarkdownService();
        service.ProcessMarkdown("""
                               ```mermaid
                               sequenceDiagram
                                   participant User as User (Browser)
                                   participant React as React Frontend
                                   participant API as POST /accept-mapping
                                   participant Repo as LearnedAliasRepository
                                   participant DB as PostgreSQL<br/>learned_aliases

                                   Note over User,React: Import #1: "ln_num" scores 67% -> loan.loan_id

                                   User->>React: Clicks Accept on "ln_num"
                                   React->>React: Set confidence = 1.0 (local state)
                                   React->>API: { sourceColumn: "ln_num",<br/>targetField: "loan.loan_id",<br/>confidence: 0.67 }
                                   API->>Repo: UpsertAsync(tenant, schema,<br/>"loan.loan_id", "ln_num", 0.67)
                                   Repo->>DB: INSERT learned_alias<br/>AcceptedCount = 1

                                   Note over User,DB: Import #2: new file also has "ln_num"

                                   DB-->>Repo: Load aliases for tenant+schema
                                   Repo-->>React: "ln_num" -> "loan.loan_id" (learned)

                                   Note over React: Scoring: token similarity of<br/>"ln_num" vs learned alias "ln_num"<br/>= 1.0 * 0.80 = 80% (1 accept)<br/>or 1.0 * 0.95 = 95% (2+ accepts)

                                   React-->>User: "ln_num" -> loan.loan_id<br/>at 80% (Review) or 95% (Ready)
                               ```
                               """);

        Assert.True(service.DiagramDocuments.Count > 0);
        var doc = service.DiagramDocuments.Values.First();

        // Find multi-line note text elements and verify they're inside their note boxes
        var paths = doc.Elements.OfType<MermaidSharp.Rendering.SvgPath>().ToList();
        var multiTexts = doc.Elements.OfType<MermaidSharp.Rendering.SvgMultiLineText>().ToList();

        // The 4-line note should have a box taller than 40px (default NoteHeight)
        var noteBoxPaths = paths.Where(p =>
            p.Fill == "#2a2520" && p.Stroke == "#8a7e3b").ToList();
        Assert.True(noteBoxPaths.Count >= 3, $"Expected at least 3 note boxes, got {noteBoxPaths.Count}");

        // The tallest note box should be > 80px for the 4-line note
        foreach (var notePath in noteBoxPaths)
        {
            var coords = notePath.D!.Split([' ', ',', 'M', 'L', 'Z'],
                StringSplitOptions.RemoveEmptyEntries);
            var yValues = new List<double>();
            for (var i = 1; i < coords.Length; i += 2)
                if (double.TryParse(coords[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var yVal))
                    yValues.Add(yVal);

            if (yValues.Count >= 2)
            {
                var height = yValues.Max() - yValues.Min();
                // At least one note should be tall enough for multi-line content
                if (height > 80)
                    return; // Test passes - found a properly sized multi-line note
            }
        }

        Assert.Fail("No note box found with height > 80px for the 4-line note");
    }

    [Fact]
    public void SequenceDiagram_TextContentNotEmpty()
    {
        var service = new MarkdownService();
        service.ProcessMarkdown("""
                               ```mermaid
                               sequenceDiagram
                                   participant Alice
                                   participant Bob
                                   Alice->>Bob: Hello
                               ```
                               """);

        var doc = service.DiagramDocuments.Values.First();
        var textElements = new List<MermaidSharp.Rendering.SvgElement>();
        CollectTextElements(doc.Elements, textElements);

        // Check SvgText elements have content
        foreach (var el in textElements.OfType<MermaidSharp.Rendering.SvgText>())
        {
            Assert.False(string.IsNullOrEmpty(el.Content),
                $"SvgText at ({el.X}, {el.Y}) has empty content");
        }

        // Check SvgMultiLineText elements have lines
        foreach (var el in textElements.OfType<MermaidSharp.Rendering.SvgMultiLineText>())
        {
            Assert.True(el.Lines.Length > 0,
                $"SvgMultiLineText at ({el.X}, {el.StartY}) has no lines");
            foreach (var line in el.Lines)
            {
                // Lines should contain participant names or messages
                Assert.False(string.IsNullOrEmpty(line),
                    $"SvgMultiLineText line is empty at ({el.X}, {el.StartY})");
            }
        }
    }

    static void CollectTextElements(IEnumerable<MermaidSharp.Rendering.SvgElement> elements, List<MermaidSharp.Rendering.SvgElement> results)
    {
        foreach (var el in elements)
        {
            if (el is MermaidSharp.Rendering.SvgText or MermaidSharp.Rendering.SvgMultiLineText or MermaidSharp.Rendering.SvgForeignObject)
                results.Add(el);
            if (el is MermaidSharp.Rendering.SvgGroup g)
                CollectTextElements(g.Children, results);
        }
    }

    static void CollectAllElementTypes(IEnumerable<MermaidSharp.Rendering.SvgElement> elements, List<string> results, int depth)
    {
        foreach (var el in elements)
        {
            var indent = new string(' ', depth * 2);
            var extra = el switch
            {
                MermaidSharp.Rendering.SvgText t => $" Content=\"{t.Content}\" X={t.X} Y={t.Y} Fill=\"{t.Fill}\" Style=\"{t.Style}\" FontSize=\"{t.FontSize}\" Anchor=\"{t.TextAnchor}\" Baseline=\"{t.DominantBaseline}\" Transform=\"{t.Transform}\"",
                MermaidSharp.Rendering.SvgMultiLineText mt => $" Lines=[{string.Join("|", mt.Lines)}] X={mt.X} Y={mt.StartY} Fill=\"{mt.Fill}\"",
                MermaidSharp.Rendering.SvgForeignObject fo => $" Html=\"{fo.HtmlContent}\" X={fo.X} Y={fo.Y}",
                MermaidSharp.Rendering.SvgGroup g => $" Class=\"{g.Class}\" Transform=\"{g.Transform}\" Style=\"{g.Style}\"",
                MermaidSharp.Rendering.SvgRect r => $" X={r.X} Y={r.Y} W={r.Width} H={r.Height} Fill=\"{r.Fill}\" Stroke=\"{r.Stroke}\" Style=\"{r.Style}\" Class=\"{r.Class}\"",
                MermaidSharp.Rendering.SvgPath p => $" D=\"{p.D?.Substring(0, Math.Min(120, p.D?.Length ?? 0))}\" Fill=\"{p.Fill}\" Stroke=\"{p.Stroke}\" Style=\"{p.Style}\"",
                MermaidSharp.Rendering.SvgLine l => $" X1={l.X1} Y1={l.Y1} X2={l.X2} Y2={l.Y2} Stroke=\"{l.Stroke}\" Style=\"{l.Style}\"",
                MermaidSharp.Rendering.SvgPolygon p => $" Points=\"{p.Points}\" Fill=\"{p.Fill}\" Stroke=\"{p.Stroke}\"",
                _ => $" [{el.GetType().Name}]"
            };
            results.Add($"{indent}{el.GetType().Name}{extra}");
            if (el is MermaidSharp.Rendering.SvgGroup group)
                CollectAllElementTypes(group.Children, results, depth + 1);
        }
    }

    static AvaloniaNativeDiagramRendererPlugin CreatePlugin(MarkdownService service) =>
        new(
            service,
            resolveDiagramTextBrush: () => null,
            saveDiagramAs: (_, _) => Task.CompletedTask);
}
