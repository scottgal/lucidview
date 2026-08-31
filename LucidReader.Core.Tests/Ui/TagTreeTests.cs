using LucidReader.Core.Storage;
using LucidReader.Models;
using LucidReader.Services;
using LucidReader.Views;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// The sidebar-side half of tagging: what a tag row maps onto, how the
/// selection survives a tree reload, and what the section header does with a
/// set of counts that overlap.
///
/// All of it on plain classes, none of it constructing a Window.
/// </summary>
public class TagTreeTests
{
    private static FeedTreeNode Tag(string name, int unread = 0) => new()
    {
        Title = name, Kind = FeedTreeNodeKind.Tag, TagName = name, UnreadCount = unread
    };

    // ================= what a tag row queries =================

    [Fact]
    public void A_tag_row_queries_by_tag_across_every_feed()
    {
        var query = ItemQueryBuilder.Build(Tag("later"), ItemFilter.All);

        Assert.Equal("later", query.TagName);
        Assert.Null(query.FeedId);
        Assert.Null(query.FolderId);
    }

    /// <summary>
    /// A tag is a scope, not a smart row: it keeps the filter segment rather
    /// than overriding it, so Unread still means unread inside a tag view.
    /// </summary>
    [Theory]
    [InlineData(ItemFilter.All)]
    [InlineData(ItemFilter.Unread)]
    [InlineData(ItemFilter.Starred)]
    public void A_tag_row_keeps_the_current_filter(ItemFilter filter)
    {
        var query = ItemQueryBuilder.Build(Tag("later"), filter);

        Assert.Equal(filter, query.Filter);
        Assert.Equal("later", query.TagName);
    }

    [Fact]
    public void A_feed_row_still_carries_no_tag()
    {
        var node = new FeedTreeNode { Title = "Feed", Kind = FeedTreeNodeKind.Feed, FeedId = 7 };

        Assert.Null(ItemQueryBuilder.Build(node, ItemFilter.All).TagName);
    }

    // ================= search scoping =================

    [Fact]
    public void A_tag_is_something_a_search_can_be_narrowed_to()
    {
        Assert.True(SearchQueryBuilder.CanScope(Tag("later")));
        Assert.False(SearchQueryBuilder.CanScope(
            new FeedTreeNode { Title = "Tagless", Kind = FeedTreeNodeKind.Tag }));
    }

    /// <summary>
    /// The same decision the feed and folder scopes follow: search spans
    /// everything unless the toggle is on, so a tag selection does not
    /// silently narrow it.
    /// </summary>
    [Fact]
    public void A_search_under_a_tag_is_unscoped_unless_the_toggle_is_on()
    {
        var unscoped = SearchQueryBuilder.Build("x", Tag("later"), ItemFilter.All, false, 50);
        var scoped = SearchQueryBuilder.Build("x", Tag("later"), ItemFilter.All, true, 50);

        Assert.Null(unscoped.TagName);
        Assert.Equal("later", scoped.TagName);
    }

    [Fact]
    public void A_scoped_search_under_a_tag_carries_no_feed_or_folder()
    {
        var scoped = SearchQueryBuilder.Build("x", Tag("later"), ItemFilter.Unread, true, 50);

        Assert.Null(scoped.FeedId);
        Assert.Null(scoped.FolderId);
        Assert.Equal(ItemFilter.Unread, scoped.Filter);
    }

    [Fact]
    public void A_scoped_search_under_a_feed_still_carries_no_tag()
    {
        var node = new FeedTreeNode { Title = "Feed", Kind = FeedTreeNodeKind.Feed, FeedId = 7 };

        Assert.Null(SearchQueryBuilder.Build("x", node, ItemFilter.All, true, 50).TagName);
    }

    // ================= selection across a tree reload =================

    [Fact]
    public void A_tag_row_is_matched_across_a_reload_by_its_name()
    {
        Assert.True(MainWindow.IsSameRow(Tag("later"), Tag("later")));
        Assert.False(MainWindow.IsSameRow(Tag("later"), Tag("archive")));
    }

    /// <summary>
    /// The database matches tags with SQLite's NOCASE, so the tree has to
    /// agree: a reload that picks up a different spelling of the same tag
    /// still repoints onto it rather than dropping the selection.
    /// </summary>
    [Fact]
    public void A_tag_row_is_matched_across_a_reload_ignoring_ascii_case()
    {
        Assert.True(MainWindow.IsSameRow(Tag("DotNet"), Tag("dotnet")));
    }

    [Fact]
    public void A_tag_row_is_never_the_same_row_as_a_feed_or_a_folder()
    {
        var feed = new FeedTreeNode { Title = "later", Kind = FeedTreeNodeKind.Feed, FeedId = 1 };
        var folder = new FeedTreeNode { Title = "later", Kind = FeedTreeNodeKind.Folder, FolderId = 1 };

        Assert.False(MainWindow.IsSameRow(Tag("later"), feed));
        Assert.False(MainWindow.IsSameRow(Tag("later"), folder));
    }

    // ================= the section header =================

    /// <summary>
    /// Tags overlap by design - one article can carry two of them - so a sum
    /// across tag rows is not a count of anything a list can reach. The
    /// section header therefore shows no number, while each tag row still
    /// shows its own.
    /// </summary>
    [Fact]
    public void The_tags_section_header_carries_no_total()
    {
        var section = new SidebarSection { Title = "Tags" };
        section.Nodes.Add(Tag("later", 3));
        section.Nodes.Add(Tag("archive", 2));

        Assert.Equal(0, section.UnreadCount);
        Assert.Equal(string.Empty, section.UnreadLabel);
        Assert.True(section.IsVisible);
    }

    [Fact]
    public void An_empty_tags_section_renders_as_nothing()
    {
        Assert.False(new SidebarSection { Title = "Tags" }.IsVisible);
    }

    [Fact]
    public void A_feeds_section_total_is_unaffected_by_the_tag_exclusion()
    {
        var section = new SidebarSection { Title = "Feeds" };
        section.Nodes.Add(new FeedTreeNode { Title = "A", Kind = FeedTreeNodeKind.Feed, UnreadCount = 3 });
        section.Nodes.Add(new FeedTreeNode { Title = "B", Kind = FeedTreeNodeKind.Feed, UnreadCount = 4 });

        Assert.Equal(7, section.UnreadCount);
    }

    // ================= the row itself =================

    [Fact]
    public void Only_a_tag_row_reports_itself_as_one()
    {
        Assert.True(Tag("later").IsTag);
        Assert.False(Tag("later").IsFeed);
        Assert.False(new FeedTreeNode { Title = "Feed", Kind = FeedTreeNodeKind.Feed }.IsTag);
    }

    /// <summary>
    /// A tag row sits flush, like a folder and the smart rows. Only a feed
    /// inside a folder is indented, and a tag is not inside anything.
    /// </summary>
    [Fact]
    public void A_tag_row_sits_flush()
    {
        Assert.Equal(default, Tag("later").Indent);
    }

    [Fact]
    public void A_tag_row_shows_no_favicon_slot()
    {
        Assert.False(Tag("later").ShowIconPlaceholder);
        Assert.False(Tag("later").HasIcon);
    }

    [Fact]
    public void A_tag_row_shows_its_unread_count_only_when_it_has_one()
    {
        Assert.Equal(string.Empty, Tag("later").UnreadLabel);
        Assert.Equal("4", Tag("later", 4).UnreadLabel);
    }
}
