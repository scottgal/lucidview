namespace LucidReader.Core.Model;

/// <summary>
/// Where the markdown stored for an article was derived from.
///
/// The split between <see cref="Feed"/> and <see cref="FeedArticle"/> is the
/// point of this enum. Both came out of the feed document, and until V9 both
/// were recorded as <see cref="Feed"/>, which is why the reading pane used to
/// say "showing the summary the feed provided" over articles it was in fact
/// showing in full: "arrived in the feed" and "is only a teaser" are different
/// facts, and a feed carrying a whole post in content:encoded is the proof.
/// </summary>
public enum ContentSource
{
    /// <summary>
    /// A teaser. The feed gave nothing longer than a summary and either no
    /// page could be fetched or fetching was turned off, so what is stored is
    /// all there is. This is the one case the reading pane badges.
    /// </summary>
    Feed = 0,

    /// <summary>
    /// The whole article, read from the page it lives on.
    /// </summary>
    Extracted = 1,

    /// <summary>
    /// The whole article, taken from the feed document itself - the body a
    /// publisher put in content:encoded or an Atom content element - and
    /// converted by exactly the same markdown pipeline as
    /// <see cref="Extracted"/>. Complete, so the reader needs no page fetch and
    /// no badge; it is not the same thing as <see cref="Feed"/> and must not be
    /// treated as one.
    /// </summary>
    FeedArticle = 2
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
