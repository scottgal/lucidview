namespace LucidReader.Core.Feeds;

/// <summary>
/// Turns the raw title/href/date/summary quadruples that come off a scraped
/// page into the <see cref="DetectedArticle"/> shape the rest of mylo stores.
///
/// Written once and shared by every path that reads a scraped page, because
/// the gate in the middle of it is not optional. Every address here came out
/// of somebody else's document and is stored, shown in a list the user clicks,
/// and later fetched unattended by the offline downloader, so it is resolved
/// against the page it was read from and put through
/// <see cref="FeedUrlPolicy"/> before it can become an article. That was true
/// of the detector's own path and of the template path; a third path that
/// skipped it would be the hole the other two were written to avoid.
/// </summary>
internal static class ScrapedArticles
{
    /// <summary>
    /// One entry as read off the page. Title and link may be blank or absent,
    /// which is what a run member that turned out not to be an entry looks
    /// like; those are dropped rather than repaired.
    /// </summary>
    internal readonly record struct Raw(
        string? Title, string? Link, string? Published, string? Summary);

    internal static IReadOnlyList<DetectedArticle> From(IEnumerable<Raw> raw, Uri pageUri)
    {
        var articles = new List<DetectedArticle>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pageIdentity = CanonicalArticleId.FromLink(pageUri.ToString());

        foreach (var entry in raw)
        {
            if (string.IsNullOrWhiteSpace(entry.Link)) continue;
            if (string.IsNullOrWhiteSpace(entry.Title)) continue;
            if (!Uri.TryCreate(pageUri, entry.Link, out var absolute)) continue;
            if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps) continue;
            if (!FeedUrlPolicy.IsAllowed(absolute.ToString())) continue;

            var canonical = CanonicalArticleId.FromLink(absolute.ToString());
            if (canonical is null) continue;

            // A link back to the page being read is the "you are here" entry,
            // not one of the things the page lists.
            if (pageIdentity is not null && canonical == pageIdentity) continue;
            if (!seen.Add(canonical)) continue;

            articles.Add(new DetectedArticle(
                entry.Title.Trim(),
                absolute.ToString(),
                canonical,
                FeedDateParser.TryParse(entry.Published ?? string.Empty),
                string.IsNullOrWhiteSpace(entry.Summary) ? null : entry.Summary));

            if (articles.Count >= ArticleListDetector.MaxArticles) break;
        }

        return articles;
    }
}
