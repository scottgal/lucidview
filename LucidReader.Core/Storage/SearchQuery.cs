using LucidReader.Core.Model;

namespace LucidReader.Core.Storage;

/// <summary>
/// One full-text search. Shaped like <see cref="ItemQuery"/> on purpose: the
/// item list has exactly one set of scoping rules (a feed, a folder, or
/// everything, crossed with the All/Unread/Starred filter) and search obeys
/// the same set rather than inventing a second one.
///
/// FeedId and FolderId are both null for an unscoped search, which is the
/// default the toolbar uses.
/// </summary>
public readonly record struct SearchQuery(
    string Text,
    long? FeedId,
    long? FolderId,
    ItemFilter Filter,
    int Limit)
{
    /// <summary>
    /// Narrows the search to articles carrying this tag. The sibling of
    /// <see cref="ItemQuery.TagName"/>, and declared the same way and for the
    /// same reason: as a body property, so no existing call site changes.
    /// </summary>
    public string? TagName { get; init; }
}

/// <summary>
/// One search result: the item, plus the passage of it that matched.
///
/// The snippet comes from FTS5's own snippet() over whichever indexed column
/// scored best, so it is the text that explains the hit rather than the first
/// 180 characters of the body, which may say nothing about the query. The
/// matched terms are delimited with <see cref="MatchStart"/> and
/// <see cref="MatchEnd"/>.
/// </summary>
public sealed record SearchHit(FeedItem Item, string Snippet)
{
    /// <summary>
    /// Control characters, not visible punctuation, and chosen for that
    /// reason: the snippet is prose the user reads, and any printable
    /// delimiter would be indistinguishable from a delimiter that genuinely
    /// occurs in the article. If a display layer ever fails to strip these
    /// they render as nothing rather than as junk.
    /// </summary>
    public const char MatchStart = '\u0001';

    public const char MatchEnd = '\u0002';
}
