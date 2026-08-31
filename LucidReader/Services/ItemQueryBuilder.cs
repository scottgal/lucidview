using LucidReader.Core.Storage;
using LucidReader.Models;

namespace LucidReader.Services;

/// <summary>
/// Pure mapping from "whatever is selected in the feed tree" to the query
/// that should run. A smart row queries across every feed and overrides the
/// filter chips; a folder, a feed or a tag scopes the query and keeps the
/// chosen filter.
///
/// Extracted out of MainWindow.BuildQuery so the decision logic can be unit
/// tested directly for each selection shape without constructing a Window.
/// </summary>
public static class ItemQueryBuilder
{
    public static ItemQuery Build(FeedTreeNode? node, ItemFilter currentFilter)
    {
        if (node is null || node.Kind == FeedTreeNodeKind.Smart)
            return new ItemQuery(null, null, node?.SmartFilter ?? currentFilter, 500, 0);

        // A tag is a scope like a feed or a folder, not a smart row: it keeps
        // the All/Unread/Starred segment rather than overriding it, so the
        // segment still narrows what a tag view shows. It carries no feed or
        // folder id, so a tag spans every feed the tagged articles came from.
        if (node.Kind == FeedTreeNodeKind.Tag)
            return new ItemQuery(null, null, currentFilter, 500, 0) { TagName = node.TagName };

        return new ItemQuery(
            node.Kind == FeedTreeNodeKind.Feed ? node.FeedId : null,
            node.Kind == FeedTreeNodeKind.Folder ? node.FolderId : null,
            currentFilter,
            500,
            0);
    }
}
