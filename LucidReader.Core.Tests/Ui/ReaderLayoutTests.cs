using LucidReader.Core.Model;
using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// The pane-collapse cycle. The window itself cannot be constructed in a test
/// here, which is exactly why the decision lives in ReaderLayout rather than
/// in MainWindow: the order the panes collapse in, and the guarantee that the
/// reading pane never goes, are the parts worth pinning down.
/// </summary>
public class ReaderLayoutTests
{
    [Fact]
    public void Cycle_collapses_leftmost_first_then_the_next_one_left()
    {
        var first = ReaderLayout.Next(ReaderLayoutMode.ThreePane);
        var second = ReaderLayout.Next(first);
        var third = ReaderLayout.Next(second);

        Assert.Equal(ReaderLayoutMode.ListAndReading, first);
        Assert.Equal(ReaderLayoutMode.ReadingOnly, second);
        Assert.Equal(ReaderLayoutMode.ThreePane, third);
    }

    [Fact]
    public void Three_clicks_return_to_where_they_started_from_any_mode()
    {
        foreach (var mode in Enum.GetValues<ReaderLayoutMode>())
        {
            var walked = ReaderLayout.Next(ReaderLayout.Next(ReaderLayout.Next(mode)));
            Assert.Equal(mode, walked);
        }
    }

    [Theory]
    [InlineData(ReaderLayoutMode.ThreePane, true, true)]
    [InlineData(ReaderLayoutMode.ListAndReading, false, true)]
    [InlineData(ReaderLayoutMode.ReadingOnly, false, false)]
    public void Each_mode_shows_the_panes_its_name_says(
        ReaderLayoutMode mode, bool sidebar, bool itemList)
    {
        Assert.Equal(sidebar, ReaderLayout.ShowsSidebar(mode));
        Assert.Equal(itemList, ReaderLayout.ShowsItemList(mode));
    }

    /// <summary>
    /// The invariant the whole cycle rests on. A mode with no reading pane
    /// would leave the article with nowhere to be drawn, and the button would
    /// then be a way to make the app useless in two clicks.
    /// </summary>
    [Fact]
    public void The_reading_pane_is_never_collapsed()
    {
        foreach (var mode in Enum.GetValues<ReaderLayoutMode>())
            Assert.True(ReaderLayout.ShowsReadingPane(mode));
    }

    [Fact]
    public void Panes_are_only_ever_removed_from_the_left()
    {
        // A pane that is hidden in one mode must stay hidden in every later
        // mode of the cycle up to the reset, which is what "sequential, left
        // to right" means. Stated as a count so it also catches a mode that
        // hid the list while keeping the sidebar.
        Assert.Equal(3, ReaderLayout.VisiblePaneCount(ReaderLayoutMode.ThreePane));
        Assert.Equal(2, ReaderLayout.VisiblePaneCount(ReaderLayoutMode.ListAndReading));
        Assert.Equal(1, ReaderLayout.VisiblePaneCount(ReaderLayoutMode.ReadingOnly));
    }

    [Fact]
    public void The_tooltip_names_what_the_next_click_does()
    {
        Assert.Equal("Hide the sidebar", ReaderLayout.DescribeNext(ReaderLayoutMode.ThreePane));
        Assert.Equal("Hide the article list", ReaderLayout.DescribeNext(ReaderLayoutMode.ListAndReading));
        Assert.Equal("Show the sidebar and article list",
            ReaderLayout.DescribeNext(ReaderLayoutMode.ReadingOnly));
    }

    [Fact]
    public void Every_mode_has_its_own_tooltip_and_description()
    {
        var modes = Enum.GetValues<ReaderLayoutMode>();

        Assert.Equal(modes.Length, modes.Select(ReaderLayout.DescribeNext).Distinct().Count());
        Assert.Equal(modes.Length, modes.Select(ReaderLayout.Describe).Distinct().Count());
    }

    [Theory]
    [InlineData("ThreePane", ReaderLayoutMode.ThreePane)]
    [InlineData("ListAndReading", ReaderLayoutMode.ListAndReading)]
    [InlineData("ReadingOnly", ReaderLayoutMode.ReadingOnly)]
    [InlineData("readingonly", ReaderLayoutMode.ReadingOnly)]
    public void Stored_values_parse_back_to_their_mode(string stored, ReaderLayoutMode expected) =>
        Assert.Equal(expected, ReaderLayout.Parse(stored));

    /// <summary>
    /// Anything unreadable has to land on the full layout. The failure that
    /// matters is a settings file from a build that knew a fourth mode: coming
    /// up with no sidebar and no list would leave the user unable to reach
    /// their feeds and with no obvious way back.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SomethingElse")]
    [InlineData("7")]
    public void Unrecognised_stored_values_fall_back_to_the_full_layout(string? stored) =>
        Assert.Equal(ReaderLayoutMode.ThreePane, ReaderLayout.Parse(stored));

    [Fact]
    public void Every_mode_round_trips_through_settings()
    {
        foreach (var mode in Enum.GetValues<ReaderLayoutMode>())
            Assert.Equal(mode, ReaderLayout.Parse(ReaderLayout.ToStoredValue(mode)));
    }

    [Fact]
    public void The_default_setting_is_the_full_layout() =>
        Assert.Equal(ReaderLayoutMode.ThreePane, ReaderLayout.Parse(ReaderSettings.Defaults.LayoutMode));

    /// <summary>
    /// The settings dialog has no control for the layout, so its round trip
    /// must not reset one the toolbar button set.
    /// </summary>
    [Fact]
    public void The_settings_dialog_round_trip_leaves_the_layout_alone()
    {
        var settings = ReaderSettings.Defaults with { LayoutMode = "ReadingOnly" };

        var applied = new SettingsDraft(settings).Apply();

        Assert.Equal("ReadingOnly", applied.LayoutMode);
    }
}
