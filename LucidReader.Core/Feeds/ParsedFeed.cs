namespace LucidReader.Core.Feeds;

/// <summary>
/// SkippedItemCount records items the parser could not read at all. Partial
/// success is the normal case with real feeds: eighteen good items out of
/// twenty is a successful fetch, not a failure, but we surface the two.
/// </summary>
/// <param name="IconUrl">
/// The icon the feed names for itself: an RSS channel's &lt;image&gt;&lt;url&gt;,
/// or an Atom feed's &lt;icon&gt; or &lt;logo&gt;. Null when the feed names none,
/// which is most feeds. It is the cheapest icon there is - it arrives inside a
/// document the refresh has already fetched and parsed - which is why
/// <see cref="FeedIconResolver"/> tries it before anything that costs a
/// request. Optional so callers that synthesise a ParsedFeed from something
/// other than a feed document (a scraped page) need say nothing about it.
/// </param>
public sealed record ParsedFeed(
    string? Title,
    string? SiteUrl,
    IReadOnlyList<ParsedItem> Items,
    int SkippedItemCount,
    string? IconUrl = null);
