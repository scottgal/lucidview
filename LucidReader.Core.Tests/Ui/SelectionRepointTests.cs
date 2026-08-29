using LucidReader.Core.Storage;
using LucidReader.Models;
using LucidReader.Views;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// MainWindow.LoadFeedTreeAsync throws every FeedTreeNode away and builds new
/// ones, so the selection has to be matched by what the row stands for rather
/// than by reference. IsSameRow is the rule that does it, kept static and
/// node-only so it can be tested without constructing a Window.
/// </summary>
public class SelectionRepointTests
{
    private static FeedTreeNode Feed(long id, bool paused = false) => new()
    {
        Title = "Feed " + id, Kind = FeedTreeNodeKind.Feed, FeedId = id, IsAutoPaused = paused
    };

    [Fact]
    public void A_rebuilt_feed_row_matches_the_one_it_replaces()
    {
        Assert.True(MainWindow.IsSameRow(Feed(7), Feed(7)));
    }

    [Fact]
    public void A_different_feed_does_not_match()
    {
        Assert.False(MainWindow.IsSameRow(Feed(7), Feed(8)));
    }

    [Fact]
    public void The_pause_state_of_the_rebuilt_row_does_not_affect_the_match()
    {
        // This is the whole point: the pre-reload node says paused and the
        // rebuilt one says resumed, and they must still be the same row so
        // the selection carries over and the Resume button re-evaluates.
        Assert.True(MainWindow.IsSameRow(Feed(7, paused: true), Feed(7, paused: false)));
    }

    [Fact]
    public void A_folder_matches_on_its_folder_id()
    {
        var before = new FeedTreeNode { Title = "News", Kind = FeedTreeNodeKind.Folder, FolderId = 3 };
        var after = new FeedTreeNode { Title = "News renamed", Kind = FeedTreeNodeKind.Folder, FolderId = 3 };
        var other = new FeedTreeNode { Title = "Blogs", Kind = FeedTreeNodeKind.Folder, FolderId = 4 };

        Assert.True(MainWindow.IsSameRow(before, after));
        Assert.False(MainWindow.IsSameRow(before, other));
    }

    [Fact]
    public void A_smart_row_matches_on_its_filter()
    {
        var unread = new FeedTreeNode
        {
            Title = "Unread", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.Unread
        };
        var rebuiltUnread = new FeedTreeNode
        {
            Title = "Unread", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.Unread
        };
        var starred = new FeedTreeNode
        {
            Title = "Starred", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.Starred
        };

        Assert.True(MainWindow.IsSameRow(unread, rebuiltUnread));
        Assert.False(MainWindow.IsSameRow(unread, starred));
    }

    [Fact]
    public void Rows_of_different_kinds_never_match()
    {
        var folder = new FeedTreeNode { Title = "News", Kind = FeedTreeNodeKind.Folder, FolderId = 1 };
        var feed = new FeedTreeNode { Title = "Feed", Kind = FeedTreeNodeKind.Feed, FeedId = 1 };

        Assert.False(MainWindow.IsSameRow(folder, feed));
    }

    [Fact]
    public void A_feed_row_with_no_id_matches_nothing()
    {
        // Defensive: a Feed-kind node without a FeedId is not a row anything
        // can be re-pointed at, and two of them are not "the same row".
        var a = new FeedTreeNode { Title = "Broken", Kind = FeedTreeNodeKind.Feed };
        var b = new FeedTreeNode { Title = "Broken", Kind = FeedTreeNodeKind.Feed };

        Assert.False(MainWindow.IsSameRow(a, b));
    }
}
