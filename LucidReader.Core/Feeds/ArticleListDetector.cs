using System.Text.RegularExpressions;

namespace LucidReader.Core.Feeds;

/// <summary>
/// One article the detector believes a page is listing.
///
/// CanonicalId is <see cref="CanonicalArticleId.FromLink"/> applied to Link,
/// computed here rather than left to the caller so a scraped article and the
/// same article arriving later from a real feed carry the same identity and
/// dedupe against one another. A candidate with no usable canonical id is
/// never produced at all, so this is never null.
/// </summary>
public sealed record DetectedArticle(
    string Title,
    string Link,
    string CanonicalId,
    DateTimeOffset? PublishedUtc,
    string? Summary);

/// <summary>
/// What the detector concluded about one page.
///
/// Confidence is reported whether or not the page passed, and Reason always
/// says which rule decided it. Both are surfaced: the user is asked to approve
/// a guess, and a guess that cannot explain itself is not one anybody can
/// judge.
/// </summary>
public sealed record ArticleListDetection
{
    public static readonly ArticleListDetection None =
        new() { Reason = "No repeated run of article links was found." };

    /// <summary>
    /// What the best-scoring run of repeated links turned out to be.
    ///
    /// Populated even when <see cref="IsArticleList"/> is false, so a run that
    /// scored 0.40 can be looked at rather than only counted. Nothing acts on
    /// this without checking IsArticleList first, and nothing should: a list
    /// that did not clear the bar is a list this code does not believe in.
    /// </summary>
    public IReadOnlyList<DetectedArticle> Articles { get; init; } = [];

    /// <summary>0 to 1. See ArticleListDetector's remarks for the weights.</summary>
    public double Confidence { get; init; }

    public bool IsArticleList { get; init; }

    public required string Reason { get; init; }

    /// <summary>
    /// Why the page says it is one article rather than a list of them, or null
    /// when it says no such thing.
    ///
    /// Reported separately from <see cref="Reason"/> because it is a fact about
    /// the page rather than about the run that scored best, and because
    /// <see cref="IndexFallbackReader"/> needs it: the fallback runs on pages
    /// this detector declined and would otherwise have no way of knowing the
    /// publisher already said what the page is.
    /// </summary>
    public string? SingleArticleDeclaration { get; init; }

    /// <summary>
    /// The first few titles, for the approval prompt. A count on its own does
    /// not let anyone tell "it found the articles" from "it found the tag
    /// cloud"; the titles do.
    /// </summary>
    public IReadOnlyList<string> SampleTitles(int count) =>
        Articles.Take(count).Select(a => a.Title).ToList();
}

/// <summary>
/// Decides whether a page is an index of articles, and if so which articles.
///
/// The question this answers is structural, not semantic. An index page is a
/// page that repeats one shape: a template emitted the same container once per
/// article, and each container holds a link to the article and, usually, a
/// date. That repetition is the strongest available evidence, and it is what
/// the scoring is built around. Nothing here reads the network, so the whole
/// thing is testable against saved pages.
///
/// The five signals, and the weight each carries:
///
///   repetition   0.30  How many siblings of one shape were found, saturating
///                      at 20. A run of four is weak evidence; a run of thirty
///                      is close to proof a template produced it.
///   title text   0.25  The fraction of members whose chosen link carries text
///                      of a plausible article-title length and shape. This is
///                      what rejects a nav bar, a footer link list, a tag
///                      cloud and a row of "Read more" links, all of which are
///                      repeated siblings with links in them.
///   dates        0.15  The fraction with a date near the link. A date is the
///                      single most article-like thing a list item can carry,
///                      but its absence must not disqualify: Hacker News has
///                      no dates in its list and is unambiguously an index.
///                      So this is a bonus worth 0.15, not a gate.
///   same host    0.15  The fraction of links pointing at the page's own host.
///                      A bonus, not a requirement, for the same reason: an
///                      aggregator's whole purpose is to link elsewhere.
///   distinct     0.15  Distinct links as a fraction of members. Pagination
///                      and "share this" rows repeat one target.
///
/// A group must also clear the gates listed on <see cref="Reject"/> before it
/// is scored at all. Those exist because a low score and a wrong answer are
/// different failures: the gates rule out shapes that are not article lists at
/// all, and the score then ranks the ones that might be. Where the score is a
/// judgement about degree, the gates are judgements about kind, and most of
/// what a real corpus rejects turns out to be the second sort - a card of
/// undated links, a documentation tree listed inside itself, a run of further
/// reading at the foot of an article.
///
/// Two things narrow the search before any of that. Runs inside a nav, a
/// header, a footer or an aside are not looked at, because a site's own markup
/// has already said they are chrome. And a page that declares itself an
/// article, through og:type or through schema.org, has to score
/// <see cref="DeclaredConfidenceThreshold"/> rather than
/// <see cref="ConfidenceThreshold"/> - the declaration is the publisher's own
/// statement and is nearly always right, because a news article's "more
/// stories" rail is a genuine, well-formed list of articles, structurally
/// indistinguishable from an index and measured at 0.63 to 0.76 on real
/// article pages. What it is not is infallible: github.blog serves its own
/// front page as a schema.org TechArticle headed "Home". So the declaration
/// raises the bar instead of closing the door, and a page whose address is a
/// site root ignores it outright, because nothing that lives at "/" is one
/// piece of writing.
///
/// Where a page makes no declaration at all, the approval step is what stands
/// between a wrong guess and a stored subscription.
///
/// The whole thing is measured rather than argued about:
/// ArticleListCorpusTests runs it over twenty-five saved pages and asserts
/// both the per-page answers and the total. It went from 17 of 25 to 24 of 25
/// when the gates above were added.
///
/// Every link is resolved to an absolute address and run through
/// <see cref="FeedUrlPolicy"/> before it can become a candidate. This class
/// makes no requests itself, but what it returns is stored, shown, and later
/// fetched by the offline downloader, and the links came out of a remote
/// document - the same shape that made OPML import an SSRF primitive.
/// </summary>
public static partial class ArticleListDetector
{
    /// <summary>
    /// The fewest repeated siblings that can be an article list. Three rows of
    /// anything is a nav bar as often as it is an index, and a page that
    /// really does list only three articles is a page whose owner will not
    /// miss this feature.
    /// </summary>
    public const int MinimumArticles = 4;

    /// <summary>
    /// Where repetition stops earning credit. Past twenty siblings the shape
    /// is not in doubt and more of them says nothing new.
    /// </summary>
    private const int RepetitionSaturation = 20;

    /// <summary>
    /// The most articles one detection returns. A very long archive page can
    /// list hundreds; storing all of them on the first refresh of a scraped
    /// feed would bury everything else in the user's unread list.
    /// </summary>
    public const int MaxArticles = 100;

    /// <summary>
    /// The score a page has to reach. Set where it is because of what the
    /// weights add up to for the two shapes that matter: an on-host blog index
    /// with dates scores near 1.0, and Hacker News - no dates at all, almost
    /// nothing on-host - scores about 0.86, so the bar has room under both. A
    /// nav bar or a footer link list does not reach it even when it survives
    /// the gates, because its title text fails.
    /// </summary>
    public const double ConfidenceThreshold = 0.55;

    /// <summary>
    /// The score a page that calls itself an article has to reach before the
    /// structure is believed over the declaration.
    ///
    /// The declaration used to end the scan outright, and for article pages it
    /// is still nearly always right: a news article's "more stories" rail is a
    /// genuine, well-formed list of articles that scores 0.63 to 0.76, and
    /// nothing structural tells it from an index. What the outright gate could
    /// not survive is a site that mislabels its own front page, which
    /// github.blog does. So the declaration now sets a much higher bar rather
    /// than closing the door, and the bar sits above every measured article
    /// page's rail and below a real index's score.
    /// </summary>
    public const double DeclaredConfidenceThreshold = 0.90;

    /// <summary>
    /// How long a paragraph has to be to count as prose rather than a caption,
    /// a byline or a standfirst.
    /// </summary>
    private const int ProseParagraphLength = 250;

    /// <summary>
    /// How much prose a page carries before it is treated as being an article
    /// itself, given its address also looks like one article's.
    /// </summary>
    private const int ArticleProseCharacters = 2000;

    /// <summary>
    /// The fewest links a run on a page that is itself an article has to hold
    /// before it is read as an index anyway. A piece with a list of further
    /// reading at the foot of it carries a handful; a genuine archive page
    /// carries far more.
    /// </summary>
    private const int MinimumArticlesOnAnArticlePage = 20;

    /// <summary>
    /// The fewest entries a run carrying no date at all can hold and still be
    /// an index. A date remains a bonus rather than a requirement - Hacker
    /// News, Slashdot and danluu.com all publish lists without one - but an
    /// undated run has to make the case on repetition instead, and the undated
    /// lists that are real run to fifteen and a hundred entries while a
    /// documentation hub's card of links runs to six. Half of
    /// <see cref="RepetitionSaturation"/> is where that line is drawn.
    /// </summary>
    private const int MinimumUndatedRun = 10;

    private const int MinTitleLength = 15;

    /// <summary>
    /// The shortest a two-word heading can be and still be an article title,
    /// used only when the entry also carries a date. Martin Fowler's bliki is
    /// why: its entries are titled "Vibe Coding" and "November Inflection",
    /// which the three-word rule rejects outright. A date next to a heading is
    /// what separates those from "Contact Us".
    /// </summary>
    private const int MinDatedTitleLength = 10;
    private const int MaxTitleLength = 250;
    private const int MinSummaryLength = 60;
    private const int MaxSummaryLength = 500;

    /// <summary>
    /// Link text that is furniture rather than a title, matched whole and
    /// case-insensitively after punctuation is trimmed. These are short enough
    /// that MinTitleLength catches most of them anyway; the list is here for
    /// the ones that are not ("Continue reading this article", "Older posts").
    /// </summary>
    private static readonly HashSet<string> ChromeText = new(StringComparer.OrdinalIgnoreCase)
    {
        "read more", "more", "read the full story", "continue reading",
        "continue reading this article", "next", "previous", "next page",
        "previous page", "older posts", "newer posts", "older", "newer",
        "home", "about", "about us", "contact", "contact us", "subscribe",
        "comments", "share", "tags", "categories", "archive", "archives",
        "login", "log in", "sign in", "sign up", "menu", "search", "rss",
        "privacy policy", "terms of service", "cookie policy", "back to top",
        "skip to content", "view all", "see all", "learn more"
    };

    /// <summary>
    /// Class-name fragments that mean "there is a date here". Checked against
    /// an element's class list, then its title attribute and then its text,
    /// which is the order of decreasing precision: a machine-readable value in
    /// an attribute beats a rendered string every time.
    /// </summary>
    private static readonly string[] DateClassHints =
        ["date", "time", "age", "published", "posted", "meta-date", "timestamp"];

    public static ArticleListDetection Detect(string html, Uri pageUri) =>
        Detect(html, pageUri, MaxArticles);

    public static ArticleListDetection Detect(string html, Uri pageUri, int maxArticles)
    {
        if (string.IsNullOrWhiteSpace(html)) return ArticleListDetection.None;

        var root = HtmlOutline.Parse(html);
        var declaration = DeclaredAsArticle(html, root, pageUri);
        var page = PageShape.Of(pageUri, root);

        ScoredGroup? best = null;

        foreach (var container in EnumerateContainers(root))
        {
            foreach (var group in GroupSiblings(container))
            {
                var scored = Score(group, page, maxArticles);
                if (scored is null) continue;
                if (best is null || scored.Confidence > best.Confidence) best = scored;
            }
        }

        if (best is null)
            return declaration is null
                ? ArticleListDetection.None
                : new ArticleListDetection
                {
                    Reason = declaration,
                    SingleArticleDeclaration = declaration
                };

        var required = declaration is null ? ConfidenceThreshold : DeclaredConfidenceThreshold;

        return new ArticleListDetection
        {
            Articles = best.Articles,
            Confidence = best.Confidence,
            IsArticleList = best.Confidence >= required,
            SingleArticleDeclaration = declaration,
            Reason = best.Confidence >= required
                ? $"Found {best.Articles.Count} repeated article links " +
                  $"(confidence {best.Confidence:0.00})."
                : declaration is null
                    ? $"The best run of repeated links scored {best.Confidence:0.00}, " +
                      $"under the {ConfidenceThreshold:0.00} needed."
                    : $"{declaration} Its best run of repeated links scored " +
                      $"{best.Confidence:0.00}, under the {DeclaredConfidenceThreshold:0.00} " +
                      "that would outweigh the declaration."
        };
    }

    /// <summary>
    /// The things about the whole page that change how a run inside it should
    /// be read, worked out once because every group on the page shares them.
    ///
    /// ProseCharacters counts the text in substantial paragraphs. It does not
    /// separate an index from an article on its own - plenty of indexes publish
    /// entries in full, and Daring Fireball's front page carries more prose
    /// than most articles do - but paired with an address that looks like one
    /// article's it is what tells a piece with a list of further reading at the
    /// bottom from a page whose whole purpose is that list.
    /// </summary>
    private sealed record PageShape(
        Uri Uri, string? Identity, int ProseCharacters, bool AddressLooksLikeOneArticle)
    {
        public static PageShape Of(Uri pageUri, HtmlElement root) =>
            new(pageUri,
                CanonicalArticleId.FromLink(pageUri.ToString()),
                ProseCharacters: root.Descendants()
                    .Where(e => e.Tag == "p" && !e.Descendants().Any(d => d.Tag == "p"))
                    .Select(e => e.TextContent.Length)
                    .Where(length => length >= ProseParagraphLength)
                    .Sum(),
                AddressLooksLikeOneArticle: HasSlugLikeAddress(pageUri));

        /// <summary>
        /// Whether the page's own address is the shape a single article's is
        /// rather than a section's. Index pages sit at a bare host or a short
        /// section name; articles sit at a slug or a page file. Every index in
        /// the measured corpus is at "/", "/blog", "/news" or "/bliki/", and
        /// every article at a hyphenated slug or an .html file.
        /// </summary>
        private static bool HasSlugLikeAddress(Uri pageUri)
        {
            var segments = pageUri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return false;

            var last = segments[^1];
            return last.Contains('-')
                   || last.Contains('_')
                   || last.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                   || last.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
                   || last.EndsWith(".php", StringComparison.OrdinalIgnoreCase)
                   || last.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Why the page is one article rather than a list of them, according to
    /// the page itself, or null if it says no such thing.
    ///
    /// Two independent declarations are honoured, and either is enough. Both
    /// are statements the publisher wrote for machines to read, which is worth
    /// more than anything this class can infer from the markup around it: an
    /// article page on a news site carries a "more stories" rail that is a
    /// genuine, well-formed list of articles, and no amount of structural
    /// reasoning will tell that rail apart from an index. The publisher
    /// already has.
    ///
    /// The schema.org check is deliberately narrow. A blog index legitimately
    /// describes its entries as BlogPosting inside an ItemList, a Blog or a
    /// CollectionPage, so a block carrying any of those markers is a listing
    /// describing its contents, not a page calling itself an article, and is
    /// left alone. Everything is read out of the raw HTML because the outline
    /// deliberately skips script contents.
    /// </summary>
    private static string? DeclaredAsArticle(string html, HtmlElement root, Uri pageUri)
    {
        // A site's front page is not one article, whatever its markup says.
        // github.blog serves its homepage with a schema.org TechArticle headed
        // "Home" whose mainEntityOfPage is the site root, and the outright gate
        // believed it. Nothing that lives at "/" is a single piece of writing,
        // so the declaration is read as the template misfiring and dropped.
        if (pageUri.AbsolutePath.Trim('/').Length == 0
            && string.IsNullOrEmpty(pageUri.Query)) return null;

        var openGraph = root.Descendants().Any(e =>
            e.Tag == "meta"
            && string.Equals(e.Attribute("property"), "og:type", StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Attribute("content"), "article", StringComparison.OrdinalIgnoreCase));

        if (openGraph)
            return "The page declares itself a single article (og:type=article).";

        foreach (Match block in LinkedDataPattern().Matches(html))
        {
            var json = block.Groups["body"].Value;
            if (ListingMarkers.Any(m => json.Contains(m, StringComparison.OrdinalIgnoreCase))) continue;
            if (!ArticleTypePattern().IsMatch(json)) continue;

            return "The page declares itself a single article (schema.org Article).";
        }

        return null;
    }

    /// <summary>
    /// Markers that turn a block of schema.org into a description of a
    /// collection rather than of one article. Any of them present and the
    /// block's article types are the collection's members, not the page.
    /// </summary>
    private static readonly string[] ListingMarkers =
        ["itemListElement", "\"ItemList\"", "\"CollectionPage\"", "\"Blog\"", "blogPost"];

    /// <summary>
    /// Elements the page's own markup says are chrome. A run of repeated links
    /// inside one of these is a menu, a breadcrumb, a site footer or a sidebar
    /// rail, and no amount of structural scoring separates those from an index
    /// - GitHub's global header holds five links whose text reads exactly like
    /// article titles ("Actions Automate any workflow") and scored 0.62.
    ///
    /// The whole subtree is skipped rather than penalised. An index page does
    /// not put its index inside its own nav, and the elements that do show up
    /// inside cards - a per-card &lt;header&gt; wrapping the heading - are not
    /// where the run's container is anyway.
    /// </summary>
    private static readonly HashSet<string> ChromeElements =
        new(StringComparer.OrdinalIgnoreCase) { "nav", "header", "footer", "aside" };

    private static IEnumerable<HtmlElement> EnumerateContainers(HtmlElement root)
    {
        if (root.Children.Count >= MinimumArticles) yield return root;

        var pending = new Stack<HtmlElement>();
        for (var i = root.Children.Count - 1; i >= 0; i--) pending.Push(root.Children[i]);

        while (pending.Count > 0)
        {
            var element = pending.Pop();
            if (ChromeElements.Contains(element.Tag)) continue;

            if (element.Children.Count >= MinimumArticles) yield return element;

            for (var i = element.Children.Count - 1; i >= 0; i--) pending.Push(element.Children[i]);
        }
    }

    /// <summary>
    /// The element children of one container, split into runs that share a
    /// tag and a class list, plus a run per tag with the classes ignored.
    ///
    /// The class-exact grouping is the precise one and is what most templates
    /// produce. The tag-only run is there because a large family of templates
    /// stamps a per-item class on every row: WordPress writes
    /// "post-98460 category-ai-and-ml tag-github-copilot" on each list item, so
    /// no two rows share a signature and the strongest evidence on the page -
    /// twenty identical cards - split into twenty runs of one. Ignoring the
    /// classes puts those back together.
    ///
    /// Both are offered and the higher score wins, so the loose grouping can
    /// only find a run the strict one missed. It cannot smuggle a weak one
    /// past, because a tag bucket that mixes a list with other markup loses on
    /// the coverage and title gates in <see cref="Reject"/>.
    ///
    /// Groups smaller than MinimumArticles are dropped here rather than scored
    /// and rejected, since a group that small can never clear the size gate.
    /// </summary>
    private static IEnumerable<List<HtmlElement>> GroupSiblings(HtmlElement container)
    {
        var byShape = new Dictionary<string, List<HtmlElement>>(StringComparer.Ordinal);
        var byTag = new Dictionary<string, List<HtmlElement>>(StringComparer.Ordinal);

        foreach (var child in container.Children)
        {
            var signature = child.Tag + "|" + child.ClassSignature;
            if (!byShape.TryGetValue(signature, out var members))
                byShape[signature] = members = [];
            members.Add(child);

            if (!byTag.TryGetValue(child.Tag, out var sameTag))
                byTag[child.Tag] = sameTag = [];
            sameTag.Add(child);
        }

        foreach (var group in byShape.Values)
            if (group.Count >= MinimumArticles)
                yield return group;

        foreach (var (tag, group) in byTag)
        {
            if (group.Count < MinimumArticles) continue;
            if (byShape.ContainsKey(tag + "|" + group[0].ClassSignature)
                && group.All(e => e.ClassSignature == group[0].ClassSignature)) continue;

            yield return group;
        }
    }

    private sealed record ScoredGroup(IReadOnlyList<DetectedArticle> Articles, double Confidence);

    private sealed record Candidate(
        DetectedArticle Article,
        bool TitleIsPlausible,
        bool IsSameHost,
        string LinkHost,
        string PathKey);

    private static ScoredGroup? Score(List<HtmlElement> members, PageShape page, int maxArticles)
    {
        var candidates = new List<Candidate>(members.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in members)
        {
            var candidate = ReadCandidate(member, page);
            if (candidate is null) continue;
            if (!seen.Add(candidate.Article.CanonicalId)) continue;
            candidates.Add(candidate);
        }

        if (Reject(candidates, members.Count, page)) return null;

        var count = candidates.Count;
        var repetition = Math.Min(count, RepetitionSaturation) / (double)RepetitionSaturation;
        var titleQuality = candidates.Count(c => c.TitleIsPlausible) / (double)count;
        var dated = candidates.Count(c => c.Article.PublishedUtc is not null) / (double)count;
        var sameHost = candidates.Count(c => c.IsSameHost) / (double)count;

        // Distinct links are already guaranteed by the seen set above, so this
        // measures the members that produced a distinct one: a run where half
        // the rows all link to the same target loses the other half here.
        var distinct = count / (double)members.Count;

        var confidence =
            0.30 * repetition +
            0.25 * titleQuality +
            0.15 * dated +
            0.15 * sameHost +
            0.15 * Math.Min(distinct, 1.0);

        return new ScoredGroup(
            candidates.Take(maxArticles).Select(c => c.Article).ToList(),
            confidence);
    }

    /// <summary>
    /// The gates a group has to clear before its score means anything.
    ///
    ///   too few          Fewer than MinimumArticles usable candidates.
    ///   thin coverage    Under 60% of the members yielded one. A run of
    ///                    twenty divs where six hold an article link is a
    ///                    layout grid, not a list.
    ///   repeated titles  Under 80% of the titles are distinct. A tag cloud,
    ///                    a pagination strip and a column of "Read more" links
    ///                    all repeat one string.
    ///   weak titles      Under 60% of the link texts look like article
    ///                    titles. This is the gate a nav bar and a footer link
    ///                    list fail.
    ///   stranded host    Almost nothing on the page's own host, and only one
    ///                    or two other hosts between them - a blogroll or an
    ///                    embedded widget pointing at somebody else's site.
    ///                    An aggregator is the opposite shape, dozens of
    ///                    distinct hosts, and is deliberately not caught here.
    ///   nested paths     More than a tenth of the links sit underneath another
    ///                    link in the same run. That is a documentation tree
    ///                    describing itself, where "/aspnet/core" and
    ///                    "/aspnet/core/tutorials" are both entries. Articles
    ///                    are never filed inside one another.
    ///   article's own    The page is itself an article and the run is small.
    ///     footer         A piece with further reading at the foot of it holds
    ///                    a handful of links that are, individually, real
    ///                    articles; only a genuine archive holds twenty.
    ///   short and        A short run with no date anywhere in it. Undated
    ///     undated        indexes exist and are welcome, but they are archives
    ///                    of dozens of entries, not cards of six.
    /// </summary>
    private static bool Reject(List<Candidate> candidates, int memberCount, PageShape page)
    {
        if (candidates.Count < MinimumArticles) return true;

        if (page.AddressLooksLikeOneArticle
            && page.ProseCharacters >= ArticleProseCharacters
            && candidates.Count < MinimumArticlesOnAnArticlePage) return true;

        if (NestedPathFraction(candidates) > 0.10) return true;

        if (candidates.Count < MinimumUndatedRun
            && candidates.All(c => c.Article.PublishedUtc is null)) return true;

        if (candidates.Count / (double)memberCount < 0.60) return true;

        var distinctTitles = candidates
            .Select(c => NormaliseTitle(c.Article.Title))
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinctTitles / (double)candidates.Count < 0.80) return true;

        if (candidates.Count(c => c.TitleIsPlausible) / (double)candidates.Count < 0.60) return true;

        var sameHost = candidates.Count(c => c.IsSameHost) / (double)candidates.Count;
        if (sameHost < 0.15)
        {
            var otherHosts = candidates
                .Where(c => !c.IsSameHost)
                .Select(c => c.LinkHost)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (otherHosts <= 2) return true;
        }

        return false;
    }

    private static string NormaliseTitle(string title) =>
        title.Trim().ToLowerInvariant();

    /// <summary>
    /// The share of the run's links that another link in the same run sits
    /// above in the path hierarchy. Zero on every article index measured; a
    /// third on a documentation hub, which lists a section and that section's
    /// pages side by side.
    /// </summary>
    private static double NestedPathFraction(List<Candidate> candidates)
    {
        var paths = candidates.Select(c => c.PathKey).ToList();
        var prefixes = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);

        var nested = 0;
        foreach (var path in paths)
        {
            var parent = path;
            while (true)
            {
                var cut = parent.LastIndexOf('/');
                if (cut <= 0) break;
                parent = parent[..cut];
                if (!prefixes.Contains(parent)) continue;
                nested++;
                break;
            }
        }

        return nested / (double)paths.Count;
    }

    private static Candidate? ReadCandidate(HtmlElement member, PageShape page)
    {
        var pageUri = page.Uri;
        var pageIdentity = page.Identity;

        HtmlElement? bestAnchor = null;
        var bestText = string.Empty;
        string? bestLink = null;

        // Prepend rather than Descendants alone: plenty of templates make the
        // repeated element itself the link and put the heading, date and
        // standfirst inside it. Looking only at descendants found no anchor on
        // those pages at all, so the run scored nothing.
        foreach (var anchor in member.Descendants().Prepend(member))
        {
            if (anchor.Tag != "a") continue;

            var href = anchor.Attribute("href");
            if (string.IsNullOrWhiteSpace(href)) continue;
            if (href.StartsWith('#')) continue;

            if (!Uri.TryCreate(pageUri, href, out var absolute)) continue;
            if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps) continue;

            // The gate every address that came out of somebody else's document
            // passes before this app will store it or fetch it. See the class
            // remarks: these links are stored as items and later downloaded.
            if (!FeedUrlPolicy.IsAllowed(absolute.ToString())) continue;

            var identity = CanonicalArticleId.FromLink(absolute.ToString());
            if (identity is null) continue;

            // A link back to the page being scanned is the "you are here" link
            // every index carries, not one of the articles it lists.
            if (pageIdentity is not null
                && string.Equals(identity, pageIdentity, StringComparison.Ordinal)) continue;

            var text = anchor.TextContent.Trim();
            if (text.Length <= bestText.Length) continue;

            bestAnchor = anchor;
            bestText = text;
            bestLink = absolute.ToString();
        }

        if (bestAnchor is null || bestLink is null) return null;

        var canonical = CanonicalArticleId.FromLink(bestLink);
        if (canonical is null) return null;

        var title = Truncate(bestText, MaxTitleLength);
        if (title.Length == 0) return null;

        var hasLinkUri = Uri.TryCreate(bestLink, UriKind.Absolute, out var linkUri);
        var host = hasLinkUri ? linkUri!.Host : string.Empty;
        var pathKey = hasLinkUri
            ? host + "/" + linkUri!.AbsolutePath.Trim('/')
            : bestLink;

        var published = FindDate(member);

        return new Candidate(
            new DetectedArticle(
                title,
                bestLink,
                canonical,
                published,
                FindSummary(member, title)),
            IsPlausibleTitle(bestText, published is not null),
            string.Equals(host, pageUri.Host, StringComparison.OrdinalIgnoreCase),
            host,
            pathKey);
    }

    private static bool IsPlausibleTitle(string text, bool dated = false)
    {
        if (text.Length > MaxTitleLength) return false;

        var trimmed = text.Trim(' ', '.', ',', ':', ';', '→', '»', '>', '-');
        if (ChromeText.Contains(trimmed)) return false;

        var words = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        // A heading of two words with a publication date beside it is an
        // article title; two words on their own is as likely to be a menu.
        if (dated && text.Length >= MinDatedTitleLength && words >= 2) return true;

        return text.Length >= MinTitleLength && words >= 3;
    }

    /// <summary>
    /// A date inside the member, or failing that inside the element that
    /// immediately follows it.
    ///
    /// The sibling fallback is not a guess: it is Hacker News, where a story's
    /// title and a story's age are two separate table rows, and it applies
    /// only when the sibling holds no article link of its own - if it does, it
    /// is another candidate rather than this one's byline, and reading its
    /// date would attach the wrong date to the wrong article.
    /// </summary>
    private static DateTimeOffset? FindDate(HtmlElement member)
    {
        if (DateWithin(member) is { } inside) return inside;

        var next = member.NextSibling;
        if (next is null) return null;
        if (next.Descendants().Any(d => d.Tag == "a"
                                        && IsPlausibleTitle(d.TextContent.Trim()))) return null;

        return DateWithin(next);
    }

    /// <summary>
    /// Three passes, in order of how much the source can be trusted.
    ///
    ///   1. A machine-readable attribute: a &lt;time datetime&gt;, any element
    ///      carrying datetime, or a data attribute naming a date. These were
    ///      written for a machine to read and mean exactly one thing.
    ///   2. An element whose class says it holds a date, read from its title
    ///      attribute first and its text second. This is how Hacker News
    ///      carries a story's age, and how most templates label a byline.
    ///   3. Rendered text in a leaf element that contains a four-digit year.
    ///      Last, because it is the only pass that can be fooled: a year test
    ///      first is what stops "20 minute read" and "Page 1 of 40" being read
    ///      as dates, and a leaf-only rule is what stops the whole card's
    ///      concatenated text being handed to the parser.
    /// </summary>
    private static DateTimeOffset? DateWithin(HtmlElement element)
    {
        var nodes = element.Descendants().Prepend(element).ToList();

        foreach (var node in nodes)
        {
            if (node.Tag == "time"
                && FeedDateParser.TryParse(node.Attribute("datetime") ?? node.TextContent.Trim())
                    is { } fromTime)
                return fromTime;

            foreach (var (name, value) in node.Attributes)
            {
                if (value.Length == 0) continue;
                if (!name.Equals("datetime", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("data-", StringComparison.OrdinalIgnoreCase)) continue;
                if (FeedDateParser.TryParse(value) is { } fromData) return fromData;
            }
        }

        foreach (var node in nodes)
        {
            var classes = node.ClassSignature;
            if (classes.Length == 0) continue;
            if (!DateClassHints.Any(hint => classes.Contains(hint, StringComparison.Ordinal))) continue;

            var labelled = FeedDateParser.TryParse(node.Attribute("title"))
                           ?? FeedDateParser.TryParse(node.TextContent.Trim());
            if (labelled is not null) return labelled;
        }

        foreach (var node in nodes)
        {
            if (node.Children.Count > 0) continue;

            var text = node.OwnText.ToString().Trim();
            if (text.Length is < 6 or > 48) continue;
            if (!YearPattern().IsMatch(text)) continue;
            if (FeedDateParser.TryParse(text) is { } fromText) return fromText;
        }

        return null;
    }

    /// <summary>
    /// The longest leaf text block under the member that is not the title
    /// itself. Plenty of indexes carry a standfirst per entry, and it is the
    /// only body text a scraped item will ever have unless the offline
    /// downloader fetches the article.
    ///
    /// Leaf-only is what keeps this honest: an element with children has the
    /// whole card's text under it, title and tags and byline included, and
    /// returning that would make every summary a jumble of the row it came
    /// from. Null when the page offers nothing.
    /// </summary>
    private static string? FindSummary(HtmlElement member, string title)
    {
        string? best = null;

        foreach (var node in member.Descendants())
        {
            if (node.Tag is not ("p" or "div" or "blockquote" or "span")) continue;
            if (node.Children.Count > 0) continue;

            var text = HtmlElement.CollapseWhitespace(node.OwnText.ToString()).Trim();
            if (text.Length < MinSummaryLength) continue;
            if (text.Contains(title, StringComparison.OrdinalIgnoreCase)) continue;
            if (best is null || text.Length > best.Length) best = text;
        }

        return best is null ? null : Truncate(best, MaxSummaryLength);
    }

    [GeneratedRegex(@"\b(19|20)\d{2}\b")]
    private static partial Regex YearPattern();

    [GeneratedRegex(
        """<script[^>]*type\s*=\s*["']application/ld\+json["'][^>]*>(?<body>.*?)</script>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkedDataPattern();

    [GeneratedRegex(
        """"@type"\s*:\s*"(Article|NewsArticle|BlogPosting|TechArticle|ScholarlyArticle|Report)"""",
        RegexOptions.IgnoreCase)]
    private static partial Regex ArticleTypePattern();

    private static string Truncate(string value, int limit) =>
        value.Length <= limit ? value : value[..limit].TrimEnd();

    /// <summary>
    /// Turns a detection into the same shape a parsed feed produces, so the
    /// refresh path can store scraped articles through exactly the code that
    /// stores real ones: same upsert, same canonical-id dedupe, same
    /// tombstones, same retention, same offline queue.
    ///
    /// The guid is the canonical id rather than the raw link. That is what
    /// makes a scraped article stable across refreshes even when the site
    /// starts appending a tracking parameter to its own links, and it is the
    /// same string the canonical_id column carries, so the two never disagree.
    /// </summary>
    public static ParsedFeed ToParsedFeed(
        this ArticleListDetection detection, string? title, Uri pageUri) =>
        detection.Articles.ToParsedFeed(title, pageUri);

    /// <summary>
    /// The same mapping for articles that did not come from a detection, which
    /// is what a page read through a learned template produces. One mapping so
    /// the two paths cannot drift on what a scraped item's guid is.
    /// </summary>
    public static ParsedFeed ToParsedFeed(
        this IReadOnlyList<DetectedArticle> articles, string? title, Uri pageUri) =>
        new(
            title,
            pageUri.ToString(),
            articles
                .Select(a => new ParsedItem
                {
                    Guid = a.CanonicalId,
                    Link = a.Link,
                    Title = a.Title,
                    PublishedUtc = a.PublishedUtc,
                    Summary = a.Summary,
                    ContentHtml = null
                })
                .ToList(),
            0);
}

/// <summary>
/// A refresh of a scraped page that came back with no articles.
///
/// Distinct from FeedParseException because the two mean different things to
/// the person reading the status line: a feed that failed to parse is a broken
/// feed, whereas this is mylo's own guess about somebody else's markup having
/// stopped working. It is thrown rather than returned as an empty batch so
/// that FeedRefreshService records it as a failure - a scrape that silently
/// stored zero items would look identical to "no new articles" and could stay
/// that way forever.
/// </summary>
public sealed class FeedScrapeException(string message) : Exception(message);
