namespace LucidReader.Core.Feeds;

/// <summary>
/// One suggested subscription, as it is offered to a brand new profile.
/// </summary>
/// <param name="Title">
/// The name shown in the sidebar until the first successful refresh adopts
/// whatever the publisher calls itself.
/// </param>
public sealed record DefaultFeed(string FeedUrl, string Title, string SiteUrl);

/// <summary>
/// The small set of feeds a first run starts with, so the app has something
/// to show instead of three empty panes and a sidebar with nothing under
/// Feeds.
///
/// Kept deliberately short. A starter list is a suggestion, not a curation:
/// five rows are enough to prove the reader works and cheap to unsubscribe
/// from, where twenty would be a chore to clear and would hammer five times
/// as many servers on behalf of someone who never asked for any of them.
///
/// Every address here was fetched and parsed before it was written down, and
/// every one of them still passes through <see cref="FeedUrlPolicy"/> at seed
/// time like any other address the app is given. Nothing about being compiled
/// into the binary exempts a URL from that gate.
/// </summary>
public static class DefaultFeeds
{
    /// <summary>
    /// The maintainer's own site first, then four general-interest and
    /// technology publishers whose feeds are public and meant for aggregators.
    /// mostlylucid.net declares both an RSS and an Atom feed; the RSS one is
    /// used here because it is the one the site lists first and because the
    /// reader's own parser is exercised most heavily against RSS.
    /// </summary>
    public static IReadOnlyList<DefaultFeed> All { get; } =
    [
        new("https://www.mostlylucid.net/rss", "mostlylucid.net", "https://www.mostlylucid.net"),
        new("https://feeds.bbci.co.uk/news/rss.xml", "BBC News", "https://www.bbc.co.uk/news"),
        new("https://feeds.arstechnica.com/arstechnica/index", "Ars Technica", "https://arstechnica.com"),
        new("https://www.theverge.com/rss/index.xml", "The Verge", "https://www.theverge.com"),
        new("https://www.nasa.gov/news-release/feed/", "NASA", "https://www.nasa.gov")
    ];

    /// <summary>
    /// The subset of <see cref="All"/> that <see cref="FeedUrlPolicy"/> is
    /// willing to fetch. Every entry passes today; this exists so that a
    /// future edit to either the list or the policy cannot smuggle an address
    /// past the gate simply because it was hard-coded.
    /// </summary>
    public static IReadOnlyList<DefaultFeed> Allowed() =>
        All.Where(f => FeedUrlPolicy.IsAllowed(f.FeedUrl)).ToList();
}
