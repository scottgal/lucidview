using AngleSharp;
using AngleSharp.Dom;
using StyloExtract.Abstractions;
using StyloExtract.Heuristics;

namespace LucidReader.Core.Feeds;

/// <summary>
/// How a scraped page's articles were read this time.
/// </summary>
public enum ScrapeSource
{
    /// <summary>The detector ran, as it does on the first refresh of a feed
    /// and whenever a stored template stopped working.</summary>
    Detector = 0,

    /// <summary>A template stored on an earlier refresh was applied.</summary>
    Template,

    /// <summary>The detector declined the page and
    /// <see cref="IndexFallbackReader"/> read it instead. Recorded separately
    /// so a feed that only exists because of the fallback is visible as such
    /// rather than indistinguishable from one the detector handles.</summary>
    Fallback,
}

/// <summary>
/// What one scrape produced, and how.
/// </summary>
public sealed record ScrapeReading(
    IReadOnlyList<DetectedArticle> Articles,
    ScrapeSource Source,
    string Reason);

/// <summary>
/// Reads the articles off a scraped index page, reusing a template learned on
/// an earlier refresh when there is one that still works.
///
/// <para><b>The detector answers first.</b> Nothing here decides whether a page
/// is a list of articles. <see cref="ArticleListDetector"/> does, it is measured
/// against twenty-five saved pages, and it is asked before anything else on
/// every read. A template is a cache of one of its answers, not a second
/// opinion.</para>
///
/// <para><b>And only what it declines goes further.</b>
/// <see cref="IndexFallbackReader"/> is asked about a page the detector turned
/// down, which is how lwn.net is readable at all. It cannot change the answer
/// for a page the detector accepts, because it is never consulted about one.
/// A reading that came from it is marked <see cref="ScrapeSource.Fallback"/>
/// rather than passed off as the detector's.</para>
///
/// <para><b>Why cache it at all.</b> A scraped feed is polled on a schedule
/// forever, and the detector re-derives the page's structure from nothing on
/// every poll, scoring every repeated run to reach the answer it reached last
/// time. A template records that answer, so what a poll extracts is anchored to
/// a shape somebody once agreed with rather than re-argued each time. That is
/// not a speed argument and is not offered as one: the template path parses the
/// page with a real DOM and fingerprints it, which costs more than the
/// detector's own pass over the raw HTML. What it buys is that a page whose
/// best two runs score within a hundredth of each other cannot quietly return a
/// different list on Tuesday than it did on Monday.</para>
///
/// <para><b>What happens when a template goes stale.</b> It falls back to the
/// detector and learns the page again. This is the case the whole design turns
/// on, because the failure it prevents is silent: a template that half matches
/// a redesigned page returns four entries where there were thirty, and to a
/// reader that is indistinguishable from the site having gone quiet. So a
/// template's output is checked against the number of entries it found when it
/// was learned, and anything materially short is thrown away rather than
/// returned. A scrape that ends up with nothing at all still throws
/// <see cref="FeedScrapeException"/>, exactly as it did before templates
/// existed, so it is recorded as a failure rather than as a refresh with no
/// news.</para>
/// </summary>
public static class ScrapedPageReader
{
    /// <summary>
    /// The share of the entries a template found when it was learned that it
    /// has to find again before its answer is used.
    ///
    /// Not 1.0, because an index page legitimately shrinks: a site that lists
    /// this month's posts has fewer in the first week of a month than in the
    /// last week of the one before. Not 0.1 either, because the point of the
    /// check is to notice a template that is matching a fragment of what it
    /// used to. Two thirds is comfortably below any real month boundary and
    /// comfortably above a template that has come loose.
    /// </summary>
    public const double MinimumTemplateYield = 0.67;

    /// <summary>
    /// Read the page, then leave behind a template that will read it again.
    ///
    /// <paramref name="store"/> may be null, which is what a caller that has no
    /// profile directory to write to looks like. Everything still works; the
    /// detector simply runs every time.
    /// </summary>
    public static async Task<ScrapeReading> ReadAsync(
        string html,
        Uri pageUri,
        ScrapeTemplateStore? store,
        CancellationToken ct = default)
    {
        if (store is null)
        {
            // No profile directory to write a template to, so the page is read
            // from scratch every time. The detector still answers first and the
            // fallback still only sees what it declined; the document is parsed
            // lazily inside ReadPageAsync, so the ordinary case where the detector
            // succeeds pays nothing for the fallback existing.
            return await ReadPageAsync(html, pageUri, null, ct);
        }

        var document = await ParseAsync(html, pageUri, ct);

        var learned = await store.FindAsync(pageUri, document, ct);
        if (learned is not null)
        {
            var expected = ExpectedItemCount(learned);
            var records = new RecordApplicator().Apply(document, learned);
            var articles = ToArticles(records, pageUri);

            if (articles.Count >= Math.Max(ArticleListDetector.MinimumArticles,
                                           (int)Math.Ceiling(expected * MinimumTemplateYield)))
            {
                return new ScrapeReading(
                    articles, ScrapeSource.Template,
                    $"Read {articles.Count} articles with the template learned for this page.");
            }
        }

        // No template, or one that no longer holds. Ask the detector and, if it
        // is confident, leave a template behind for next time.
        var reading = await ReadPageAsync(html, pageUri, document, ct);

        var examples = reading.Articles
            .Select(a => new RecordExample(a.Title, a.Link, a.PublishedUtc?.ToString("O"), a.Summary))
            .ToList();

        var induced = new RecordTemplateInducer().InduceFromExamples(Guid.NewGuid(), document, examples);
        if (induced is not null && KeepsWhatTheDetectorFound(induced, reading.Articles))
        {
            await store.StoreAsync(pageUri, document, induced, ct);
        }

        return reading;
    }

    /// <summary>
    /// Whether a template is worth keeping, which means it does not throw away
    /// something the detector was getting.
    ///
    /// A date is the case this exists for. The detector reads one out of a
    /// machine-readable attribute, out of an element whose class says it holds
    /// a date, or out of rendered text, in that order. A field rule can only
    /// promise the first, so on a site that writes its dates as plain text the
    /// template would return the same articles undated, and undated articles
    /// sort to the bottom of a list the user reads by date. Caching an answer
    /// that is worse than the answer is not a cache.
    ///
    /// The same test covers summaries. Both are checked against what the
    /// detector actually produced on this page rather than against a fixed
    /// expectation, because plenty of index pages carry neither and there is
    /// nothing to lose on those.
    /// </summary>
    private static bool KeepsWhatTheDetectorFound(
        LearnedExtractor induced, IReadOnlyList<DetectedArticle> found)
    {
        var fields = induced.Rules
            .Where(r => r.Fields is not null)
            .SelectMany(r => r.Fields!)
            .Select(f => f.Field)
            .ToHashSet();

        // A record with no title is dropped on the way out, so a template
        // without a title rule would return an empty list on every refresh and
        // fall back every time. Rejecting it here costs one induction instead
        // of one wasted apply per poll.
        if (!fields.Contains(RecordField.Title)) return false;

        var half = found.Count / 2.0;

        if (found.Count(a => a.PublishedUtc is not null) > half
            && !fields.Contains(RecordField.Published)) return false;

        if (found.Count(a => !string.IsNullOrWhiteSpace(a.Summary)) > half
            && !fields.Contains(RecordField.Summary)) return false;

        return true;
    }

    /// <summary>
    /// Parse with the page's own address attached, so relative hrefs resolve
    /// the way they do in a browser. Nothing is fetched: AngleSharp's default
    /// configuration carries no requester, so a document full of script and
    /// image references loads exactly as much of the network as the detector
    /// does, which is none of it.
    /// </summary>
    private static Task<IDocument> ParseAsync(string html, Uri pageUri, CancellationToken ct) =>
        BrowsingContext.New(Configuration.Default)
            .OpenAsync(request => request.Content(html).Address(pageUri.ToString()), ct);

    /// <summary>
    /// Run the detector, ask <see cref="IndexFallbackReader"/> about what it
    /// declined, and throw when neither can read the page. The message is the
    /// one the user sees on the feed row.
    ///
    /// <para>The order is the guarantee, not an optimisation. A page the
    /// detector accepts is returned before the fallback is constructed, so no
    /// subscription that works today can start answering differently because of
    /// what the library thinks.</para>
    ///
    /// <para><paramref name="parsed"/> is the document the template path already
    /// built, or null when there was no store and nothing has parsed the page.
    /// In the second case parsing is deferred until the detector has actually
    /// declined, so a refresh of a feed the detector handles never parses a DOM
    /// it does not need.</para>
    /// </summary>
    private static async Task<ScrapeReading> ReadPageAsync(
        string html, Uri pageUri, IDocument? parsed, CancellationToken ct)
    {
        var detection = ArticleListDetector.Detect(html, pageUri);

        if (detection.IsArticleList && detection.Articles.Count > 0)
        {
            return new ScrapeReading(
                detection.Articles, ScrapeSource.Detector,
                $"Found {detection.Articles.Count} repeated article links " +
                $"(confidence {detection.Confidence:0.00}).");
        }

        var document = parsed ?? await ParseAsync(html, pageUri, ct);
        var fallback = IndexFallbackReader.TryRead(document, pageUri, detection);

        if (fallback is not null && fallback.Articles.Count > 0)
        {
            return new ScrapeReading(
                fallback.Articles, ScrapeSource.Fallback, fallback.Reason);
        }

        throw new FeedScrapeException(
            "This is a scraped page, and it no longer looks like a list of articles. " +
            "The site has probably changed its layout. " + detection.Reason);
    }

    /// <summary>
    /// How many entries the template matched when it was learned. Stored on the
    /// centroid because that is where the index already keeps per-role counts,
    /// so nothing extra has to be persisted for it.
    /// </summary>
    private static int ExpectedItemCount(LearnedExtractor extractor) =>
        extractor.Centroid.ByRole.TryGetValue(BlockRole.RepeatedItem, out var centroid)
            ? centroid.ObservationCount
            : 0;

    /// <summary>
    /// Records into the shape the rest of mylo already stores.
    ///
    /// Every address is resolved against the page and put through
    /// <see cref="FeedUrlPolicy"/>, for the same reason the detector does it:
    /// these links came out of somebody else's document and are stored, shown,
    /// and later fetched by the offline downloader. A template is a cached
    /// answer, not a licence to skip the gate.
    /// </summary>
    private static IReadOnlyList<DetectedArticle> ToArticles(
        IReadOnlyList<ExtractedRecord> records, Uri pageUri) =>
        ScrapedArticles.From(
            records.Select(r => new ScrapedArticles.Raw(
                r.Title, r.Permalink, r.Published, r.Summary)),
            pageUri);
}
