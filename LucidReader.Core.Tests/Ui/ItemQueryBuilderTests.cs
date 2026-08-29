using LucidReader.Core.Storage;
using LucidReader.Models;
using LucidReader.Services;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class ItemQueryBuilderTests
{
    [Fact]
    public void Null_selection_queries_every_feed_with_the_current_filter()
    {
        var query = ItemQueryBuilder.Build(null, ItemFilter.Unread);

        Assert.Equal(new ItemQuery(null, null, ItemFilter.Unread, 500, 0), query);
    }

    [Fact]
    public void A_smart_row_queries_every_feed_and_overrides_the_current_filter_with_its_own()
    {
        var node = new FeedTreeNode { Title = "Starred", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.Starred };

        // Current filter chip is Unread, but the smart row's own filter wins.
        var query = ItemQueryBuilder.Build(node, ItemFilter.Unread);

        Assert.Equal(new ItemQuery(null, null, ItemFilter.Starred, 500, 0), query);
    }

    [Fact]
    public void A_feed_row_scopes_to_that_feed_and_keeps_the_current_filter()
    {
        var node = new FeedTreeNode { Title = "Alpha", Kind = FeedTreeNodeKind.Feed, FeedId = 7, FolderId = 3 };

        var query = ItemQueryBuilder.Build(node, ItemFilter.Unread);

        Assert.Equal(new ItemQuery(7, null, ItemFilter.Unread, 500, 0), query);
    }

    [Fact]
    public void A_folder_row_scopes_to_that_folder_and_keeps_the_current_filter()
    {
        var node = new FeedTreeNode { Title = "News", Kind = FeedTreeNodeKind.Folder, FolderId = 3 };

        var query = ItemQueryBuilder.Build(node, ItemFilter.Starred);

        Assert.Equal(new ItemQuery(null, 3, ItemFilter.Starred, 500, 0), query);
    }
}
