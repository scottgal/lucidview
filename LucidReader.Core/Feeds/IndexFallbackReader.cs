using AngleSharp.Dom;
using StyloExtract.Heuristics;

namespace LucidReader.Core.Feeds;

/// <summary>
/// The second opinion, asked only about pages <see cref="ArticleListDetector"/>
/// declined.
///
/// <para><b>Why there is one at all.</b> The detector reads a page by finding a
/// repeated run whose members each hold the link to their own article. That
/// covers twenty-four of the twenty-five saved pages and it is the primary path
/// for all of them. lwn.net is the one it cannot read, and not for want of
/// tuning: every headline on that front page is an h2 holding no link, and the
/// article address is only reachable from the blurb underneath, where kernel.org
/// and whatever else the blurb quotes sit closer to the headline than the
/// article does. No rule for borrowing a link from a following sibling picks the
/// right anchor there, which is why the miss was recorded rather than papered
/// over.
///
/// StyloExtract's <see cref="IndexPageExtractor"/> answers it a different way:
/// a heading with no link of its own takes the address its own section states
/// twice on the page's own host, because lwn writes each article's address
/// twice, once as "Full Story" and once as the comment count, while a
/// third-party link quoted in a blurb appears once. A section that never
/// restates an address yields nothing rather than a guess. That gets lwn's ten
/// articles exactly right.</para>
///
/// <para><b>Why it is a fallback and not a vote.</b> Running the library first,
/// or averaging the two, would put twenty-four pages that already work at the
/// mercy of a change in a package. So the detector answers first and this is
/// only consulted when it says no. Nothing about a page the detector accepts can
/// change because of anything in this file.</para>
///
/// <para><b>The gate, and the number behind it.</b> The library's own
/// classification is 24 of the same 25 pages, and its one miss is on the
/// negative side: it reads the forty-citation reference list at the foot of a
/// Wikipedia article as an index, at confidence 0.84. That is the failure that
/// matters here, because a false positive means mylo offers to subscribe the
/// user to a page that is not an index. So deferring to the library's
/// classification is not an option and confidence cannot separate the two
/// either: lwn scores 0.85 and the Wikipedia citation list 0.84, a hundredth
/// apart.
///
/// What does separate them is whose articles the run points at.
/// <see cref="MinimumSameHostShare"/> of the items must be on the page's own
/// host. lwn's ten are 10 of 10; the Wikipedia citations are 0 of 42, because
/// citations point away by definition, as does a run of further reading at the
/// foot of an article. The obvious objection is aggregators, whose whole purpose
/// is to link elsewhere - and the answer is that the detector already accepts
/// every one of them (Hacker News at 0.85, lobste.rs at 0.84, Slashdot at 0.64),
/// so none of them ever reaches this code. Restricting the fallback to pages
/// that index their own host costs nothing measurable on the corpus and removes
/// the library's only false positive on it.
///
/// The publisher's own statement is honoured on top of that: a page carrying
/// og:type=article or a schema.org Article is declined outright, without the
/// higher-bar treatment the detector gives it. A news article's "more stories"
/// rail is a genuine, well-formed, same-host list of articles that no structural
/// rule tells from an index, and on this path there is no second signal left to
/// weigh it against.</para>
///
/// <para>Measured by ScrapedPageAcceptanceCorpusTests over the same twenty-five
/// saved pages, which records which path accepts each one.</para>
/// </summary>
public static class IndexFallbackReader
{
    /// <summary>
    /// The share of the items that have to be on the page's own host.
    ///
    /// Three quarters rather than all, because an index that files a couple of
    /// its entries elsewhere is ordinary - danluu.com lists its Patreon posts
    /// beside the rest - and rather than a half, because a half leaves room for
    /// a run that is as much citation as index. The two pages this actually
    /// decides between sit at 1.00 and 0.00, so the exact figure is not doing
    /// delicate work; it is written down so that a page which lands in the
    /// middle later is declined rather than argued about.
    /// </summary>
    public const double MinimumSameHostShare = 0.75;

    /// <summary>
    /// What the library makes of a page the detector turned down, or null when
    /// it does not clear the gate above, which is the ordinary answer.
    /// </summary>
    /// <param name="document">The page, already parsed. Callers on this path
    /// have one, and parsing a second time to save an argument would double the
    /// cost of the only expensive step here.</param>
    /// <param name="pageUri">The address the page was fetched from, which is
    /// what relative hrefs resolve against and what "its own host" means.</param>
    /// <param name="declined">The detector's answer. Passed rather than
    /// re-derived so the caller cannot forget to ask it first, and so the
    /// publisher's own declaration is read once.</param>
    public static ArticleListDetection? TryRead(
        IDocument document, Uri pageUri, ArticleListDetection declined)
    {
        // Belt and braces on the ordering the whole design rests on. A caller
        // that reached here with an accepted page has a bug, and quietly
        // replacing the detector's answer would hide it.
        if (declined.IsArticleList) return null;

        if (declined.SingleArticleDeclaration is not null) return null;

        var result = new IndexPageExtractor().Detect(document, pageUri);
        if (!result.IsIndex) return null;

        var articles = ScrapedArticles.From(
            result.Items.Select(i => new ScrapedArticles.Raw(i.Title, i.Link, i.Published, i.Summary)),
            pageUri);

        if (articles.Count < ArticleListDetector.MinimumArticles) return null;

        var sameHost = articles.Count(a =>
            Uri.TryCreate(a.Link, UriKind.Absolute, out var uri)
            && string.Equals(uri.Host, pageUri.Host, StringComparison.OrdinalIgnoreCase));

        if (sameHost / (double)articles.Count < MinimumSameHostShare) return null;

        return new ArticleListDetection
        {
            Articles = articles,
            Confidence = result.Confidence,
            IsArticleList = true,
            Reason =
                $"mylo's own reading of this page found no list, but a second pass found " +
                $"{articles.Count} articles on {pageUri.Host} itself " +
                $"(confidence {result.Confidence:0.00}). {result.Reason}"
        };
    }
}
