namespace LucidReader.Core.Feeds;

/// <summary>
/// One feed discovery is thinking about offering, with whatever evidence
/// about it is to hand. SiteUrl and ItemLinks come from actually parsing the
/// feed and are the strong evidence; DeclaredMediaType is the type attribute
/// the page's link element carried, which says which format it is but nothing
/// about which articles it holds.
/// </summary>
public sealed record FeedAlternateCandidate(
    string FeedUrl,
    string? DeclaredMediaType = null,
    string? SiteUrl = null,
    IReadOnlyList<string>? ItemLinks = null);

/// <summary>What <see cref="FeedAlternates.Classify"/> decided about one feed.</summary>
public sealed record FeedAlternateVerdict(
    string FeedUrl,
    bool IsAlternate,
    string? AlternateOfUrl);

/// <summary>
/// Works out which of the feeds a site offers are alternate formats of one
/// another, so the add-feed dialog can pre-tick one instead of all of them.
///
/// This is the fix for the duplicate the reader could not previously avoid.
/// mostlylucid.net publishes an RSS feed and an Atom feed; compared directly
/// they carry twenty item links each with an overlap of twenty. They are the
/// same twenty articles. Subscribing to both, which was the dialog's default,
/// stored every article twice under two feed ids, and no amount of
/// (feed_id, guid) care downstream can undo that, because the two documents
/// give the same article different identifiers on purpose.
///
/// Two kinds of evidence, in order of strength:
///
///   1. The feeds' own contents. If two candidates were fetched and parsed,
///      their item links are compared after the same normalisation the item
///      dedupe uses (CanonicalArticleId), and a large overlap is taken as
///      proof. This is the test that actually fires for a site like
///      mostlylucid.net, and it is strong enough to say yes about two feeds
///      at unrelated addresses and, just as importantly, no about two feeds
///      on one host that carry genuinely different sections.
///   2. Failing that, their declared site link, and failing that, the shape
///      of their addresses: same host, and both paths ending in a
///      conventional feed name. That is a heuristic and is only reached when
///      the feeds could not be read, so it deliberately errs towards leaving
///      a feed ticked rather than grouping two unrelated ones.
///
/// The survivor of a group is the Atom feed where there is one. Atom requires
/// every entry to carry a globally unique &lt;id&gt; and an &lt;updated&gt;
/// timestamp; RSS makes both &lt;guid&gt; and &lt;pubDate&gt; optional, and
/// real feeds omit them (Hacker News publishes thirty items and no guid at
/// all). Since stable item identity is what dedupe runs on and a change
/// timestamp is what tells an edit from a repeat, the format that mandates
/// both is the one worth subscribing to.
/// </summary>
public static class FeedAlternates
{
    /// <summary>
    /// How much of the smaller feed's item set has to appear in the larger
    /// one before the two count as the same source. Well below 1.0 on
    /// purpose: two formats of one feed routinely publish different window
    /// sizes (twenty items in one, ten in the other) and a publisher can be
    /// mid-update when the two are fetched a second apart.
    /// </summary>
    private const double OverlapThreshold = 0.6;

    /// <summary>
    /// Final path segments that read as "this is the site's feed". Same set
    /// FeedAutodiscovery probes for, kept here rather than shared with it
    /// because the two lists answer different questions and should be free to
    /// diverge: that one decides what to fetch, this one decides what looks
    /// like a sibling of what.
    /// </summary>
    private static readonly string[] FeedPathNames =
    [
        "rss", "rss.xml", "atom", "atom.xml", "feed", "feed.xml",
        "index.xml", "rss.rss", "feed.atom"
    ];

    public static IReadOnlyList<FeedAlternateVerdict> Classify(
        IReadOnlyList<FeedAlternateCandidate> candidates)
    {
        if (candidates.Count < 2)
        {
            return candidates
                .Select(c => new FeedAlternateVerdict(c.FeedUrl, false, null))
                .ToList();
        }

        var groups = BuildGroups(candidates);
        var verdicts = new List<FeedAlternateVerdict>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var group = groups[candidate.FeedUrl];
            var keeper = PreferredOf(group);

            verdicts.Add(candidate.FeedUrl == keeper.FeedUrl
                ? new FeedAlternateVerdict(candidate.FeedUrl, false, null)
                : new FeedAlternateVerdict(candidate.FeedUrl, true, keeper.FeedUrl));
        }

        return verdicts;
    }

    /// <summary>
    /// Transitive grouping: A and C end up together when both match B, which
    /// is the right answer for a site offering three formats of one feed.
    /// Written as a plain merge over a list of groups rather than a
    /// disjoint-set structure because the candidate list here is at most a
    /// handful of feeds.
    /// </summary>
    private static Dictionary<string, List<FeedAlternateCandidate>> BuildGroups(
        IReadOnlyList<FeedAlternateCandidate> candidates)
    {
        var groups = new List<List<FeedAlternateCandidate>>();

        foreach (var candidate in candidates)
        {
            var matched = groups.Where(g => g.Any(other => SameSource(candidate, other))).ToList();

            if (matched.Count == 0)
            {
                groups.Add([candidate]);
                continue;
            }

            var merged = matched[0];
            merged.Add(candidate);

            foreach (var extra in matched.Skip(1))
            {
                merged.AddRange(extra);
                groups.Remove(extra);
            }
        }

        var byUrl = new Dictionary<string, List<FeedAlternateCandidate>>(StringComparer.Ordinal);
        foreach (var group in groups)
            foreach (var member in group)
                byUrl[member.FeedUrl] = group;

        return byUrl;
    }

    private static bool SameSource(FeedAlternateCandidate a, FeedAlternateCandidate b)
    {
        if (string.Equals(a.FeedUrl, b.FeedUrl, StringComparison.OrdinalIgnoreCase)) return true;

        var aLinks = CanonicalSet(a.ItemLinks);
        var bLinks = CanonicalSet(b.ItemLinks);

        // Strong evidence, and conclusive in both directions: when both feeds
        // were read, what they contain settles it and no weaker test gets a
        // say. Two feeds on one host carrying different sections land here and
        // are correctly left apart.
        if (aLinks.Count > 0 && bLinks.Count > 0)
        {
            var overlap = aLinks.Count(bLinks.Contains);
            return overlap / (double)Math.Min(aLinks.Count, bLinks.Count) >= OverlapThreshold;
        }

        // Next best: both name the same site. A feed's channel link is the
        // page it is a feed OF, so two feeds claiming one page are two views
        // of it.
        var aSite = CanonicalArticleId.FromLink(a.SiteUrl);
        var bSite = CanonicalArticleId.FromLink(b.SiteUrl);
        if (aSite is not null && bSite is not null)
            return string.Equals(aSite, bSite, StringComparison.Ordinal);

        return SameHostAndFeedShaped(a.FeedUrl, b.FeedUrl);
    }

    /// <summary>
    /// The last-resort heuristic: one host, and two addresses that both look
    /// like the site's own feed rather than a feed for one section of it.
    /// "/rss" and "/atom" pair up; "/blog/dotnet/rss" and "/blog/go/rss" do
    /// not, because their paths differ by more than the final segment.
    /// </summary>
    private static bool SameHostAndFeedShaped(string first, string second)
    {
        if (!Uri.TryCreate(first, UriKind.Absolute, out var a)) return false;
        if (!Uri.TryCreate(second, UriKind.Absolute, out var b)) return false;
        if (!a.Host.Equals(b.Host, StringComparison.OrdinalIgnoreCase)) return false;

        var (aParent, aLeaf) = SplitPath(a.AbsolutePath);
        var (bParent, bLeaf) = SplitPath(b.AbsolutePath);

        if (!string.Equals(aParent, bParent, StringComparison.OrdinalIgnoreCase)) return false;

        return FeedPathNames.Contains(aLeaf, StringComparer.OrdinalIgnoreCase)
               && FeedPathNames.Contains(bLeaf, StringComparer.OrdinalIgnoreCase);
    }

    private static (string Parent, string Leaf) SplitPath(string absolutePath)
    {
        var path = absolutePath.TrimEnd('/');
        var lastSlash = path.LastIndexOf('/');
        return lastSlash < 0 ? (string.Empty, path) : (path[..lastSlash], path[(lastSlash + 1)..]);
    }

    private static HashSet<string> CanonicalSet(IReadOnlyList<string>? links)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (links is null) return set;

        foreach (var link in links)
            if (CanonicalArticleId.FromLink(link) is { } canonical)
                set.Add(canonical);

        return set;
    }

    /// <summary>
    /// Atom first, then RSS, then anything unidentified; ties broken by the
    /// shorter address and then alphabetically, purely so the same input
    /// always produces the same answer.
    /// </summary>
    private static FeedAlternateCandidate PreferredOf(IEnumerable<FeedAlternateCandidate> group) =>
        group
            .OrderBy(FormatRank)
            .ThenBy(c => c.FeedUrl.Length)
            .ThenBy(c => c.FeedUrl, StringComparer.Ordinal)
            .First();

    private static int FormatRank(FeedAlternateCandidate candidate)
    {
        if (Mentions(candidate, "atom")) return 0;
        if (Mentions(candidate, "rss")) return 1;
        return 2;
    }

    /// <summary>
    /// The declared media type when the page gave one, otherwise the address
    /// itself. "/atom.xml" with no type attribute is still recognisably an
    /// Atom feed, and the alternative is calling it unidentified and picking
    /// on address length alone.
    /// </summary>
    private static bool Mentions(FeedAlternateCandidate candidate, string token) =>
        (candidate.DeclaredMediaType?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false)
        || LeafOf(candidate.FeedUrl).Contains(token, StringComparison.OrdinalIgnoreCase);

    private static string LeafOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? SplitPath(uri.AbsolutePath).Leaf
            : string.Empty;
}
