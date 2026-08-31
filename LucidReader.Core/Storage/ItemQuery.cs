namespace LucidReader.Core.Storage;

public enum ItemFilter
{
    All = 0,
    Unread = 1,
    Starred = 2
}

/// <summary>
/// A single item-list query. FeedId and FolderId are both optional: null for
/// both means "across every feed", which is what the All items, Unread and
/// Starred smart rows use.
/// </summary>
public readonly record struct ItemQuery(
    long? FeedId,
    long? FolderId,
    ItemFilter Filter,
    int Limit,
    int Offset)
{
    /// <summary>
    /// Narrows the query to articles carrying this tag, matched
    /// case-insensitively. Null for every query that is not a tag view.
    ///
    /// Declared in the body rather than as a sixth positional parameter so
    /// every existing call site keeps compiling and keeps meaning what it
    /// meant. It also combines with the rest rather than replacing it: the
    /// All/Unread/Starred filter still applies inside a tag view, which is
    /// the whole point of the tag being one more scope rather than a separate
    /// kind of list.
    /// </summary>
    public string? TagName { get; init; }
}
