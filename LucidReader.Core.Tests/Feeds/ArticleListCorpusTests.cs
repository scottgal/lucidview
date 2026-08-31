using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// The detector measured against twenty-five pages that really exist, each
/// fetched once and saved whole under Fixtures/Html/corpus.
///
/// This is the test that stops the heuristics drifting. Every page is asserted
/// on its own, so a change that breaks one names the page rather than moving a
/// total, and the total is asserted too, so a change that trades one page for
/// another cannot hide inside a per-page pass. The fixtures are committed and
/// nothing here reads the network.
///
/// One of the twenty-five is recorded as a known miss rather than deleted or
/// quietly re-labelled. A corpus trimmed until the number looks good measures
/// nothing, and a page whose expectation was loosened to match the code is a
/// page that has stopped being evidence.
/// The miss carries the reason it is one and is asserted to still fail, so
/// fixing it fails this test and makes somebody come and delete the excuse.
/// </summary>
public class ArticleListCorpusTests
{
    /// <summary>
    /// One saved page: what it is, where it came from, and what a person
    /// reading it would say it is.
    /// </summary>
    /// <param name="Name">Fixture file name without the extension.</param>
    /// <param name="Url">The address it was fetched from, which the detector
    /// needs to resolve relative links and to recognise its own page.</param>
    /// <param name="IsList">True when the page is an index of articles.</param>
    /// <param name="KnownMiss">Set when the detector gets this page wrong and
    /// the miss is accepted, saying why. Null when it gets it right.</param>
    public sealed record Page(string Name, string Url, bool IsList, string? KnownMiss = null);

    private const bool List = true;
    private const bool NotAList = false;

    /// <summary>
    /// The corpus, and what a human says about each. Eighteen index pages and
    /// seven pages that are not indexes, chosen so that the two sides overlap
    /// structurally: the negatives include an article with a list of further
    /// reading in its footer, a documentation hub whose cards of links read
    /// like article titles, and a code repository whose repeated links are
    /// filenames, all of which the detector scored as lists before this corpus
    /// existed.
    /// </summary>
    public static readonly Page[] Corpus =
    [
        // Aggregators. No dates in the markup a reader would call a date, and
        // almost every link off-host.
        new("hn", "https://news.ycombinator.com", List),
        new("lobsters", "https://lobste.rs", List),
        new("slashdot", "https://slashdot.org/", List),

        // Weblogs, template-generated.
        new("mostlylucid-blog", "https://www.mostlylucid.net/blog", List),
        new("jvns", "https://jvns.ca/", List),
        new("simonwillison", "https://simonwillison.net/", List),
        new("rustblog", "https://blog.rust-lang.org/", List),
        new("dotnetblog", "https://devblogs.microsoft.com/dotnet/", List),
        new("daringfireball", "https://daringfireball.net", List),

        // Weblogs, hand-rolled or unusual. danluu.com is a bare list of anchors
        // with unquoted hrefs; overreacted.io makes the repeated element itself
        // the link; martinfowler.com titles its entries in two words.
        new("danluu", "https://danluu.com/", List),
        new("overreacted", "https://overreacted.io/", List),
        new("martinfowler", "https://martinfowler.com/bliki/", List),

        // A front page that declares itself a single schema.org article.
        new("githubblog", "https://github.blog/", List),

        // News front pages.
        new("theregister", "https://www.theregister.com/", List),
        new("arstechnica", "https://arstechnica.com/", List),
        new("theverge-home", "https://www.theverge.com/", List),
        new("bbc-news", "https://www.bbc.co.uk/news", List),

        new("lwn", "https://lwn.net/", List,
            KnownMiss:
            "LWN's front page titles each item in an h2 that holds no link at " +
            "all, and the article address is only reachable from the blurb " +
            "underneath it - sometimes as 'Full Story', sometimes only as the " +
            "'Comments' link, and sometimes not at all, with the nearest " +
            "anchors pointing at kernel.org and other sites quoted in the " +
            "blurb. There is no rule for borrowing a link from a following " +
            "sibling that picks the right anchor on this page, and the ones " +
            "that come close would attach an arbitrary quoted link to a " +
            "headline, which is worse than declining."),

        // Not lists.
        new("example", "https://example.com/", NotAList),
        new("hn-item", "https://news.ycombinator.com/item?id=1", NotAList),
        new("wikipedia-article", "https://en.wikipedia.org/wiki/RSS", NotAList),
        new("simonwillison-article", "https://simonwillison.net/2024/Dec/31/llms-in-2024/", NotAList),
        new("danluu-article", "https://danluu.com/why-hardware-development-is-hard/", NotAList),
        new("github-repo", "https://github.com/scottgal/lucidview", NotAList),
        new("dotnet-docs", "https://learn.microsoft.com/en-us/dotnet/csharp/", NotAList)
    ];

    /// <summary>
    /// How many of the twenty-five the detector has to get right. This is what
    /// it actually scores, not a target: raising the floor is the last step of
    /// any change that improves it, and a change that drops a page fails here
    /// even if it fixed another one.
    ///
    /// It was 17 before the structural work described on
    /// <see cref="ArticleListDetector"/> and is 24 after.
    /// </summary>
    public const int AccuracyFloor = 24;

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var page in Corpus) data.Add(page.Name);
        return data;
    }

    private static string CorpusDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", "corpus");

    private static ArticleListDetection Detect(Page page) =>
        ArticleListDetector.Detect(
            File.ReadAllText(Path.Combine(CorpusDirectory, page.Name + ".html")),
            new Uri(page.Url));

    private static Page Find(string name) => Corpus.Single(p => p.Name == name);

    [Theory]
    [MemberData(nameof(Names))]
    public void Each_page_is_classified_as_a_person_would(string name)
    {
        var page = Find(name);
        var detection = Detect(page);

        if (page.KnownMiss is not null)
        {
            Assert.True(
                detection.IsArticleList != page.IsList,
                $"{page.Name} is recorded as a known miss but now passes. " +
                "Delete the KnownMiss reason, raise AccuracyFloor and say so in " +
                $"the commit. The recorded reason was: {page.KnownMiss}");
            return;
        }

        Assert.True(
            detection.IsArticleList == page.IsList,
            $"{page.Name} ({page.Url}) should be " +
            $"{(page.IsList ? "an article list" : "not an article list")} but the " +
            $"detector said {(detection.IsArticleList ? "it is" : "it is not")}, " +
            $"with confidence {detection.Confidence:0.00} over " +
            $"{detection.Articles.Count} links. It said: {detection.Reason}");
    }

    /// <summary>
    /// A page the detector accepts has to have found the articles, not a run of
    /// something else that happened to score. A count on its own does not tell
    /// those apart, so this asserts that most of what came back carries a title
    /// long enough to be one and an address on a page rather than a fragment.
    /// Ten characters is the bar because martinfowler.com genuinely titles its
    /// entries "Vibe Coding".
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void An_accepted_page_yields_usable_articles(string name)
    {
        var page = Find(name);
        if (!page.IsList || page.KnownMiss is not null) return;

        var detection = Detect(page);

        Assert.True(detection.Articles.Count >= ArticleListDetector.MinimumArticles,
            $"{page.Name} returned {detection.Articles.Count} articles.");

        Assert.All(detection.Articles, article =>
        {
            Assert.False(string.IsNullOrWhiteSpace(article.Title));
            Assert.StartsWith("http", article.Link, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(article.CanonicalId));
        });

        var substantial = detection.Articles.Count(a => a.Title.Length >= 10);
        Assert.True(substantial / (double)detection.Articles.Count >= 0.6,
            $"{page.Name}: only {substantial} of {detection.Articles.Count} titles " +
            "look like article titles.");
    }

    [Fact]
    public void The_corpus_is_classified_at_or_above_the_recorded_accuracy()
    {
        var wrong = new List<string>();
        var correct = 0;

        foreach (var page in Corpus)
        {
            var detection = Detect(page);
            if (detection.IsArticleList == page.IsList) correct++;
            else wrong.Add($"{page.Name} (wanted {(page.IsList ? "LIST" : "NOT")}, " +
                           $"confidence {detection.Confidence:0.00}, " +
                           $"{detection.Articles.Count} links)");
        }

        Assert.True(correct >= AccuracyFloor,
            $"Corpus accuracy fell to {correct}/{Corpus.Length}, under the recorded " +
            $"{AccuracyFloor}/{Corpus.Length}. Wrong: {string.Join("; ", wrong)}");
    }

    /// <summary>
    /// Every known miss is still a miss and every other page is still right, so
    /// the floor above cannot be met by a different set of pages than the one
    /// it was recorded against.
    /// </summary>
    [Fact]
    public void The_recorded_misses_are_the_only_misses()
    {
        var expected = Corpus.Where(p => p.KnownMiss is not null).Select(p => p.Name).Order();
        var actual = Corpus
            .Where(p => Detect(p).IsArticleList != p.IsList)
            .Select(p => p.Name)
            .Order();

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// The fixture directory and the table above have to agree. A page saved
    /// but never registered is a page nothing measures, and a page registered
    /// but never saved is a test that cannot run offline.
    /// </summary>
    [Fact]
    public void Every_saved_page_is_registered_and_every_registered_page_is_saved()
    {
        var saved = Directory.GetFiles(CorpusDirectory, "*.html")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Order();

        Assert.Equal(Corpus.Select(p => p.Name).Order(), saved);
    }

    [Fact]
    public void Every_saved_page_has_content()
    {
        foreach (var page in Corpus)
        {
            var path = Path.Combine(CorpusDirectory, page.Name + ".html");
            Assert.True(new FileInfo(path).Length > 400, $"{page.Name} is empty.");
        }
    }
}
