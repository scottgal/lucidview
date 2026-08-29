using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class FeedTreeNodeTests
{
    [Fact]
    public void Unread_count_above_zero_shows_a_label_and_flag()
    {
        var node = new FeedTreeNode { Title = "Example" };

        Assert.False(node.HasUnread);
        Assert.Equal(string.Empty, node.UnreadLabel);

        node.UnreadCount = 4;

        Assert.True(node.HasUnread);
        Assert.Equal("4", node.UnreadLabel);
    }

    [Fact]
    public void A_feed_inside_a_folder_is_indented_but_a_folder_is_not()
    {
        var feedInFolder = new FeedTreeNode
        {
            Title = "Feed", Kind = FeedTreeNodeKind.Feed, FolderId = 1
        };
        var folder = new FeedTreeNode { Title = "Folder", Kind = FeedTreeNodeKind.Folder };
        var topLevelFeed = new FeedTreeNode { Title = "Feed", Kind = FeedTreeNodeKind.Feed };

        Assert.Equal(16, feedInFolder.Indent);
        Assert.Equal(0, folder.Indent);
        Assert.Equal(0, topLevelFeed.Indent);
    }

    [Fact]
    public void A_feed_with_consecutive_failures_or_an_auto_pause_has_a_problem()
    {
        var healthy = new FeedTreeNode { Title = "Feed", Kind = FeedTreeNodeKind.Feed };
        var failing = new FeedTreeNode { Title = "Feed", Kind = FeedTreeNodeKind.Feed, ConsecutiveFailures = 3 };
        var paused = new FeedTreeNode { Title = "Feed", Kind = FeedTreeNodeKind.Feed, IsAutoPaused = true };

        Assert.False(healthy.HasProblem);
        Assert.True(failing.HasProblem);
        Assert.True(paused.HasProblem);
    }
}
