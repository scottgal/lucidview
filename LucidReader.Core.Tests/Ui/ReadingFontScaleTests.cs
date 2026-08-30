using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class ReadingFontScaleTests
{
    [Fact]
    public void A_step_moves_one_point()
    {
        Assert.Equal(16, ReadingFontScale.Increase(15));
        Assert.Equal(14, ReadingFontScale.Decrease(15));
    }

    [Fact]
    public void Steps_land_on_whole_points()
    {
        Assert.Equal(16, ReadingFontScale.Increase(15.4));
        Assert.Equal(14, ReadingFontScale.Decrease(15.4));
    }

    /// <summary>
    /// Both ends are reachable by holding a key down, so both have to hold.
    /// </summary>
    [Fact]
    public void The_range_is_bounded_at_both_ends()
    {
        Assert.Equal(ReadingFontScale.Maximum, ReadingFontScale.Increase(ReadingFontScale.Maximum));
        Assert.Equal(ReadingFontScale.Minimum, ReadingFontScale.Decrease(ReadingFontScale.Minimum));
        Assert.Equal(ReadingFontScale.Maximum, ReadingFontScale.Increase(1000));
        Assert.Equal(ReadingFontScale.Minimum, ReadingFontScale.Decrease(-1000));
    }

    [Fact]
    public void Reset_returns_the_default()
    {
        Assert.Equal(ReadingFontScale.Default, ReadingFontScale.Reset());
    }

    /// <summary>
    /// settings.json is a text file a person can edit, and every value in it
    /// reaches the reading pane. A size of zero, a negative or a NaN must not.
    /// </summary>
    [Fact]
    public void A_nonsense_stored_size_is_repaired_rather_than_propagated()
    {
        Assert.Equal(ReadingFontScale.Default, ReadingFontScale.Clamp(double.NaN));
        Assert.Equal(ReadingFontScale.Minimum, ReadingFontScale.Clamp(0));
        Assert.Equal(ReadingFontScale.Minimum, ReadingFontScale.Clamp(-4));
        Assert.Equal(ReadingFontScale.Default, ReadingFontScale.Increase(double.NaN) - ReadingFontScale.Step);
    }

    [Fact]
    public void The_default_matches_the_settings_default()
    {
        Assert.Equal(ReadingFontScale.Default, LucidReader.Core.Model.ReaderSettings.Defaults.FontSize);
    }
}
