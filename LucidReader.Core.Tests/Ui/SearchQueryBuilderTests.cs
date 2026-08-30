using LucidReader.Core.Storage;
using LucidReader.Models;
using LucidReader.Services;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// How search relates to the rest of the pane: the filter segment always
/// applies, and feed or folder scoping is offered rather than imposed. The
/// reasoning for both is on SearchQueryBuilder itself.
/// </summary>
public class SearchQueryBuilderTests
{
    private static FeedTreeNode Feed(long id, long? folderId = null) =>
        new() { Title = "Alpha", Kind = FeedTreeNodeKind.Feed, FeedId = id, FolderId = folderId };

    private static FeedTreeNode Folder(long id) =>
        new() { Title = "News", Kind = FeedTreeNodeKind.Folder, FolderId = id };

    private static FeedTreeNode Smart(ItemFilter filter) =>
        new() { Title = "Starred", Kind = FeedTreeNodeKind.Smart, SmartFilter = filter };

    [Fact]
    public void By_default_a_search_spans_every_feed_and_keeps_the_current_filter()
    {
        var query = SearchQueryBuilder.Build("kingfishers", Feed(7), ItemFilter.Unread, false, 500);

        Assert.Equal(new SearchQuery("kingfishers", null, null, ItemFilter.Unread, 500), query);
    }

    [Fact]
    public void The_scope_toggle_narrows_a_search_to_the_selected_feed()
    {
        var query = SearchQueryBuilder.Build("kingfishers", Feed(7), ItemFilter.All, true, 500);

        Assert.Equal(new SearchQuery("kingfishers", 7, null, ItemFilter.All, 500), query);
    }

    [Fact]
    public void The_scope_toggle_narrows_a_search_to_the_selected_folder()
    {
        var query = SearchQueryBuilder.Build("kingfishers", Folder(3), ItemFilter.All, true, 500);

        Assert.Equal(new SearchQuery("kingfishers", null, 3, ItemFilter.All, 500), query);
    }

    [Fact]
    public void A_feed_inside_a_folder_scopes_to_the_feed_not_the_folder()
    {
        var query = SearchQueryBuilder.Build("kingfishers", Feed(7, folderId: 3), ItemFilter.All, true, 500);

        Assert.Equal(new SearchQuery("kingfishers", 7, null, ItemFilter.All, 500), query);
    }

    [Fact]
    public void A_smart_row_overrides_the_current_filter_with_its_own()
    {
        // The Starred smart row is selected while the segment says Unread,
        // exactly as ItemQueryBuilder resolves the same conflict.
        var query = SearchQueryBuilder.Build("kingfishers", Smart(ItemFilter.Starred), ItemFilter.Unread, false, 500);

        Assert.Equal(new SearchQuery("kingfishers", null, null, ItemFilter.Starred, 500), query);
    }

    [Fact]
    public void A_smart_row_is_not_a_scope_even_with_the_toggle_on()
    {
        var query = SearchQueryBuilder.Build("kingfishers", Smart(ItemFilter.All), ItemFilter.All, true, 500);

        Assert.Equal(new SearchQuery("kingfishers", null, null, ItemFilter.All, 500), query);
    }

    [Fact]
    public void No_selection_is_not_a_scope_either()
    {
        var query = SearchQueryBuilder.Build("kingfishers", null, ItemFilter.Starred, true, 500);

        Assert.Equal(new SearchQuery("kingfishers", null, null, ItemFilter.Starred, 500), query);
    }

    [Theory]
    [InlineData(FeedTreeNodeKind.Feed, true)]
    [InlineData(FeedTreeNodeKind.Folder, true)]
    [InlineData(FeedTreeNodeKind.Smart, false)]
    public void CanScope_agrees_with_what_Build_does(FeedTreeNodeKind kind, bool expected)
    {
        var node = kind switch
        {
            FeedTreeNodeKind.Feed => Feed(7),
            FeedTreeNodeKind.Folder => Folder(3),
            _ => Smart(ItemFilter.All)
        };

        Assert.Equal(expected, SearchQueryBuilder.CanScope(node));

        var scoped = SearchQueryBuilder.Build("x", node, ItemFilter.All, true, 500);
        Assert.Equal(expected, scoped.FeedId is not null || scoped.FolderId is not null);
    }

    [Fact]
    public void CanScope_says_no_to_nothing_selected()
    {
        Assert.False(SearchQueryBuilder.CanScope(null));
    }

    /// <summary>
    /// A node whose kind says feed but which carries no id cannot be scoped
    /// to. Without this the toggle would enable itself against a scope that
    /// resolves to "everything", so turning it on would appear to do nothing.
    /// </summary>
    [Fact]
    public void A_feed_node_with_no_id_is_not_scopeable()
    {
        var node = new FeedTreeNode { Title = "Broken", Kind = FeedTreeNodeKind.Feed };

        Assert.False(SearchQueryBuilder.CanScope(node));
        Assert.Null(SearchQueryBuilder.Build("x", node, ItemFilter.All, true, 500).FeedId);
    }
}
