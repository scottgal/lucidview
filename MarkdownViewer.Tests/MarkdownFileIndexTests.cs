using MarkdownViewer.Services;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace MarkdownViewer.Tests;

public sealed class MarkdownFileIndexTests
{
    [Fact]
    public async Task HeadingAnchorsMatchWholeDocumentMarkdigAcrossPageBoundaries()
    {
        var lines = new List<string> { "# Same title", "# Same title-1", "# Explicit {#chosen}", "" };
        lines.AddRange(Enumerable.Range(0, 160).SelectMany(i => new[] { $"Paragraph {i}.", "" }));
        lines.AddRange(["# Same title", "# Same title", "", "Setext **heading**", "======", "",
            "> ## Nested café heading", "", "# [A link](https://example.com) &amp; `code`", "",
            "# !!!", "# Same title", "```text", "# This is not a heading", "```"]);
        await WithFile(lines, async file =>
        {
            var index = await MarkdownFileIndex.CreateAsync(file);
            var document = Markdown.Parse(string.Join('\n', lines), new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
            foreach (var heading in document.Descendants<HeadingBlock>())
                Assert.True(index.FindHeading(heading.GetAttributes().Id!) == heading.Line,
                    $"Missing {heading.GetAttributes().Id} at {heading.Line}.");
            Assert.Null(index.FindHeading("this-is-not-a-heading"));
            Assert.Null(index.FindHeading("missing-heading"));
        });
    }

    [Fact]
    public async Task KeepsFencedCodeAndListsWithinCompletePages()
    {
        var lines = Enumerable.Range(0, 80).SelectMany(i => new[] { $"Paragraph {i}.", "" }).ToList();
        var fenceStart = lines.Count;
        lines.Add("```text");
        lines.AddRange(Enumerable.Range(0, 180).Select(i => $"code {i}"));
        lines.Add("```");
        var fenceEnd = lines.Count;
        lines.Add("");
        var listStart = lines.Count;
        lines.AddRange(Enumerable.Range(0, 150).Select(i => $"- item {i}"));
        var listEnd = lines.Count;
        lines.Add("");
        lines.Add("# After the list");
        await WithFile(lines, async file =>
        {
            var index = await MarkdownFileIndex.CreateAsync(file);
            AssertContinuous(index, file.LineCount);
            var fence = index.FindPage(fenceStart);
            Assert.False(fence.SourceOnly);
            Assert.True(fence.FirstLine + fence.LineCount >= fenceEnd);
            var list = index.FindPage(listStart);
            Assert.False(list.SourceOnly);
            Assert.True(list.FirstLine + list.LineCount >= listEnd, string.Join("; ", index.Pages));
        });
    }

    [Fact]
    public async Task OversizedFenceUsesBoundedSourcePagesAndRecoversAfterClosingFence()
    {
        var lines = new List<string> { "```text" };
        lines.AddRange(Enumerable.Range(0, 2000).Select(i => $"code {i}"));
        lines.Add("```");
        lines.Add("");
        var heading = lines.Count;
        lines.Add("# Normal Markdown again");
        lines.Add("A paragraph.");
        await WithFile(lines, async file =>
        {
            var index = await MarkdownFileIndex.CreateAsync(file);
            AssertContinuous(index, file.LineCount);
            Assert.All(index.Pages.Where(p => p.FirstLine < 2002), p => Assert.True(p.SourceOnly));
            Assert.All(index.Pages, p => Assert.InRange(p.LineCount, 1, 512));
            Assert.False(index.FindPage(heading).SourceOnly);
        });
    }

    [Fact]
    public async Task ResolvesReferenceLinksDefinedOnALaterPage()
    {
        var lines = new List<string> { "[Read more][later]", "" };
        lines.AddRange(Enumerable.Range(0, 150).SelectMany(i => new[] { $"Paragraph {i}.", "" }));
        lines.Add("[later]: https://example.com/article \"A title\"");
        await WithFile(lines, async file =>
        {
            var index = await MarkdownFileIndex.CreateAsync(file);
            Assert.Contains("[later]:", index.ReferenceContext);
            var page = index.FindPage(0);
            var text = string.Join('\n', await file.ReadLinesAsync(page.FirstLine, page.LineCount));
            var html = Markdig.Markdown.ToHtml(text + "\n\n" + index.ReferenceContext);
            Assert.Contains("href=\"https://example.com/article\"", html);
        });
    }

    private static void AssertContinuous(MarkdownFileIndex index, long lineCount)
    {
        long nextLine = 0;
        foreach (var page in index.Pages)
        {
            Assert.Equal(nextLine, page.FirstLine);
            nextLine += page.LineCount;
        }
        Assert.Equal(lineCount, nextLine);
    }

    private static async Task WithFile(IEnumerable<string> lines, Func<IndexedTextFile, Task> test)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"lucidview-blocks-{Guid.NewGuid():N}.md");
        try
        {
            await File.WriteAllTextAsync(filePath, string.Join('\n', lines));
            await test(await IndexedTextFile.OpenAsync(filePath));
        }
        finally { File.Delete(filePath); }
    }
}
