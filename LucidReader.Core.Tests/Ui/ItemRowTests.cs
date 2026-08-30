using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class ItemRowTests
{
    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "30m")]
    [InlineData(120, "2h")]
    [InlineData(60 * 24 * 3, "3d")]
    public void Relative_dates_read_the_way_a_person_expects(int minutesAgo, string expected)
    {
        var now = DateTimeOffset.Parse("2026-08-29T12:00:00Z");

        Assert.Equal(expected, ItemRow.FormatRelative(now.AddMinutes(-minutesAgo), now));
    }

    [Fact]
    public void An_item_dated_in_the_future_does_not_render_a_negative_age()
    {
        var now = DateTimeOffset.Parse("2026-08-29T12:00:00Z");

        Assert.Equal("just now", ItemRow.FormatRelative(now.AddHours(3), now));
    }

    [Fact]
    public void An_old_item_falls_back_to_an_absolute_date()
    {
        var now = DateTimeOffset.Parse("2026-08-29T12:00:00Z");

        Assert.Contains("2026", ItemRow.FormatRelative(now.AddDays(-40), now));
    }

    [Fact]
    public void Marking_a_row_read_flips_its_weight()
    {
        var row = new ItemRow
        {
            Item = new LucidReader.Core.Model.FeedItem
            {
                FeedId = 1, Guid = "g", FirstSeenUtc = DateTimeOffset.UtcNow
            },
            FeedName = "Example"
        };

        Assert.Equal(Avalonia.Media.FontWeight.SemiBold, row.TitleWeight);
        row.IsRead = true;
        Assert.Equal(Avalonia.Media.FontWeight.Normal, row.TitleWeight);
    }
}
