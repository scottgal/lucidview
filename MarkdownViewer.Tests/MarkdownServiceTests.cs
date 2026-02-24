using MarkdownViewer.Services;
using MarkdownViewer.Models;

namespace MarkdownViewer.Tests;

public class MarkdownServiceTests
{
    private readonly MarkdownService _service = new();

    #region Metadata Extraction Tests

    [Fact]
    public void ExtractMetadata_WithCategories_ReturnsCategories()
    {
        // Arrange
        var content = @"# Test
<!--category-- ASP.NET, PostgreSQL, Search, RRF -->
Some content here.";

        // Act
        var metadata = MarkdownService.ExtractMetadata(content);

        // Assert
        Assert.True(metadata.HasMetadata);
        Assert.Equal(4, metadata.Categories.Count);
        Assert.Contains("ASP.NET", metadata.Categories);
        Assert.Contains("PostgreSQL", metadata.Categories);
        Assert.Contains("Search", metadata.Categories);
        Assert.Contains("RRF", metadata.Categories);
    }

    [Fact]
    public void ExtractMetadata_WithDateTime_ReturnsPublicationDate()
    {
        // Arrange
        var content = @"# Test
<datetime class=""hidden"">2026-01-14T12:00</datetime>
Some content here.";

        // Act
        var metadata = MarkdownService.ExtractMetadata(content);

        // Assert
        Assert.True(metadata.HasMetadata);
        Assert.NotNull(metadata.PublicationDate);
        Assert.Equal(2026, metadata.PublicationDate!.Value.Year);
        Assert.Equal(1, metadata.PublicationDate.Value.Month);
        Assert.Equal(14, metadata.PublicationDate.Value.Day);
    }

    [Fact]
    public void ExtractMetadata_WithBothTags_ReturnsBoth()
    {
        // Arrange
        var content = @"# Test
<!--category-- Testing, Mermaid -->
<datetime class=""hidden"">2026-01-12T14:00</datetime>
Content here.";

        // Act
        var metadata = MarkdownService.ExtractMetadata(content);

        // Assert
        Assert.True(metadata.HasMetadata);
        Assert.Equal(2, metadata.Categories.Count);
        Assert.NotNull(metadata.PublicationDate);
    }

    [Fact]
    public void ExtractMetadata_WithNoTags_ReturnsEmptyMetadata()
    {
        // Arrange
        var content = @"# Simple Document
Just plain markdown here.";

        // Act
        var metadata = MarkdownService.ExtractMetadata(content);

        // Assert
        Assert.False(metadata.HasMetadata);
        Assert.Empty(metadata.Categories);
        Assert.Null(metadata.PublicationDate);
    }

    [Fact]
    public void ExtractMetadata_CategoriesAreTrimmed()
    {
        // Arrange
        var content = "<!--category--   Spaces  ,  Around  ,  Values  -->";

        // Act
        var metadata = MarkdownService.ExtractMetadata(content);

        // Assert
        Assert.Equal(3, metadata.Categories.Count);
        Assert.Contains("Spaces", metadata.Categories);
        Assert.Contains("Around", metadata.Categories);
        Assert.Contains("Values", metadata.Categories);
    }

    #endregion

    #region Markdown Processing Tests

    [Fact]
    public void ProcessMarkdown_RemovesMetadataTags()
    {
        // Arrange
        var content = @"# Title
<!--category-- Test, Category -->
<datetime class=""hidden"">2026-01-14T12:00</datetime>
Body content here.";

        // Act
        var processed = _service.ProcessMarkdown(content);

        // Assert
        Assert.DoesNotContain("<!--category--", processed);
        Assert.DoesNotContain("<datetime", processed);
        Assert.Contains("# Title", processed);
        Assert.Contains("Body content here", processed);
    }

    [Fact]
    public void ProcessMarkdown_ConvertsMermaidToCodeBlock()
    {
        // Arrange
        var content = @"# Diagram Test

```mermaid
flowchart TD
    A --> B
```

After diagram.";

        // Act
        var processed = _service.ProcessMarkdown(content);

        // Assert — flowchart mermaid blocks are replaced with native rendering markers
        Assert.Contains("FLOWCHART:flowchart-0", processed);
        Assert.DoesNotContain("```mermaid", processed);
        // The raw mermaid code should NOT appear — it's been replaced
        Assert.DoesNotContain("```", processed.Replace("```csharp", ""));
    }

    [Fact]
    public void ProcessMarkdown_PreservesRegularCodeBlocks()
    {
        // Arrange
        var content = @"# Code Test

```csharp
public class Test { }
```

```javascript
console.log('hello');
```";

        // Act
        var processed = _service.ProcessMarkdown(content);

        // Assert
        Assert.Contains("```csharp", processed);
        Assert.Contains("public class Test", processed);
        Assert.Contains("```javascript", processed);
        Assert.Contains("console.log", processed);
    }

    [Fact]
    public void ProcessMarkdown_HandlesMultipleMermaidBlocks()
    {
        // Arrange
        var content = @"# Multiple Diagrams

```mermaid
flowchart LR
    A --> B
```

Some text.

```mermaid
sequenceDiagram
    User->>Server: Request
```";

        // Act
        var processed = _service.ProcessMarkdown(content);

        // Assert — mermaid blocks should be replaced with native markers or images
        Assert.DoesNotContain("```mermaid", processed);
        // Flowchart uses native FlowchartCanvas marker, sequence uses native DiagramCanvas marker
        Assert.True(
            processed.Contains("FLOWCHART:") || processed.Contains("DIAGRAM:") || processed.Contains("Mermaid Diagram"),
            "Should contain a native diagram marker or rendered image");
        // Raw mermaid code should be replaced, not present as text
        Assert.DoesNotContain("flowchart LR", processed);
    }

    [Fact]
    public void ProcessMarkdown_NonFlowchartMermaid_RendersAsNativeDiagramMarker()
    {
        // Arrange
        var content = @"# Sequence

```mermaid
sequenceDiagram
    participant User
    participant Server
    User->>Server: Ping
```";

        // Act
        var processed = _service.ProcessMarkdown(content);

        // Assert
        Assert.DoesNotContain("```mermaid", processed);
        Assert.Contains("DIAGRAM:diagram-0", processed);
        Assert.DoesNotContain("![Mermaid Diagram](", processed);
        Assert.Single(_service.DiagramDocuments);
    }

    [Fact]
    public void ProcessMarkdown_ComplexFlowchartWithLessThanLabel_RendersWithoutParseError()
    {
        // Arrange
        var content = """
                      ```mermaid
                      flowchart TB
                          TOK[Token similarity]
                          TYPE[Type compatibility]
                          PAT[Pattern matching]
                          FEAT[Feature group scoring]
                          FIT[Data fit scoring]
                          LEARN_BOOST[Learned alias boost]
                          MOV{Margin of<br/>victory < 7%?}
                          GATE{Confidence<br/>< 80%?}
                          OLLAMA[qwen3:0.6b<br/>via Ollama or LLamaSharp]

                          TOK & TYPE & PAT & FEAT & FIT & LEARN_BOOST --> MOV
                          MOV -->|Yes: cap to 79%| GATE
                          GATE -->|Yes: < 80%| OLLAMA
                      ```
                      """;

        // Act
        var processed = _service.ProcessMarkdown(content);

        // Assert
        Assert.DoesNotContain("Mermaid parse error", processed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cannot render", processed, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            processed.Contains("FLOWCHART:") || processed.Contains("![Mermaid Diagram]("),
            "Flowchart should render natively or fall back to SVG image.");
    }

    [Fact]
    public void ProcessMarkdown_PreservesHeadings()
    {
        // Arrange
        var content = @"# Heading 1
## Heading 2
### Heading 3";

        // Act
        var processed = _service.ProcessMarkdown(content);

        // Assert
        Assert.Contains("# Heading 1", processed);
        Assert.Contains("## Heading 2", processed);
        Assert.Contains("### Heading 3", processed);
    }

    [Fact]
    public void ProcessMarkdown_PreservesLists()
    {
        // Arrange
        var content = @"- Item 1
- Item 2
  - Nested
1. Numbered
2. List";

        // Act
        var processed = _service.ProcessMarkdown(content);

        // Assert
        Assert.Contains("- Item 1", processed);
        Assert.Contains("- Nested", processed);
        Assert.Contains("1. Numbered", processed);
    }

    [Fact]
    public void ProcessMarkdown_PreservesTables()
    {
        // Arrange
        var content = @"| Column 1 | Column 2 |
|----------|----------|
| Data 1   | Data 2   |";

        // Act
        var processed = _service.ProcessMarkdown(content);

        // Assert
        Assert.Contains("| Column 1 |", processed);
        Assert.Contains("| Data 1   |", processed);
    }

    #endregion
}
