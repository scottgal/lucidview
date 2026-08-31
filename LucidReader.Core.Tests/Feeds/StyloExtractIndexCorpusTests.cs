using AngleSharp;
using AngleSharp.Dom;
using StyloExtract.Abstractions;
using StyloExtract.Heuristics;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// StyloExtract's index-page path, measured against the same twenty-five saved
/// pages <see cref="ArticleListCorpusTests"/> measures mylo's own detector
/// against.
///
/// It lives here rather than in the StyloExtract repository because the corpus
/// does: twenty-five whole pages, six megabytes, fetched once and committed,
/// and a second copy of them would drift from this one. mylo depends on the
/// library, so this is the side of the boundary that can see both.
///
/// What it measures is not mylo's behaviour. Nothing in the app calls
/// <see cref="IndexPageExtractor"/>: the detector decides what a scraped page
/// lists and templates are learned from its answer. This is here so a change
/// to the library's structural reasoning cannot land unmeasured, and so the
/// two implementations can be compared on the same evidence.
///
/// The library scored 10 of 25 before this path existed, when the only way to
/// ask it was to count RepeatedItem blocks: it emitted them on five pages, got
/// the links right on one, and fired on a documentation hub that is not a list
/// at all.
/// </summary>
public class StyloExtractIndexCorpusTests
{
    /// <summary>
    /// What the library scores. Raising this is the last step of any change
    /// that improves it, exactly as on <see cref="ArticleListCorpusTests"/>.
    /// </summary>
    public const int AccuracyFloor = 24;

    /// <summary>
    /// The one page the library gets wrong, and why it is recorded rather than
    /// deleted or re-labelled.
    /// </summary>
    private static readonly Dictionary<string, string> KnownMisses = new()
    {
        ["wikipedia-article"] =
            "The article's reference list is forty citations in one repeated " +
            "element, each distinctly titled, each a real link to a real page " +
            "on a different host. Structurally it is an index and it scores as " +
            "one. The rules that would reject it - demanding the entry's link " +
            "be most of the entry's text, or that the run carry the page's own " +
            "host - would also reject daringfireball.net's front page and every " +
            "aggregator in the corpus."
    };

    private static string CorpusDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", "corpus");

    private static IDocument Parse(string name, Uri uri)
    {
        var html = File.ReadAllText(Path.Combine(CorpusDirectory, name + ".html"));
        return BrowsingContext.New(Configuration.Default)
            .OpenAsync(request => request.Content(html).Address(uri.ToString()))
            .GetAwaiter().GetResult();
    }

    private static IndexPageResult Detect(ArticleListCorpusTests.Page page) =>
        new IndexPageExtractor().Detect(Parse(page.Name, new Uri(page.Url)), new Uri(page.Url));

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var page in ArticleListCorpusTests.Corpus) data.Add(page.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Each_page_is_classified_as_a_person_would(string name)
    {
        var page = ArticleListCorpusTests.Corpus.Single(p => p.Name == name);
        var result = Detect(page);

        if (KnownMisses.TryGetValue(name, out var reason))
        {
            Assert.True(
                result.IsIndex != page.IsList,
                $"{name} is recorded as a known miss for the library but now passes. " +
                $"Delete the entry, raise AccuracyFloor and say so in the commit. " +
                $"The recorded reason was: {reason}");
            return;
        }

        Assert.True(
            result.IsIndex == page.IsList,
            $"{name} ({page.Url}) should be " +
            $"{(page.IsList ? "an index" : "not an index")} but the library said " +
            $"{(result.IsIndex ? "it is" : "it is not")}, with confidence " +
            $"{result.Confidence:0.00} over {result.Items.Count} items. It said: {result.Reason}");
    }

    [Fact]
    public void The_corpus_is_classified_at_or_above_the_recorded_accuracy()
    {
        var wrong = new List<string>();
        var correct = 0;

        foreach (var page in ArticleListCorpusTests.Corpus)
        {
            var result = Detect(page);
            if (result.IsIndex == page.IsList) correct++;
            else wrong.Add($"{page.Name} (wanted {(page.IsList ? "INDEX" : "NOT")}, " +
                           $"confidence {result.Confidence:0.00}, {result.Items.Count} items)");
        }

        Assert.True(correct >= AccuracyFloor,
            $"Library accuracy fell to {correct}/{ArticleListCorpusTests.Corpus.Length}, under the " +
            $"recorded {AccuracyFloor}. Wrong: {string.Join("; ", wrong)}");
    }

    [Fact]
    public void The_recorded_misses_are_the_only_misses()
    {
        var expected = KnownMisses.Keys.Order();
        var actual = ArticleListCorpusTests.Corpus
            .Where(p => Detect(p).IsIndex != p.IsList)
            .Select(p => p.Name)
            .Order();

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// A page the library accepts has to hand back items whose titles and
    /// addresses are usable, not a run of something else that scored. This is
    /// the assertion that would have caught the old path returning
    /// martinfowler.com's citation links instead of its seven entries.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void An_accepted_page_yields_usable_items(string name)
    {
        var page = ArticleListCorpusTests.Corpus.Single(p => p.Name == name);
        if (!page.IsList || KnownMisses.ContainsKey(name)) return;

        var result = Detect(page);
        Assert.True(result.Items.Count >= IndexPageExtractor.MinimumItems);

        Assert.All(result.Items, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Link));
        });

        var substantial = result.Items.Count(i => i.Title.Length >= 10);
        Assert.True(substantial / (double)result.Items.Count >= 0.6,
            $"{name}: only {substantial} of {result.Items.Count} titles look like titles.");
    }

    /// <summary>
    /// And what it finds can be turned into a template that reads the same page
    /// again. Not every accepted page: three of them are runs whose members
    /// each hold several entries rather than one, and declining to induce from
    /// those is the right answer, since a template built on them would pick an
    /// arbitrary link out of each. What is asserted is that most of the corpus
    /// round-trips, and that where a template is produced it finds a comparable
    /// number of items rather than a handful.
    /// </summary>
    [Fact]
    public void Most_accepted_pages_yield_a_template_that_reads_them_again()
    {
        var inducer = new RecordTemplateInducer();
        var applicator = new RecordApplicator();

        var accepted = 0;
        var reused = 0;
        var poor = new List<string>();

        foreach (var page in ArticleListCorpusTests.Corpus.Where(p => p.IsList))
        {
            var uri = new Uri(page.Url);
            var document = Parse(page.Name, uri);
            var result = new IndexPageExtractor().Detect(document, uri);
            if (!result.IsIndex) continue;
            accepted++;

            var learned = inducer.InduceFromExamples(
                Guid.NewGuid(),
                document,
                result.Items.Select(i => new RecordExample(i.Title, i.Link, i.Published, i.Summary)).ToList());
            if (learned is null) continue;

            var records = applicator.Apply(Parse(page.Name, uri), learned);
            if (records.Count >= result.Items.Count * 0.67) reused++;
            else poor.Add($"{page.Name} ({records.Count} of {result.Items.Count})");
        }

        Assert.True(reused >= 13,
            $"Only {reused} of {accepted} accepted pages round-tripped through a template. " +
            $"Short: {string.Join("; ", poor)}");
    }
}
