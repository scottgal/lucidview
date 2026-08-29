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
}
