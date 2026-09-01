namespace LucidReader.Core.Feeds;

/// <summary>
/// One item as the feed published it. Deliberately separate from FeedItem:
/// this is what a remote server said, not what we have decided to store.
/// Guid is nullable here because plenty of feeds omit it; the storage layer
/// is what fills in a link-hash fallback.
/// </summary>
public sealed record ParsedItem
{
    public string? Guid { get; init; }
    public string? Link { get; init; }
    public string? Title { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PublishedUtc { get; init; }
    public DateTimeOffset? UpdatedUtc { get; init; }
    public string? Summary { get; init; }

    /// <summary>
    /// The richest content the feed offered: content:encoded for RSS, or an
    /// Atom content element, falling back to the description or summary.
    /// Still HTML at this point; conversion to markdown happens later.
    /// </summary>
    public string? ContentHtml { get; init; }

    /// <summary>
    /// The categories the publisher put on the item: RSS &lt;category&gt;
    /// elements, or Atom &lt;category term="..."/&gt;. Already normalised and
    /// de-duplicated by <see cref="LucidReader.Core.Model.TagName"/>, so what
    /// is here is a list of names the tag store will accept as they stand.
    ///
    /// Empty rather than null when the feed offers none, which is most feeds:
    /// a caller iterating this should not have to ask whether the publisher
    /// bothered.
    /// </summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>
    /// The image the feed itself names for this item, or null when it names
    /// none.
    ///
    /// Before this existed, an item's picture could only ever come from
    /// OfflineDownloader reading og:image off the article page, so a
    /// subscription with auto-download off, or one whose articles had not been
    /// fetched yet, showed a grey placeholder in the list and no lead image in
    /// the reading pane. That was true even for publishers who put the image
    /// URL directly in the feed: measured across mylo's own starter feeds, 53
    /// of 93 items carried one (BBC News on every item via media:thumbnail,
    /// Ars Technica on every item via media:content), and all 53 were parsed
    /// and thrown away.
    ///
    /// Absolute and already through <see cref="FeedUrlPolicy"/> by the time it
    /// is set, like every other address that arrives from remote content.
    /// </summary>
    public string? ImageUrl { get; init; }
}
