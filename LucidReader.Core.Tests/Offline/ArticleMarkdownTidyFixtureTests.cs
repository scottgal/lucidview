using LucidReader.Core.Offline;
using MarkdownViewer.Services;
using Xunit;
using Xunit.Abstractions;

namespace LucidReader.Core.Tests.Offline;

/// <summary>
/// The tidy over the real thing: three articles saved from three publishers,
/// run through the same HtmlToMarkdownService the offline downloader uses,
/// then tidied. Unit tests on hand-written markdown prove the rules; only
/// this proves the rules match what real pages actually produce.
///
/// The three sites were picked because their document titles disagree about
/// everything. mostlylucid appends a language in brackets, The Verge appends
/// its name behind a pipe, NASA behind a hyphen, and the extractor removes
/// some of those suffixes and not others depending on the page. An equality
/// check against the feed's item title fails on all three.
/// </summary>
public class ArticleMarkdownTidyFixtureTests
{
    private readonly ITestOutputHelper _output;

    public ArticleMarkdownTidyFixtureTests(ITestOutputHelper output) => _output = output;

    private static string Html(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", name));

    private static (string Before, string After) Convert(string file, string url, string itemTitle)
    {
        var markdown = new HtmlToMarkdownService().Convert(Html(file), new Uri(url));
        return (markdown, ArticleMarkdownTidy.Clean(markdown, itemTitle));
    }

    private void Report(string label, string before, string after)
    {
        _output.WriteLine($"===== {label}: BEFORE (first 10 lines) =====");
        foreach (var line in before.Split('\n').Take(10)) _output.WriteLine("| " + line);
        _output.WriteLine($"===== {label}: AFTER (first 10 lines) =====");
        foreach (var line in after.Split('\n').Take(10)) _output.WriteLine("| " + line);
    }

    [Fact]
    public void MostlylucidLosesTheTitleEchoTheDuplicateHeadingAndTheStraySlashes()
    {
        const string title =
            "Signal Shingle: a novel architecture for high-performance ASP.NET multi-widget sites";

        var (before, after) = Convert(
            "mostlylucid-post.html",
            "https://www.mostlylucid.net/blog/signal-shingle-architecture",
            title);

        Report("mostlylucid", before, after);

        var head = Head(after);

        Assert.DoesNotContain("(English)", head, StringComparison.Ordinal);
        Assert.DoesNotContain(title, head, StringComparison.Ordinal);
        Assert.DoesNotContain("\n//\n", "\n" + head + "\n", StringComparison.Ordinal);

        // What is left above the prose is the site's own date and read-time,
        // which are real content and were never in scope to remove.
        Assert.StartsWith("Friday, 24 July 2026", after.TrimStart(), StringComparison.Ordinal);

        // The real article survives, opening line and all.
        Assert.Contains("*Part of the Stylo.Bot release series.", after, StringComparison.Ordinal);
        Assert.Contains("## The problem: per-request fan-out", after, StringComparison.Ordinal);
        Assert.Contains("```mermaid", after, StringComparison.Ordinal);
    }

    [Fact]
    public void VergeLosesTheTitleEchoAndItsOwnH1()
    {
        const string title =
            "Enormous 12TB Steam leak includes abandoned Half-Life 2: Episode 3 assets";

        var (before, after) = Convert(
            "verge.html",
            "https://www.theverge.com/games/986552/12tb-steam-leak-half-life-2-episode-3",
            title);

        Report("verge", before, after);

        var head = Head(after);

        Assert.DoesNotContain(title, head, StringComparison.Ordinal);
        Assert.StartsWith("﻿The archive includes basically every title", after.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("Over [12 terabytes]", after, StringComparison.Ordinal);
    }

    [Fact]
    public void NasaLosesTheTitleEchoAndTheRepeatBelowTheDownloadsHeading()
    {
        const string title =
            "Ribbon-Cutting Event for NASA Deep Space Network’s Deep Space Station 23";

        var (before, after) = Convert(
            "nasa.html",
            "https://science.nasa.gov/photojournal/ribbon-cutting-event-for-nasa-deep-space-networks-deep-space-station-23/",
            title);

        Report("nasa", before, after);

        var head = Head(after);

        // Both echoes go: the bare one at the top and the H2 four blocks down.
        Assert.DoesNotContain(title, head, StringComparison.Ordinal);
        Assert.StartsWith("08/25/2026", after.TrimStart(), StringComparison.Ordinal);

        // The date and the site's own Downloads heading sat between the two
        // echoes. They are not the title, so they stay.
        Assert.Contains("Downloads", head, StringComparison.Ordinal);
        Assert.Contains("Leadership from NASA Headquarters", after, StringComparison.Ordinal);
    }

    /// <summary>The leading region the rules are allowed to touch.</summary>
    private static string Head(string markdown) =>
        string.Join("\n", markdown.Split('\n').Take(10));
}
