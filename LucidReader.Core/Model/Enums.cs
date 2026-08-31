namespace LucidReader.Core.Model;

public enum ContentSource
{
    Feed = 0,
    Extracted = 1
}

/// <summary>
/// Where a subscription's articles come from.
///
/// PublishedFeed is everything that existed before scraping: an address that
/// answers with RSS, Atom or RDF, parsed by FeedParser. ScrapedPage is a
/// address that answers with an ordinary HTML page which
/// <see cref="Feeds.ArticleListDetector"/> read as an index of articles, and
/// which the user then approved.
///
/// The distinction has to be stored rather than re-derived, for two reasons.
/// The refresh path needs it: a scraped feed must be run through the detector
/// on every refresh instead of through the XML parser, and guessing which from
/// the response's content type would make the answer depend on what the server
/// happened to send that minute. And the UI needs it: a scrape is a guess
/// about somebody else's markup and breaks when they change it, which is a
/// materially different promise from a published feed, and the app says so
/// rather than letting the two look identical.
/// </summary>
public enum FeedSourceKind
{
    PublishedFeed = 0,
    ScrapedPage = 1
}

public enum OfflineState
{
    None = 0,
    Pending = 1,
    Downloaded = 2,
    Failed = 3
}
