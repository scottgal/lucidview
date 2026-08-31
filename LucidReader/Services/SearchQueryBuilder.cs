using LucidReader.Core.Storage;
using LucidReader.Models;

namespace LucidReader.Services;

/// <summary>
/// Pure mapping from "what the user typed, what is selected, and whether the
/// scope toggle is on" to the search that should run. The sibling of
/// <see cref="ItemQueryBuilder"/>, and deliberately the same shape, so the
/// two lists the item pane can show obey one set of scoping rules.
///
/// Two decisions are encoded here, both of them Mail's behaviour rather than
/// "search ignores the rest of the UI":
///
/// 1. The All/Unread/Starred segment always applies. The segment is a
///    standing statement about which articles the user is working through,
///    and a search that answered it with read articles while the segment says
///    Unread would be contradicting the pane's own header. A smart row
///    overrides the segment for its own filter, exactly as ItemQueryBuilder
///    does, so searching under the Starred row searches starred articles.
///
/// 2. Feed and folder scoping is offered, not imposed. The default is a
///    search across every feed, because that is the search a reader wants
///    most of the time ("where did I read that?") and because scoping
///    silently to whatever happens to be selected makes an empty result
///    impossible to explain. The toolbar toggle opts into narrowing it, and
///    only a feed or a folder is something to narrow to: with a smart row
///    selected there is no scope, so the toggle is ignored rather than
///    quietly meaning something else.
/// </summary>
public static class SearchQueryBuilder
{
    public static SearchQuery Build(
        string text,
        FeedTreeNode? node,
        ItemFilter currentFilter,
        bool scopeToSelection,
        int limit)
    {
        var filter = node is { Kind: FeedTreeNodeKind.Smart }
            ? node.SmartFilter
            : currentFilter;

        if (!scopeToSelection || !CanScope(node))
            return new SearchQuery(text, null, null, filter, limit);

        return new SearchQuery(
            text,
            node!.Kind == FeedTreeNodeKind.Feed ? node.FeedId : null,
            node.Kind == FeedTreeNodeKind.Folder ? node.FolderId : null,
            filter,
            limit)
        {
            TagName = node.Kind == FeedTreeNodeKind.Tag ? node.TagName : null
        };
    }

    /// <summary>
    /// True when the selection is something a search can be narrowed to. Also
    /// what the toolbar's scope toggle binds its enabled state to, so the
    /// control cannot claim a scope the query builder would ignore.
    ///
    /// A tag is one of those things, for the reason a folder is: it is a
    /// named set of articles the user assembled and can point at. It follows
    /// decision 2 above rather than getting an exception to it - selecting a
    /// tag does not silently narrow the search, the toggle does, and the
    /// toggle then says "This tag".
    /// </summary>
    public static bool CanScope(FeedTreeNode? node) => node?.Kind switch
    {
        FeedTreeNodeKind.Feed => node.FeedId is not null,
        FeedTreeNodeKind.Folder => node.FolderId is not null,
        FeedTreeNodeKind.Tag => !string.IsNullOrEmpty(node.TagName),
        _ => false
    };
}
