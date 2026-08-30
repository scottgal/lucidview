using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// The arithmetic behind the reading pane's centred column. It lives in a
/// plain class precisely so it can be tested here; a Window cannot be
/// constructed in a unit test in this repo, so nothing about the layout would
/// be checkable at all if this logic sat in MainWindow.
/// </summary>
public class ReadingColumnMetricsTests
{
    private const double Overhead =
        (ReadingColumnMetrics.SideGutter * 2) + ReadingColumnMetrics.ScrollBarReserve;

    [Fact]
    public void A_width_the_pane_can_show_is_used_as_is()
    {
        var resolved = ReadingColumnMetrics.Resolve(preferredWidth: 760, paneWidth: 1400);

        Assert.Equal(760, resolved);
    }

    [Fact]
    public void A_saved_width_wider_than_the_pane_is_clamped_down()
    {
        var resolved = ReadingColumnMetrics.Resolve(preferredWidth: 1600, paneWidth: 900);

        Assert.Equal(900 - Overhead, resolved);
        Assert.True(resolved < 1600);
    }

    [Fact]
    public void A_width_below_the_minimum_is_clamped_up()
    {
        var resolved = ReadingColumnMetrics.Resolve(preferredWidth: 100, paneWidth: 1400);

        Assert.Equal(ReadingColumnMetrics.MinimumWidth, resolved);
    }

    [Fact]
    public void A_pane_too_narrow_for_the_minimum_still_gets_the_minimum()
    {
        // The column would rather overflow and let the pane scroll than
        // collapse to something unreadable.
        var resolved = ReadingColumnMetrics.Resolve(preferredWidth: 760, paneWidth: 200);

        Assert.Equal(ReadingColumnMetrics.MinimumWidth, resolved);
    }

    /// <summary>
    /// The one that matters most: clamping is a view-time decision, never a
    /// write back to the setting. A pane that grows again has to give the user
    /// their preferred width back rather than leaving them stuck at whatever
    /// the narrowest moment allowed.
    /// </summary>
    [Fact]
    public void Widening_the_pane_restores_the_preferred_width_rather_than_a_previous_clamp()
    {
        const double preferred = 1200;

        var whileNarrow = ReadingColumnMetrics.Resolve(preferred, paneWidth: 700);
        Assert.True(whileNarrow < preferred, "the narrow pane should have clamped");

        // Resolve is pure, so the second call is handed the same preferred
        // width the first one was: nothing recorded the clamp.
        var afterWidening = ReadingColumnMetrics.Resolve(preferred, paneWidth: 1600);

        Assert.Equal(preferred, afterWidening);
    }

    [Fact]
    public void A_preferred_width_above_the_maximum_is_capped()
    {
        var resolved = ReadingColumnMetrics.Resolve(preferredWidth: 5000, paneWidth: 4000);

        Assert.Equal(ReadingColumnMetrics.MaximumWidth, resolved);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public void An_unmeasured_pane_falls_back_to_the_preferred_width(double paneWidth)
    {
        var resolved = ReadingColumnMetrics.Resolve(preferredWidth: 760, paneWidth: paneWidth);

        Assert.Equal(760, resolved);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public void An_unset_preferred_width_falls_back_to_the_minimum(double preferredWidth)
    {
        var resolved = ReadingColumnMetrics.Resolve(preferredWidth, paneWidth: 1400);

        Assert.Equal(ReadingColumnMetrics.MinimumWidth, resolved);
    }
}
