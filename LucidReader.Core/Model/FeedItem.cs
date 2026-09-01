namespace LucidReader.Core.Model;

public sealed record FeedItem
{
    public long Id { get; init; }
    public long FeedId { get; init; }
    public required string Guid { get; init; }
    public string? Link { get; init; }
    public string? Title { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PublishedUtc { get; init; }
    public DateTimeOffset? UpdatedUtc { get; init; }
    public string? Summary { get; init; }

    /// <summary>
    /// The richest body the feed itself carried, as HTML: content:encoded for
    /// RSS, an Atom content element. Null when the feed offered nothing beyond
    /// <see cref="Summary"/>, which is the common case and is also what every
    /// row written before the V9 migration holds.
    ///
    /// This is what the publisher sent, not what we decided to show. The
    /// reading pane never renders it directly; OfflineDownloader converts it to
    /// markdown into <see cref="ContentMarkdown"/> like any other source.
    /// </summary>
    public string? ContentHtml { get; init; }

    public string? ContentMarkdown { get; init; }
    public ContentSource ContentSource { get; init; }
    public bool IsRead { get; init; }
    public bool IsStarred { get; init; }
    public DateTimeOffset FirstSeenUtc { get; init; }
    public OfflineState OfflineState { get; init; }
    public string? OfflineError { get; init; }
    public string? ImageUrl { get; init; }

    /// <summary>
    /// This article's identity across feeds, from
    /// <see cref="Feeds.CanonicalArticleId.FromLink"/>. Null when the item has
    /// no usable link, which means "stands alone": two nulls are never the
    /// same article.
    /// </summary>
    public string? CanonicalId { get; init; }
}
