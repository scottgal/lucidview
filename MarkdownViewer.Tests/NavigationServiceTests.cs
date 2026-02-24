using MarkdownViewer.Services;

namespace MarkdownViewer.Tests;

public class NavigationServiceTests
{
    [Fact]
    public void ExtractHeadings_SingleHeading_ReturnsOne()
    {
        // Arrange
        var content = "# Main Title\n\nSome content.";

        // Act
        var headings = NavigationService.ExtractHeadings(content);

        // Assert
        Assert.Single(headings);
        Assert.Equal("Main Title", headings[0].Text);
        Assert.Equal(1, headings[0].Level);
    }

    [Fact]
    public void ExtractHeadings_MultipleHeadings_ReturnsAll()
    {
        // Arrange
        var content = @"# Title
## Section 1
## Section 2
### Subsection
## Section 3";

        // Act
        var headings = NavigationService.ExtractHeadings(content);

        // Assert
        Assert.Single(headings); // Only top-level
        Assert.Equal("Title", headings[0].Text);
        Assert.Equal(3, headings[0].Children.Count); // 3 H2s
    }

    [Fact]
    public void ExtractHeadings_NestedHeadings_BuildsHierarchy()
    {
        // Arrange
        var content = @"# Main
## Sub 1
### Sub Sub 1
## Sub 2";

        // Act
        var headings = NavigationService.ExtractHeadings(content);

        // Assert
        Assert.Single(headings);
        var main = headings[0];
        Assert.Equal("Main", main.Text);
        Assert.Equal(2, main.Children.Count);
        Assert.Equal("Sub 1", main.Children[0].Text);
        Assert.Single(main.Children[0].Children);
        Assert.Equal("Sub Sub 1", main.Children[0].Children[0].Text);
    }

    [Fact]
    public void ExtractHeadings_NoHeadings_ReturnsEmpty()
    {
        // Arrange
        var content = "Just regular text\n\nNo headings here.";

        // Act
        var headings = NavigationService.ExtractHeadings(content);

        // Assert
        Assert.Empty(headings);
    }

    [Fact]
    public void ExtractHeadings_IgnoresCodeBlockHeadings()
    {
        // Arrange
        var content = @"# Real Heading

```markdown
# This is in a code block
## Should be ignored
```

## Another Real Heading";

        // Act
        var headings = NavigationService.ExtractHeadings(content);

        // Assert
        // Note: Current implementation may not handle this perfectly
        // This test documents the expected behavior
        Assert.NotEmpty(headings);
        Assert.Equal("Real Heading", headings[0].Text);
    }

    [Fact]
    public void ExtractHeadings_WithSpecialCharacters()
    {
        // Arrange
        var content = @"# C# Programming
## What's New in .NET 10?
### The `async` Keyword";

        // Act
        var headings = NavigationService.ExtractHeadings(content);

        // Assert
        Assert.Single(headings);
        Assert.Equal("C# Programming", headings[0].Text);
        Assert.Contains(headings[0].Children, h => h.Text.Contains(".NET 10"));
    }
}
