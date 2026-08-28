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
    // The exact expected set, not just a list of Assert.Contains checks: a fixture
    // added to the directory without being registered here should fail this test
    // just as loudly as a fixture that goes missing.
    private static readonly string[] ExpectedFeedFiles =
    [
        "atom-simple.xml",
        "not-a-feed.html",
        "rdf-rss1.xml",
        "rss2-bad-dates.xml",
        "rss2-bare-ampersand.xml",
        "rss2-cdata-entities.xml",
        "rss2-content-encoded.xml",
        "rss2-empty-channel.xml",
        "rss2-no-guid.xml",
        "rss2-no-identity.xml",
        "rss2-relative-links.xml",
        "rss2-simple.xml",
        "rss2-undeclared-entity.xml",
        "rss2-windows-1252.xml",
    ];

    [Fact]
    public void Every_fixture_is_copied_to_the_test_output()
    {
        var files = Fixtures.AllFeedFiles();

        Assert.Equal(ExpectedFeedFiles.OrderBy(name => name), files);
    }

    [Fact]
    public void Every_fixture_has_content()
    {
        // rss2-windows-1252.xml is deliberately not valid UTF-8: File.ReadAllText
        // decodes it as UTF-8 regardless, which replaces the undecodable bytes
        // with U+FFFD rather than throwing. The result still is not blank, so
        // this loop is still a meaningful "the file isn't empty on disk" check
        // for that fixture; it does not (and must not) assert anything about
        // the decoded text, since no parser exists yet to honour the feed's
        // declared windows-1252 encoding.
        foreach (var name in Fixtures.AllFeedFiles())
            Assert.False(string.IsNullOrWhiteSpace(Fixtures.Feed(name)), $"{name} is empty.");
    }
}
