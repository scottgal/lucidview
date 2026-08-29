using LucidReader.Core.Model;
using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class FeedSettingsDraftTests
{
    private static Feed Feed() => new() { Id = 7, FeedUrl = "https://example.com/feed.xml", Title = "Example" };

    private static FeedSettingsDraft Dialog(Feed? feed = null) =>
        new(feed ?? Feed(), ReaderSettings.Defaults);

    [Fact]
    public void A_feed_with_no_overrides_opens_with_every_override_switched_off()
    {
        var dialog = Dialog();

        Assert.False(dialog.OverrideRefreshInterval);
        Assert.False(dialog.OverrideAutoDownload);
        Assert.False(dialog.OverrideFetchFullText);
        Assert.False(dialog.OverrideRetention);
    }

    [Fact]
    public void The_header_shows_the_override_when_the_feed_has_no_publisher_title_yet()
    {
        var dialog = Dialog(new Feed
        {
            Id = 7,
            FeedUrl = "https://example.com/feed.xml",
            Title = null,
            TitleOverride = "My name for it"
        });

        Assert.Equal("My name for it", dialog.DisplayTitle);
    }

    [Fact]
    public void An_override_switched_off_is_saved_as_null_not_as_the_global_value()
    {
        var dialog = Dialog(Feed() with { RefreshIntervalMinutes = 15 });
        Assert.True(dialog.OverrideRefreshInterval);

        dialog.OverrideRefreshInterval = false;
        var applied = dialog.Apply();

        Assert.Null(applied.RefreshIntervalMinutes);
    }

    [Fact]
    public void An_override_switched_on_is_saved_as_a_value()
    {
        var dialog = Dialog();
        dialog.OverrideRefreshInterval = true;
        dialog.RefreshIntervalMinutes = 15;

        var applied = dialog.Apply();

        Assert.Equal(15, applied.RefreshIntervalMinutes);
    }

    [Fact]
    public void A_false_override_is_saved_as_false_and_not_mistaken_for_unset()
    {
        var dialog = Dialog();
        dialog.OverrideAutoDownload = true;
        dialog.AutoDownload = false;

        var applied = dialog.Apply();

        Assert.False(applied.AutoDownload);
        Assert.NotNull(dialog.Result.AutoDownload);
    }

    [Fact]
    public void The_inherited_value_is_shown_so_the_user_knows_what_they_are_overriding()
    {
        var globals = ReaderSettings.Defaults with { DefaultRefreshIntervalMinutes = 45 };
        var dialog = new FeedSettingsDraft(Feed(), globals);

        Assert.Contains("45", dialog.InheritedRefreshIntervalLabel);
    }

    [Fact]
    public void A_blank_title_override_is_saved_as_null_rather_than_an_empty_string()
    {
        var dialog = Dialog(Feed() with { TitleOverride = "My name" });
        dialog.TitleOverride = "   ";

        var applied = dialog.Apply();

        Assert.Null(applied.TitleOverride);
    }

    [Fact]
    public void Fetch_bookkeeping_is_carried_through_untouched()
    {
        var feed = Feed() with { ETag = "\"abc\"", ConsecutiveFailures = 3, LastError = "boom" };
        var dialog = Dialog(feed);

        var applied = dialog.Apply();

        Assert.Equal("\"abc\"", applied.ETag);
        Assert.Equal(3, dialog.Result.ConsecutiveFailures);
        Assert.Equal("boom", dialog.Result.LastError);
    }
}
