using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class SnippetTests
{
    [Fact]
    public void Markdown_formatting_is_stripped_to_plain_text()
    {
        var text = Snippet.FromMarkdown("# Heading\n\nSome **bold** and _italic_ text.", null);

        Assert.DoesNotContain("#", text);
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("_", text);
        Assert.Contains("Some bold and italic text.", text);
    }

    [Fact]
    public void A_link_keeps_its_label_and_drops_its_target()
    {
        var text = Snippet.FromMarkdown("Read [the article](https://example.com/x) now.", null);

        Assert.Contains("the article", text);
        Assert.DoesNotContain("https://example.com/x", text);
    }

    [Fact]
    public void An_image_contributes_nothing()
    {
        var text = Snippet.FromMarkdown("![a picture](https://example.com/p.png)Actual text.", null);

        Assert.StartsWith("Actual text.", text);
    }

    [Fact]
    public void A_code_fence_does_not_leak_backticks()
    {
        var text = Snippet.FromMarkdown("Intro.\n\n```csharp\nvar x = 1;\n```\n\nOutro.", null);

        Assert.DoesNotContain("```", text);
    }

    [Fact]
    public void Html_left_in_the_summary_is_stripped_too()
    {
        var text = Snippet.FromMarkdown(null, "<p>Hello <b>there</b></p>");

        Assert.Equal("Hello there", text);
    }

    [Fact]
    public void Whitespace_and_newlines_collapse_to_single_spaces()
    {
        var text = Snippet.FromMarkdown("One\n\n\nTwo    Three", null);

        Assert.Equal("One Two Three", text);
    }

    [Fact]
    public void Markdown_is_preferred_over_the_summary_when_both_exist()
    {
        var text = Snippet.FromMarkdown("From the article body.", "From the summary.");

        Assert.Equal("From the article body.", text);
    }

    [Fact]
    public void The_summary_is_used_when_there_is_no_article_body()
    {
        var text = Snippet.FromMarkdown(null, "From the summary.");

        Assert.Equal("From the summary.", text);
    }

    [Fact]
    public void Long_text_is_truncated_on_a_word_boundary_with_an_ellipsis()
    {
        var body = string.Join(" ", Enumerable.Repeat("word", 200));

        var text = Snippet.FromMarkdown(body, null, maxLength: 40);

        Assert.True(text.Length <= 41);
        Assert.EndsWith("...", text);
        Assert.DoesNotContain("wor...", text);
    }

    [Fact]
    public void Nothing_at_all_yields_an_empty_string_rather_than_null()
    {
        Assert.Equal(string.Empty, Snippet.FromMarkdown(null, null));
    }
}
