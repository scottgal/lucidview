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
    int Offset);
