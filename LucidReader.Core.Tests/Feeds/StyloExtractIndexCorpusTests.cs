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
/// <para><b>Two numbers, not one.</b> Classification is whether the library
/// calls a page an index; item extraction is whether the links it hands back
/// are that page's articles. A page can be classified right and extracted
/// wrong, which is the worse failure of the two, because a caller that stores
/// the answer stores rubbish under a confident label. The two are recorded
/// separately below and neither is allowed to fall.</para>
///
/// <para>Both floors are what the library scores today, not targets. Before
/// this path existed the only way to ask the library anything was to count
/// RepeatedItem blocks, which emitted on five of the twenty-five pages and got
/// the links right on one. That number is not comparable with either floor
/// here and is recorded only as where this started.</para>
/// </summary>
public class StyloExtractIndexCorpusTests
{
    /// <summary>
    /// How many of the twenty-five the library classifies as a person would.
    /// Raising this is the last step of any change that improves it, exactly as
    /// on <see cref="ArticleListCorpusTests"/>.
    /// </summary>
    public const int ClassificationFloor = 24;

    /// <summary>
    /// How many of the eighteen index pages the library extracts the right
    /// items from, judged against <see cref="ItemShapes"/>. This is a different
    /// question from the one above and has its own floor for that reason.
    /// </summary>
    public const int ItemFloor = 18;

    /// <summary>
    /// The one page the library classifies wrong, and why it is recorded rather
    /// than deleted or re-labelled.
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

    /// <summary>
    /// What a correct item link looks like on one page, written down once as a
    /// human judgement about that page.
    ///
    /// <para>The alternative, comparing the library's links against mylo's
    /// detector, was tried and is too noisy to gate on: bbc-news and
    /// theregister overlap by nothing at all while both answers are valid,
    /// because the two implementations pick different sections of the same
    /// page. Agreement between two programs is not correctness. A statement of
    /// what the page's articles actually live at is.</para>
    /// </summary>
    /// <param name="Name">Fixture name, matching <see cref="ArticleListCorpusTests.Corpus"/>.</param>
    /// <param name="What">What a reader would say a correct link on this page
    /// is, in words, including where the page legitimately links off its own
    /// host.</param>
    /// <param name="Accepts">The same statement as a predicate, applied to each
    /// item link resolved against the page's address.</param>
    /// <param name="MinimumShare">The share of items that must satisfy it. One
    /// unless the page is recorded as partly wrong, in which case
    /// <paramref name="Shortfall"/> says which items and why.</param>
    /// <param name="Shortfall">Set when the share is under one, naming what the
    /// remaining items are.</param>
    public sealed record ItemShape(
        string Name,
        string What,
        Func<Uri, bool> Accepts,
        double MinimumShare = 1.0,
        string? Shortfall = null);

    private static bool PathStartsWith(Uri uri, string prefix) =>
        uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal);

    private static bool PathIsDatedUnderASection(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 3
            && segments[1].Length == 4
            && segments[1].All(char.IsAsciiDigit);
    }

    private static bool PathStartsWithAYear(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2
            && segments[0].Length == 4
            && segments[0].All(char.IsAsciiDigit);
    }

    private static bool Host(Uri uri, string host) =>
        string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One statement per index page in the corpus. Seven of the twenty-five
    /// pages are not indexes and have no entry, because there is no correct set
    /// of items for a page that should yield none.
    /// </summary>
    public static readonly ItemShape[] ItemShapes =
    [
        // Aggregators. The whole point of the page is to link somewhere else,
        // so an off-host address is the right answer and the only thing worth
        // checking on the site's own host is that the link is a story rather
        // than the furniture around one.
        new("hn",
            "A story's own address anywhere on the web, or news.ycombinator.com/item " +
            "for a text post. Any other address on news.ycombinator.com is site " +
            "furniture: a user page, a domain filter, the next-page link.",
            uri => !Host(uri, "news.ycombinator.com") || uri.AbsolutePath == "/item"),

        new("lobsters",
            "A story's own address anywhere on the web, or a lobste.rs/s/ discussion " +
            "page. lobste.rs/domains/ is the filter for everything published by one " +
            "site and is not a story.",
            uri => !Host(uri, "lobste.rs") || PathStartsWith(uri, "/s/"),
            MinimumShare: 0.90,
            Shortfall:
                "Two of the twenty-five entries answer with the /domains/ filter " +
                "beside the headline instead of the headline's own link, because " +
                "the domain name is longer than those two headlines are and the " +
                "longest link in an entry is what is taken. It is wrong and it is " +
                "recorded rather than accommodated."),

        new("slashdot",
            "A story's own address anywhere on the web, or a /story/ page on any of " +
            "slashdot's section hosts.",
            uri => !uri.Host.EndsWith("slashdot.org", StringComparison.OrdinalIgnoreCase)
                   || uri.AbsolutePath.Contains("/story/", StringComparison.Ordinal)),

        // Weblogs. Everything is on the site's own host and under the path the
        // site files its entries at.
        new("mostlylucid-blog",
            "www.mostlylucid.net/blog/<slug>.",
            uri => Host(uri, "www.mostlylucid.net")
                   && PathStartsWith(uri, "/blog/")
                   && uri.AbsolutePath.Length > "/blog/".Length),

        new("jvns", "jvns.ca/blog/<date>/<slug>.",
            uri => Host(uri, "jvns.ca") && PathStartsWith(uri, "/blog/")),

        new("simonwillison", "simonwillison.net/<year>/<month>/<day>/<slug>.",
            uri => Host(uri, "simonwillison.net") && PathStartsWithAYear(uri)),

        new("rustblog", "blog.rust-lang.org/<year>/<month>/<slug>.",
            uri => Host(uri, "blog.rust-lang.org") && PathStartsWithAYear(uri)),

        new("dotnetblog", "devblogs.microsoft.com/dotnet/<slug>.",
            uri => Host(uri, "devblogs.microsoft.com")
                   && PathStartsWith(uri, "/dotnet/")
                   && uri.AbsolutePath.Length > "/dotnet/".Length),

        new("daringfireball", "daringfireball.net/<year>/<month>/<slug>.",
            uri => Host(uri, "daringfireball.net") && PathStartsWithAYear(uri)),

        new("danluu",
            "danluu.com/<slug>, or patreon.com/posts/<id>. The Patreon entries are " +
            "not an oddity to be filtered out: danluu.com's index lists them beside " +
            "the rest and they are where those pieces are published.",
            uri => (Host(uri, "danluu.com") && uri.AbsolutePath.Length > 1)
                   || (Host(uri, "www.patreon.com") && PathStartsWith(uri, "/posts/"))),

        new("overreacted", "overreacted.io/<slug>.",
            uri => Host(uri, "overreacted.io") && uri.AbsolutePath.Length > 1),

        new("martinfowler",
            "martinfowler.com/bliki/<Entry>.html. The bliki quotes heavily and the " +
            "outside links inside each entry are citations, not entries.",
            uri => Host(uri, "martinfowler.com")
                   && PathStartsWith(uri, "/bliki/")
                   && uri.AbsolutePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)),

        new("githubblog", "github.blog/<slug>.",
            uri => Host(uri, "github.blog") && uri.AbsolutePath.Length > 1),

        // News front pages.
        new("theregister", "www.theregister.com/<section>/<year>/<...>.",
            uri => Host(uri, "www.theregister.com") && PathIsDatedUnderASection(uri)),

        new("arstechnica", "arstechnica.com/<section>/<year>/<...>.",
            uri => Host(uri, "arstechnica.com") && PathIsDatedUnderASection(uri)),

        new("theverge-home",
            "A Verge story, whose path always carries a numeric story id, or the " +
            "outside address a quick post points at. The Verge's quick posts are " +
            "one-paragraph entries whose subject is somebody else's story, and the " +
            "link the entry is about is that story, so an off-host address here is " +
            "the entry's own address rather than a citation.",
            uri => !Host(uri, "www.theverge.com")
                   || uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                          .Any(s => s.All(char.IsAsciiDigit))),

        new("bbc-news", "www.bbc.co.uk/news/articles/<id>.",
            uri => Host(uri, "www.bbc.co.uk") && PathStartsWith(uri, "/news/articles/")),

        new("lwn",
            "lwn.net/Articles/<id>/. Every headline on the front page is an h2 with " +
            "no link in it, and the article address is only reachable from the blurb " +
            "underneath, where kernel.org and other quoted sites sit closer to the " +
            "headline than the article does.",
            uri => Host(uri, "lwn.net") && PathStartsWith(uri, "/Articles/"))
    ];

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

    public static TheoryData<string> IndexNames()
    {
        var data = new TheoryData<string>();
        foreach (var shape in ItemShapes) data.Add(shape.Name);
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
                $"Delete the entry, raise ClassificationFloor and say so in the commit. " +
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

        Assert.True(correct >= ClassificationFloor,
            $"Library classification fell to {correct}/{ArticleListCorpusTests.Corpus.Length}, " +
            $"under the recorded {ClassificationFloor}. Wrong: {string.Join("; ", wrong)}");
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
    /// addresses are filled in at all. This is the cheap check; it says nothing
    /// about whether the addresses are the right ones, which is
    /// <see cref="Item_extraction_matches_the_pages_own_shape"/>.
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
    /// The links the library hands back are this page's articles, judged
    /// against what <see cref="ItemShapes"/> says an article on this page looks
    /// like.
    ///
    /// <para>This is the gate the previous one was not. "comments: 16" pointing
    /// at lwn.net/Articles/1090376/#Comments and an Amazon affiliate link
    /// quoted inside a bliki entry both have a title and an address, and both
    /// passed. Neither is an article on the page it was taken from.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(IndexNames))]
    public void Item_extraction_matches_the_pages_own_shape(string name)
    {
        var (matched, total, wrong) = Judge(name);
        var shape = ItemShapes.Single(s => s.Name == name);

        Assert.True(total >= IndexPageExtractor.MinimumItems,
            $"{name} yielded {total} items, too few to judge.");

        var share = matched / (double)total;
        Assert.True(share >= shape.MinimumShare,
            $"{name}: {matched} of {total} item links look like this page's articles, " +
            $"under the recorded {shape.MinimumShare:0.00}. A link on this page should be: " +
            $"{shape.What}" +
            (shape.Shortfall is null ? string.Empty : $" Recorded shortfall: {shape.Shortfall}") +
            $" The ones that do not match: {string.Join("; ", wrong.Take(6))}");
    }

    [Fact]
    public void The_corpus_item_extraction_is_at_or_above_the_recorded_accuracy()
    {
        var right = 0;
        var wrong = new List<string>();

        foreach (var shape in ItemShapes)
        {
            var (matched, total, _) = Judge(shape.Name);
            if (total >= IndexPageExtractor.MinimumItems
                && matched / (double)total >= shape.MinimumShare)
            {
                right++;
            }
            else
            {
                wrong.Add($"{shape.Name} ({matched}/{total})");
            }
        }

        Assert.True(right >= ItemFloor,
            $"Library item extraction fell to {right}/{ItemShapes.Length}, under the " +
            $"recorded {ItemFloor}. Wrong: {string.Join("; ", wrong)}");
    }

    /// <summary>
    /// Every index page in the corpus carries a statement of what its links
    /// should look like. A page listed as an index with no statement is a page
    /// whose items nothing measures.
    /// </summary>
    [Fact]
    public void Every_index_page_in_the_corpus_has_a_recorded_item_shape()
    {
        var expected = ArticleListCorpusTests.Corpus.Where(p => p.IsList).Select(p => p.Name).Order();
        var recorded = ItemShapes.Select(s => s.Name).Order();

        Assert.Equal(expected, recorded);
    }

    private static (int Matched, int Total, List<string> Wrong) Judge(string name)
    {
        var shape = ItemShapes.Single(s => s.Name == name);
        var page = ArticleListCorpusTests.Corpus.Single(p => p.Name == name);
        var address = new Uri(page.Url);

        var matched = 0;
        var wrong = new List<string>();
        var items = Detect(page).Items;

        foreach (var item in items)
        {
            // Items carry the href as written, which on several of these pages
            // is relative, so it is resolved against the page it was read from
            // before being judged.
            if (Uri.TryCreate(address, item.Link, out var resolved) && shape.Accepts(resolved))
            {
                matched++;
            }
            else
            {
                wrong.Add($"[{item.Title}] -> {item.Link}");
            }
        }

        return (matched, items.Count, wrong);
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
