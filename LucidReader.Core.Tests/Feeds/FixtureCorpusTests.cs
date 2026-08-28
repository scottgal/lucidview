using Xunit;

namespace LucidReader.Core.Tests.Feeds;

public static class Fixtures
{
    public static string Feed(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Feeds", name));

    public static IReadOnlyList<string> AllFeedFiles() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Feeds"))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name)
            .ToList();
}

public class FixtureCorpusTests
{
    [Fact]
    public void Every_fixture_is_copied_to_the_test_output()
    {
        var files = Fixtures.AllFeedFiles();

        Assert.Contains("rss2-simple.xml", files);
        Assert.Contains("rss2-content-encoded.xml", files);
        Assert.Contains("atom-simple.xml", files);
        Assert.Contains("rdf-rss1.xml", files);
        Assert.Contains("rss2-bad-dates.xml", files);
        Assert.Contains("rss2-no-guid.xml", files);
        Assert.Contains("rss2-relative-links.xml", files);
        Assert.Contains("rss2-undeclared-entity.xml", files);
        Assert.Contains("not-a-feed.html", files);
    }

    [Fact]
    public void Every_fixture_has_content()
    {
        foreach (var name in Fixtures.AllFeedFiles())
            Assert.False(string.IsNullOrWhiteSpace(Fixtures.Feed(name)), $"{name} is empty.");
    }
}
