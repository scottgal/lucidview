using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class SidebarSectionTests
{
    [Fact]
    public void A_section_reports_the_total_unread_of_its_nodes()
    {
        var section = new SidebarSection { Title = "Feeds" };
        section.Nodes.Add(new FeedTreeNode { Title = "A", UnreadCount = 3 });
        section.Nodes.Add(new FeedTreeNode { Title = "B", UnreadCount = 4 });

        Assert.Equal(7, section.UnreadCount);
    }

    [Fact]
    public void A_sections_unread_total_updates_when_a_node_changes()
    {
        var section = new SidebarSection { Title = "Feeds" };
        var node = new FeedTreeNode { Title = "A", UnreadCount = 3 };
        section.Nodes.Add(node);

        node.UnreadCount = 10;

        Assert.Equal(10, section.UnreadCount);
    }

    [Fact]
    public void A_sections_unread_total_updates_when_a_node_is_added()
    {
        var section = new SidebarSection { Title = "Feeds" };
        section.Nodes.Add(new FeedTreeNode { Title = "A", UnreadCount = 1 });

        section.Nodes.Add(new FeedTreeNode { Title = "B", UnreadCount = 2 });

        Assert.Equal(3, section.UnreadCount);
    }

    [Fact]
    public void A_folders_own_unread_count_is_not_double_counted_with_its_feeds()
    {
        // A folder's UnreadCount already mirrors the sum of the feeds nested
        // under it (see MainWindow.LoadFeedTreeAsync), and the Feeds section
        // contains both the folder row and its feed rows flattened together.
        // If SidebarSection.UnreadCount ever stopped excluding Folder-kind
        // nodes, this section total would double every feed inside a folder.
        var section = new SidebarSection { Title = "Feeds" };
        var folder = new FeedTreeNode { Title = "Tech", Kind = FeedTreeNodeKind.Folder, UnreadCount = 7 };
        var feedOne = new FeedTreeNode { Title = "A", Kind = FeedTreeNodeKind.Feed, FolderId = 1, UnreadCount = 3 };
        var feedTwo = new FeedTreeNode { Title = "B", Kind = FeedTreeNodeKind.Feed, FolderId = 1, UnreadCount = 4 };

        section.Nodes.Add(folder);
        section.Nodes.Add(feedOne);
        section.Nodes.Add(feedTwo);

        Assert.Equal(7, section.UnreadCount);
    }

    [Fact]
    public void An_empty_section_is_hidden_rather_than_showing_an_empty_header()
    {
        var section = new SidebarSection { Title = "Tags" };

        Assert.False(section.IsVisible);
        section.Nodes.Add(new FeedTreeNode { Title = "A" });
        Assert.True(section.IsVisible);
    }
}
