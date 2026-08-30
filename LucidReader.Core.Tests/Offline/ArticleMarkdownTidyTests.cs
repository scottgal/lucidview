using LucidReader.Core.Offline;
using Xunit;

namespace LucidReader.Core.Tests.Offline;

public class ArticleMarkdownTidyTests
{
    private const string Title = "Signal Shingle: a novel architecture for ASP.NET sites";

    [Fact]
    public void DropsALeadingPlainLineThatIsTheDocumentTitle()
    {
        var input = $"{Title} (English)\n\nThe article starts here.";

        var result = ArticleMarkdownTidy.Clean(input, Title);

        Assert.Equal("The article starts here.", result);
    }

    [Fact]
    public void DropsALeadingHeadingThatRepeatsTheTitle()
    {
        var input = $"## {Title}\n\nThe article starts here.";

        Assert.Equal("The article starts here.", ArticleMarkdownTidy.Clean(input, Title));
    }

    [Theory]
    // The three real site suffixes, each behind a different separator.
    [InlineData("(English)")]
    [InlineData("| The Verge")]
    [InlineData("- NASA Science")]
    [InlineData(": Ars Technica")]
    [InlineData("\u00b7 mostlylucid")]
    public void FuzzyMatchSeesThroughASiteSuffix(string suffix)
    {
        var input = $"# {Title} {suffix}\n\nBody.";

        Assert.Equal("Body.", ArticleMarkdownTidy.Clean(input, Title));
    }

    [Fact]
    public void FuzzyMatchWorksWhenTheSuffixIsOnTheItemTitleInstead()
    {
        var input = $"# {Title}\n\nBody.";

        Assert.Equal("Body.", ArticleMarkdownTidy.Clean(input, $"{Title} | Mostly Lucid"));
    }

    [Fact]
    public void IgnoresCaseWhitespaceAndPunctuation()
    {
        var input = "#   ENORMOUS 12TB STEAM LEAK -- HALF LIFE 2 EPISODE 3 ASSETS\n\nBody.";

        var result = ArticleMarkdownTidy.Clean(
            input, "Enormous 12TB Steam leak: Half-Life 2: Episode 3 assets");

        Assert.Equal("Body.", result);
    }

    [Fact]
    public void DropsAnOrphanedSeparatorLine()
    {
        var input = "Friday, 24 July 2026\n\n//\n\n20 minute read\n\nBody.";

        var result = ArticleMarkdownTidy.Clean(input, Title);

        Assert.Equal("Friday, 24 July 2026\n\n20 minute read\n\nBody.", result);
    }

    [Theory]
    [InlineData("//")]
    [InlineData("|")]
    [InlineData("-")]
    [InlineData("\u00b7")]
    [InlineData("\u2022")]
    [InlineData("\u2013")]
    [InlineData("\u2014")]
    public void RecognisesTheSeparatorSpellings(string separator)
    {
        var input = $"A date\n\n{separator}\n\nBody.";

        Assert.Equal("A date\n\nBody.", ArticleMarkdownTidy.Clean(input, Title));
    }

    [Fact]
    public void KeepsAHorizontalRule()
    {
        var input = "A date\n\n---\n\nBody.";

        Assert.Equal(input, ArticleMarkdownTidy.Clean(input, Title));
    }

    [Fact]
    public void KeepsALeadingHeadingThatIsNotTheTitle()
    {
        var input = "# Something else entirely\n\nBody.";

        Assert.Equal(input, ArticleMarkdownTidy.Clean(input, Title));
    }

    [Fact]
    public void KeepsAHeadingThatMerelyStartsWithTheTitle()
    {
        // The tail is a continuation of the sentence, not a site name.
        var input = $"# {Title} and how it came to be built the way it is\n\nBody.";

        Assert.Equal(input, ArticleMarkdownTidy.Clean(input, Title));
    }

    [Fact]
    public void KeepsAShortHeadingThatOnlySharesAPrefixWithAShortTitle()
    {
        var input = "# Introduction to sockets\n\nBody.";

        Assert.Equal(input, ArticleMarkdownTidy.Clean(input, "Introduction"));
    }

    [Fact]
    public void DoesNotReachAMidArticleRepeatOfTheTitle()
    {
        var body = string.Join("\n\n", Enumerable.Range(1, 8).Select(n => $"Paragraph {n}."));
        var input = $"{body}\n\n## {Title}\n\nMore prose.";

        Assert.Equal(input, ArticleMarkdownTidy.Clean(input, Title));
    }

    [Fact]
    public void DoesNotStripInsideAFencedCodeBlock()
    {
        var input = $"```\n//\n# {Title}\n```\n\nBody.";

        Assert.Equal(input, ArticleMarkdownTidy.Clean(input, Title));
    }

    [Fact]
    public void StopsAtAFenceThatOpensInTheLeadingRegion()
    {
        var input = $"# {Title}\n\n```csharp\n//\nvar x = 1;\n```\n\nBody.";

        Assert.Equal("```csharp\n//\nvar x = 1;\n```\n\nBody.", ArticleMarkdownTidy.Clean(input, Title));
    }

    [Fact]
    public void KeepsTheTitleWhenItIsTheOnlyContent()
    {
        var input = $"# {Title}";

        Assert.Equal(input, ArticleMarkdownTidy.Clean(input, Title));
    }

    [Fact]
    public void KeepsASeparatorWhenItIsTheOnlyContent()
    {
        Assert.Equal("//", ArticleMarkdownTidy.Clean("//", Title));
    }

    [Fact]
    public void LeavesAMultiLineBlockAlone()
    {
        // A title echo is one line. A wrapped paragraph that happens to open
        // with the title is prose.
        var input = $"{Title}\nand then it keeps going onto a second line.\n\nBody.";

        Assert.Equal(input, ArticleMarkdownTidy.Clean(input, Title));
    }

    [Fact]
    public void LeavesListItemsAndQuotesAlone()
    {
        var input = $"- {Title}\n\n> {Title}\n\nBody.";

        Assert.Equal(input, ArticleMarkdownTidy.Clean(input, Title));
    }

    [Fact]
    public void DoesNothingWithoutATitle()
    {
        var input = $"{Title}\n\nBody.";

        Assert.Equal(input, ArticleMarkdownTidy.Clean(input, null));
        Assert.Equal(input, ArticleMarkdownTidy.Clean(input, "   "));
    }

    [Fact]
    public void HandlesEmptyInput()
    {
        Assert.Equal(string.Empty, ArticleMarkdownTidy.Clean(null, Title));
        Assert.Equal("   ", ArticleMarkdownTidy.Clean("   ", Title));
    }

    [Fact]
    public void IsIdempotent()
    {
        var input = $"{Title} (English)\n\n## {Title}\n\n//\n\nBody.";

        var once = ArticleMarkdownTidy.Clean(input, Title);

        Assert.Equal(once, ArticleMarkdownTidy.Clean(once, Title));
    }

    [Fact]
    public void ReturnsTheInputUnchangedWhenThereIsNothingToRemove()
    {
        var input = "# Some other heading\n\nBody.\n\n## And a section\n\nMore.";

        Assert.Same(input, ArticleMarkdownTidy.Clean(input, Title));
    }
}
