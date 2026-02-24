using MermaidSharp;
using MermaidSharp.Rendering;
using MarkdownViewer.Services;

namespace MarkdownViewer.Tests;

public class SequenceDiagramTests
{
    [Fact]
    public void Render_SimpleSequence_ProducesValidSvg()
    {
        var svg = Mermaid.Render("""
            sequenceDiagram
                Alice->>Bob: Hello
                Bob-->>Alice: Hi
            """);

        Assert.NotNull(svg);
        Assert.Contains("<svg", svg);
        Assert.Contains("Alice", svg);
        Assert.Contains("Bob", svg);
        Assert.Contains("Hello", svg);
    }

    [Fact]
    public void Render_WithParticipants_ShowsParticipantNames()
    {
        var svg = Mermaid.Render("""
            sequenceDiagram
                participant User
                participant API
                participant DB
                User->>API: GET /data
                API->>DB: SELECT
                DB-->>API: Results
                API-->>User: 200 OK
            """);

        Assert.NotNull(svg);
        Assert.Contains("User", svg);
        Assert.Contains("API", svg);
        Assert.Contains("DB", svg);
        Assert.Contains("GET /data", svg);
    }

    [Fact]
    public void Render_WithActors_ProducesSvg()
    {
        var svg = Mermaid.Render("""
            sequenceDiagram
                actor Customer
                participant System
                Customer->>System: Login
                System-->>Customer: Welcome
            """);

        Assert.NotNull(svg);
        Assert.Contains("Customer", svg);
        Assert.Contains("System", svg);
    }

    [Fact]
    public void Render_WithNotes_IncludesNoteText()
    {
        var svg = Mermaid.Render("""
            sequenceDiagram
                Alice->>Bob: Request
                Note right of Bob: Process request
                Bob-->>Alice: Response
            """);

        Assert.NotNull(svg);
        Assert.Contains("Process request", svg);
    }

    [Fact]
    public void Render_WithAutoNumber_ProducesSvg()
    {
        var svg = Mermaid.Render("""
            sequenceDiagram
                autonumber
                Alice->>Bob: Step 1
                Bob->>Charlie: Step 2
                Charlie-->>Alice: Step 3
            """);

        Assert.NotNull(svg);
        Assert.Contains("Alice", svg);
        Assert.Contains("Charlie", svg);
    }

    [Fact]
    public void Render_WithActivation_ProducesSvg()
    {
        var svg = Mermaid.Render("""
            sequenceDiagram
                Alice->>+Bob: Request
                Bob-->>-Alice: Response
            """);

        Assert.NotNull(svg);
        Assert.Contains("<svg", svg);
    }

    [Fact]
    public void Render_DarkTheme_ProducesDifferentColors()
    {
        var lightSvg = Mermaid.Render("""
            sequenceDiagram
                Alice->>Bob: Hello
            """);

        var darkSvg = Mermaid.Render("""
            sequenceDiagram
                Alice->>Bob: Hello
            """, new RenderOptions { Theme = "dark" });

        Assert.NotNull(lightSvg);
        Assert.NotNull(darkSvg);
        // Both should be valid SVGs but with different color schemes
        Assert.Contains("<svg", lightSvg);
        Assert.Contains("<svg", darkSvg);
        // Dark theme uses dark background
        Assert.Contains("#0d1117", darkSvg);
    }

    [Fact]
    public void RenderToDocument_ReturnsNonNullDocument()
    {
        var doc = Mermaid.RenderToDocument("""
            sequenceDiagram
                Alice->>Bob: Hello
                Bob-->>Alice: Hi
            """);

        Assert.NotNull(doc);
        Assert.True(doc.Width > 0);
        Assert.True(doc.Height > 0);
    }

    [Fact]
    public void ProcessMarkdown_SequenceBlock_ProducesDiagramMarker()
    {
        var service = new MarkdownService();
        var processed = service.ProcessMarkdown("""
            # Test

            ```mermaid
            sequenceDiagram
                participant User
                participant Server
                User->>Server: Request
                Server-->>User: Response
            ```
            """);

        Assert.Contains("DIAGRAM:diagram-0", processed);
        Assert.DoesNotContain("```mermaid", processed);
        Assert.Single(service.DiagramDocuments);
    }

    [Fact]
    public void ProcessMarkdown_MultipleSequenceDiagrams_AllGetMarkers()
    {
        var service = new MarkdownService();
        var processed = service.ProcessMarkdown("""
            ```mermaid
            sequenceDiagram
                A->>B: First
            ```

            ```mermaid
            sequenceDiagram
                C->>D: Second
            ```
            """);

        Assert.Contains("DIAGRAM:diagram-0", processed);
        Assert.Contains("DIAGRAM:diagram-1", processed);
        Assert.Equal(2, service.DiagramDocuments.Count);
    }
}
