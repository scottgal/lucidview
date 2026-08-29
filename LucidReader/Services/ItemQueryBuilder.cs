using LucidReader.Core.Storage;
using LucidReader.Models;

namespace LucidReader.Services;

/// <summary>
/// Pure mapping from "whatever is selected in the feed tree" to the query
/// that should run. A smart row queries across every feed and overrides the
/// filter chips; a folder or a feed scopes the query and keeps the chosen
/// filter.
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

        return new ItemQuery(
            node.Kind == FeedTreeNodeKind.Feed ? node.FeedId : null,
            node.Kind == FeedTreeNodeKind.Folder ? node.FolderId : null,
            currentFilter,
            500,
            0);
    }
}
