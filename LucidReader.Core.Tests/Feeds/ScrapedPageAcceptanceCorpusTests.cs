using AngleSharp;
using AngleSharp.Dom;
using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// What mylo does with each of the twenty-five saved pages once the fallback
/// exists, which is a different question from the two the other corpus tests
/// ask.
///
/// <see cref="ArticleListCorpusTests"/> measures the detector on its own and is
/// deliberately untouched by this work: the detector is unchanged, lwn.net is
/// still a miss for it, and its recorded miss stays recorded. The floor there
/// stays 24 because 24 is still what the detector scores.
/// <see cref="StyloExtractIndexCorpusTests"/> measures the library on its own.
/// This one measures the pair as mylo actually uses them: detector first,
/// fallback only on what the detector declined, and the answer to "would mylo
/// offer this page as a feed".
///
/// <para>The column that matters is the last one. Seven of these pages are not
/// indexes, and a fallback that accepts any of them is a regression however
/// many indexes it adds, because the user is being offered a subscription to a
/// page that is not a list. The library on its own reads the reference list at
/// the foot of the Wikipedia article as an index at confidence 0.84; the gate
/// on <see cref="IndexFallbackReader"/> is what declines it here.</para>
/// </summary>
public class ScrapedPageAcceptanceCorpusTests
{
    /// <summary>Which path read a page, or that neither did.</summary>
    public enum Path
    {
        /// <summary>Neither the detector nor the fallback would offer it.</summary>
        Declined,

        /// <summary>The detector read it, and the fallback was never asked.</summary>
        Detector,

        /// <summary>The detector declined it and the fallback read it.</summary>
        Fallback
    }

    /// <summary>
    /// The path each page is expected to take. Twenty-four by the detector, one
    /// by the fallback, seven declined by both. Written out per page rather
    /// than derived, so a page that changes sides names itself.
    /// </summary>
    public static readonly Dictionary<string, Path> Expected = new()
    {
        ["hn"] = Path.Detector,
        ["lobsters"] = Path.Detector,
        ["slashdot"] = Path.Detector,
        ["mostlylucid-blog"] = Path.Detector,
        ["jvns"] = Path.Detector,
        ["simonwillison"] = Path.Detector,
        ["rustblog"] = Path.Detector,
        ["dotnetblog"] = Path.Detector,
        ["daringfireball"] = Path.Detector,
        ["danluu"] = Path.Detector,
        ["overreacted"] = Path.Detector,
        ["martinfowler"] = Path.Detector,
        ["githubblog"] = Path.Detector,
        ["theregister"] = Path.Detector,
        ["arstechnica"] = Path.Detector,
        ["theverge-home"] = Path.Detector,
        ["bbc-news"] = Path.Detector,

        // The page this whole path exists for. Its headlines are h2 elements
        // holding no link and the article address lives in the blurb beneath.
        ["lwn"] = Path.Fallback,

        // The seven that must stay declined by both.
        ["example"] = Path.Declined,
        ["hn-item"] = Path.Declined,
        ["wikipedia-article"] = Path.Declined,
        ["simonwillison-article"] = Path.Declined,
        ["danluu-article"] = Path.Declined,
        ["github-repo"] = Path.Declined,
        ["dotnet-docs"] = Path.Declined
    };

    private static string CorpusDirectory =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", "corpus");

    private static IDocument Parse(string html, Uri uri) =>
        BrowsingContext.New(Configuration.Default)
            .OpenAsync(request => request.Content(html).Address(uri.ToString()))
            .GetAwaiter().GetResult();

    /// <summary>
    /// mylo's ordering, in three lines: detector, then the fallback on what it
    /// declined, then nothing. Anything that reads a page in mylo goes through
    /// this order, so the test running it is running the real rule.
    /// </summary>
    public static (Path Path, ArticleListDetection? Reading) Read(string name)
    {
        var page = ArticleListCorpusTests.Corpus.Single(p => p.Name == name);
        var uri = new Uri(page.Url);
        var html = File.ReadAllText(System.IO.Path.Combine(CorpusDirectory, name + ".html"));

        var detection = ArticleListDetector.Detect(html, uri);
        if (detection.IsArticleList) return (Path.Detector, detection);

        var fallback = IndexFallbackReader.TryRead(Parse(html, uri), uri, detection);
        return fallback is null ? (Path.Declined, null) : (Path.Fallback, fallback);
    }

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var page in ArticleListCorpusTests.Corpus) data.Add(page.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Each_page_is_read_by_the_path_recorded_for_it(string name)
    {
        var (path, reading) = Read(name);

        Assert.True(Expected[name] == path,
            $"{name} was read by {path} but is recorded as {Expected[name]}. " +
            (reading is null ? "Nothing read it." : reading.Reason));
    }

    /// <summary>
    /// The whole corpus, classified the way a person would, by the two paths
    /// together. This is the number the fallback was added to move: the
    /// detector alone scores 24 of 25 and is unchanged, and the pair score 25.
    /// </summary>
    [Fact]
    public void The_two_paths_together_classify_the_whole_corpus()
    {
        var wrong = new List<string>();

        foreach (var page in ArticleListCorpusTests.Corpus)
        {
            var (path, _) = Read(page.Name);
            var accepted = path != Path.Declined;
            if (accepted != page.IsList)
                wrong.Add($"{page.Name} (wanted {(page.IsList ? "LIST" : "NOT")}, got {path})");
        }

        Assert.True(wrong.Count == 0,
            $"{wrong.Count} of {ArticleListCorpusTests.Corpus.Length} pages were read wrong: " +
            string.Join("; ", wrong));
    }

    /// <summary>
    /// The seven negatives, asserted on their own and by name. A false positive
    /// here is the failure that matters: mylo would offer to subscribe the user
    /// to a page that is not an index.
    /// </summary>
    [Fact]
    public void No_page_that_is_not_an_index_is_read_by_either_path()
    {
        var accepted = ArticleListCorpusTests.Corpus
            .Where(p => !p.IsList)
            .Select(p => (p.Name, Result: Read(p.Name)))
            .Where(x => x.Result.Path != Path.Declined)
            .Select(x => $"{x.Name} by {x.Result.Path}: {x.Result.Item2!.Reason}")
            .ToList();

        Assert.True(accepted.Count == 0,
            "Pages that are not indexes were offered as feeds: " + string.Join("; ", accepted));
    }

    /// <summary>
    /// Nothing the detector accepts reaches the fallback, which is the property
    /// that makes the fallback safe to add at all: the twenty-four pages that
    /// already worked cannot be changed by it. Asserted directly rather than
    /// inferred from the table, by handing the fallback a page the detector
    /// accepted and watching it refuse to answer.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void The_fallback_refuses_to_answer_about_a_page_the_detector_accepted(string name)
    {
        var page = ArticleListCorpusTests.Corpus.Single(p => p.Name == name);
        var uri = new Uri(page.Url);
        var html = File.ReadAllText(System.IO.Path.Combine(CorpusDirectory, name + ".html"));

        var detection = ArticleListDetector.Detect(html, uri);
        if (!detection.IsArticleList) return;

        Assert.Null(IndexFallbackReader.TryRead(Parse(html, uri), uri, detection));
    }

    /// <summary>
    /// The page the fallback was added for, checked on its links rather than on
    /// its count. Ten headlines, every one of them an lwn.net/Articles/ address,
    /// which is what StyloExtractIndexCorpusTests records as correct for this
    /// page.
    /// </summary>
    [Fact]
    public void The_fallback_reads_lwns_articles_and_not_the_sites_it_quotes()
    {
        var (path, reading) = Read("lwn");

        Assert.Equal(Path.Fallback, path);
        Assert.NotNull(reading);
        Assert.True(reading.Articles.Count >= ArticleListDetector.MinimumArticles,
            $"Only {reading.Articles.Count} articles.");

        Assert.All(reading.Articles, article =>
        {
            var uri = new Uri(article.Link);
            Assert.Equal("lwn.net", uri.Host);
            Assert.StartsWith("/Articles/", uri.AbsolutePath, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(article.Title));
            Assert.False(string.IsNullOrWhiteSpace(article.CanonicalId));
        });
    }

    /// <summary>
    /// The gate, named. wikipedia-article is the one negative the library reads
    /// as an index on its own, and it is the same-host share that declines it
    /// here, not the confidence: the library scores it 0.84 against lwn's 0.85.
    /// If somebody weakens <see cref="IndexFallbackReader.MinimumSameHostShare"/>
    /// this is the test that says what it costs.
    /// </summary>
    [Fact]
    public void The_wikipedia_citation_list_is_declined_on_whose_host_it_points_at()
    {
        var page = ArticleListCorpusTests.Corpus.Single(p => p.Name == "wikipedia-article");
        var uri = new Uri(page.Url);
        var html = File.ReadAllText(System.IO.Path.Combine(CorpusDirectory, "wikipedia-article.html"));
        var document = Parse(html, uri);

        var library = new StyloExtract.Heuristics.IndexPageExtractor().Detect(document, uri);
        Assert.True(library.IsIndex,
            "This test is only meaningful while the library still reads this page as " +
            "an index. It no longer does, so the fallback's gate is not what is " +
            "declining it and this test should be rewritten or deleted.");

        var onHost = library.Items.Count(i =>
            Uri.TryCreate(uri, i.Link, out var link)
            && string.Equals(link.Host, uri.Host, StringComparison.OrdinalIgnoreCase));

        Assert.True(onHost / (double)library.Items.Count < IndexFallbackReader.MinimumSameHostShare,
            $"{onHost} of {library.Items.Count} citations are on {uri.Host}.");

        Assert.Null(IndexFallbackReader.TryRead(
            document, uri, ArticleListDetector.Detect(html, uri)));
    }

    /// <summary>
    /// Every page in the corpus has a recorded path. A page saved but not
    /// listed here is a page whose acceptance nothing measures.
    /// </summary>
    [Fact]
    public void Every_page_in_the_corpus_has_a_recorded_path()
    {
        Assert.Equal(
            ArticleListCorpusTests.Corpus.Select(p => p.Name).Order(),
            Expected.Keys.Order());
    }
}
