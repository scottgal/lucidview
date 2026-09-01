namespace LucidReader.Core.Feeds;

/// <summary>
/// One feed in the shipped catalogue.
/// </summary>
/// <param name="Category">
/// The heading it is listed under. A plain string rather than an enum because
/// its only job is to group rows in a list and be read by a person.
/// </param>
public sealed record CatalogFeed(string Title, string FeedUrl, string SiteUrl, string Category);

/// <summary>
/// A small curated list of well-known feeds, so a new reader has somewhere to
/// start that is not an empty window or a text box demanding a URL they do not
/// have to hand.
///
/// Shipped as data in the binary, and fetched from nowhere. The obvious source
/// - rss.com's "popular RSS feeds" page - was read once, by hand, while this
/// was written, and it is credited in <see cref="SourceCredit"/>. It is not
/// read at runtime and must not be: scraping a third party's page to populate a
/// dialog makes the dialog depend on their markup, their uptime and their
/// willingness to be scraped, and puts a request on a path the user has not
/// asked to make. What that page yields is also thin - a handful of extractable
/// addresses, several of them a dead twitter.com/rss, rss.com's own comments
/// feed, or a domain not worth putting in front of somebody on their first day.
/// So the usable ones seeded this list and the rest of it is chosen.
///
/// Deliberately modest at a few dozen entries. This is a starting point, not a
/// directory: a list long enough to need its own search is a list nobody reads,
/// and every row here is a server that will be polled on a schedule as soon as
/// somebody ticks it.
///
/// Every address was fetched and confirmed to parse as RSS, RDF or Atom before
/// it was written down, and every one still goes through
/// <see cref="FeedUrlPolicy"/> at the moment it is offered (see
/// <see cref="Allowed"/>). Being compiled in exempts a URL from nothing, which
/// is the same rule <see cref="DefaultFeeds"/> follows.
/// </summary>
public static class FeedCatalog
{
    /// <summary>
    /// Shown in the dialog. The list was seeded from this page and it says so
    /// rather than presenting somebody else's collection as mylo's own.
    /// </summary>
    public const string SourceCredit =
        "Seeded from rss.com's list of popular RSS feeds (rss.com/blog/popular-rss-feeds), then curated.";

    public const string Technology = "Technology";
    public const string News = "News";
    public const string Science = "Science";
    public const string Culture = "Culture";
    public const string Development = "Development";

    /// <summary>
    /// The order categories appear in. Alphabetical order would put Culture
    /// first, which is not what somebody opening a feed reader is most likely
    /// to be after; this runs from the broadest interest to the most specialist.
    /// </summary>
    public static IReadOnlyList<string> Categories { get; } =
        [News, Technology, Science, Culture, Development];

    public static IReadOnlyList<CatalogFeed> All { get; } =
    [
        new("BBC News", "https://feeds.bbci.co.uk/news/rss.xml", "https://www.bbc.co.uk/news", News),
        new("NPR News", "https://feeds.npr.org/1001/rss.xml", "https://www.npr.org", News),
        new("The Guardian: World", "https://www.theguardian.com/world/rss", "https://www.theguardian.com", News),
        new("The New York Times", "https://rss.nytimes.com/services/xml/rss/nyt/HomePage.xml", "https://www.nytimes.com", News),
        new("Al Jazeera", "https://www.aljazeera.com/xml/rss/all.xml", "https://www.aljazeera.com", News),
        new("CBS News", "https://www.cbsnews.com/latest/rss/main", "https://www.cbsnews.com", News),

        new("Ars Technica", "https://feeds.arstechnica.com/arstechnica/index", "https://arstechnica.com", Technology),
        new("The Verge", "https://www.theverge.com/rss/index.xml", "https://www.theverge.com", Technology),
        new("Hacker News", "https://news.ycombinator.com/rss", "https://news.ycombinator.com", Technology),
        new("TechCrunch", "https://techcrunch.com/feed/", "https://techcrunch.com", Technology),
        new("Wired", "https://www.wired.com/feed/rss", "https://www.wired.com", Technology),
        new("Engadget", "https://www.engadget.com/rss.xml", "https://www.engadget.com", Technology),
        new("MIT Technology Review", "https://www.technologyreview.com/feed/", "https://www.technologyreview.com", Technology),
        new("Slashdot", "https://rss.slashdot.org/Slashdot/slashdotMain", "https://slashdot.org", Technology),

        new("NASA", "https://www.nasa.gov/news-release/feed/", "https://www.nasa.gov", Science),
        new("ScienceDaily", "https://www.sciencedaily.com/rss/all.xml", "https://www.sciencedaily.com", Science),
        new("Phys.org", "https://phys.org/rss-feed/", "https://phys.org", Science),
        new("Quanta Magazine", "https://api.quantamagazine.org/feed/", "https://www.quantamagazine.org", Science),
        new("Nature", "https://www.nature.com/nature.rss", "https://www.nature.com", Science),
        new("Scientific American", "https://www.scientificamerican.com/platform/syndication/rss/", "https://www.scientificamerican.com", Science),

        new("The Atlantic", "https://www.theatlantic.com/feed/all/", "https://www.theatlantic.com", Culture),
        new("Aeon", "https://aeon.co/feed.rss", "https://aeon.co", Culture),
        new("The Paris Review", "https://www.theparisreview.org/blog/feed/", "https://www.theparisreview.org", Culture),
        new("Longreads", "https://longreads.com/feed/", "https://longreads.com", Culture),
        new("kottke.org", "https://feeds.kottke.org/main", "https://kottke.org", Culture),
        new("Open Culture", "https://www.openculture.com/feed", "https://www.openculture.com", Culture),

        new("mostlylucid.net", "https://www.mostlylucid.net/rss", "https://www.mostlylucid.net", Development),
        new(".NET Blog", "https://devblogs.microsoft.com/dotnet/feed/", "https://devblogs.microsoft.com/dotnet", Development),
        new("The GitHub Blog", "https://github.blog/feed/", "https://github.blog", Development),
        new("Martin Fowler", "https://martinfowler.com/feed.atom", "https://martinfowler.com", Development),
        new("Stack Overflow Blog", "https://stackoverflow.blog/feed/", "https://stackoverflow.blog", Development),
        new("Julia Evans", "https://jvns.ca/atom.xml", "https://jvns.ca", Development),
        new("Simon Willison", "https://simonwillison.net/atom/everything/", "https://simonwillison.net", Development),
        new("CSS-Tricks", "https://css-tricks.com/feed/", "https://css-tricks.com", Development)
    ];

    /// <summary>
    /// The subset of <see cref="All"/> that <see cref="FeedUrlPolicy"/> is
    /// willing to fetch, in category order and alphabetically within a
    /// category, which is the order the dialog shows them in.
    ///
    /// Every entry passes today. This exists for the same reason
    /// DefaultFeeds.Allowed does: a later edit to the list, or to the policy,
    /// must not be able to put an address in front of the user simply because
    /// it was hard-coded.
    /// </summary>
    public static IReadOnlyList<CatalogFeed> Allowed() =>
        All.Where(feed => FeedUrlPolicy.IsAllowed(feed.FeedUrl))
            .OrderBy(feed => IndexOfCategory(feed.Category))
            .ThenBy(feed => feed.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// A category not in <see cref="Categories"/> sorts last rather than
    /// throwing or vanishing: a new heading added to the list above and
    /// forgotten here should look out of place, not disappear.
    /// </summary>
    private static int IndexOfCategory(string category)
    {
        var index = Categories.ToList().IndexOf(category);
        return index < 0 ? int.MaxValue : index;
    }
}
