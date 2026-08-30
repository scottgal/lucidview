using LucidReader.Core.Model;
using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class SettingsDraftTests
{
    [Fact]
    public void Applying_returns_the_edited_settings()
    {
        var draft = new SettingsDraft(ReaderSettings.Defaults)
        {
            DefaultRefreshIntervalMinutes = 120,
            AutoDownloadArticles = false,
            Theme = "Dark"
        };

        var result = draft.Apply();

        Assert.Equal(120, result.DefaultRefreshIntervalMinutes);
        Assert.False(result.AutoDownloadArticles);
        Assert.Equal("Dark", result.Theme);
    }

    [Fact]
    public void Every_other_setting_survives_editing_one_of_them()
    {
        var original = ReaderSettings.Defaults with { MaxArticlesPerFeed = 123, FontSize = 19 };
        var draft = new SettingsDraft(original) { DefaultRefreshIntervalMinutes = 45 };

        var result = draft.Apply();

        Assert.Equal(123, result.MaxArticlesPerFeed);
        Assert.Equal(19, result.FontSize);
    }

    [Fact]
    public void A_refresh_interval_below_the_floor_is_clamped()
    {
        var draft = new SettingsDraft(ReaderSettings.Defaults) { DefaultRefreshIntervalMinutes = 1 };

        Assert.Equal(
            (int)ReaderSettings.MinimumRefreshInterval.TotalMinutes,
            draft.Apply().DefaultRefreshIntervalMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_nonsense_concurrency_value_is_clamped_to_at_least_one(int value)
    {
        var draft = new SettingsDraft(ReaderSettings.Defaults) { MaxConcurrentFetches = value };

        Assert.True(draft.Apply().MaxConcurrentFetches >= 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_nonsense_download_concurrency_value_is_clamped_to_at_least_one(int value)
    {
        var draft = new SettingsDraft(ReaderSettings.Defaults) { MaxConcurrentDownloads = value };

        Assert.True(draft.Apply().MaxConcurrentDownloads >= 1);
    }

    [Fact]
    public void A_draft_left_untouched_reproduces_the_original_exactly()
    {
        var original = ReaderSettings.Defaults with { FontSize = 17, Theme = "GitHub" };

        Assert.Equal(original, new SettingsDraft(original).Apply());
    }

    [Fact]
    public void Online_feed_search_is_off_by_default_and_toggleable()
    {
        Assert.False(new SettingsDraft(ReaderSettings.Defaults).Apply().EnableOnlineFeedSearch);

        var draft = new SettingsDraft(ReaderSettings.Defaults) { EnableOnlineFeedSearch = true };

        Assert.True(draft.Apply().EnableOnlineFeedSearch);
    }

    [Fact]
    public void The_typography_settings_round_trip_through_the_draft()
    {
        var draft = new SettingsDraft(ReaderSettings.Defaults)
        {
            FontSize = 18,
            LineHeight = 1.8,
            CodeFontSize = 11,
            ColumnWidth = 900
        };

        var result = draft.Apply();

        Assert.Equal(18, result.FontSize);
        Assert.Equal(1.8, result.LineHeight);
        Assert.Equal(11, result.CodeFontSize);
        Assert.Equal(900, result.ColumnWidth);

        // Reopening the dialog builds a fresh draft from the saved settings,
        // which is what the dialog does; if that dropped a value the setting
        // would appear to save and then revert.
        var reopened = new SettingsDraft(result);

        Assert.Equal(18, reopened.FontSize);
        Assert.Equal(1.8, reopened.LineHeight);
        Assert.Equal(11, reopened.CodeFontSize);
        Assert.Equal(900, reopened.ColumnWidth);
    }

    [Fact]
    public void The_typography_defaults_match_lucidVIEW()
    {
        Assert.Equal(15, ReaderSettings.Defaults.FontSize);
        Assert.Equal(1.5, ReaderSettings.Defaults.LineHeight);
        Assert.Equal(13, ReaderSettings.Defaults.CodeFontSize);
    }

    [Fact]
    public void A_line_height_below_one_is_clamped_up_to_the_typefaces_own_metrics()
    {
        var draft = new SettingsDraft(ReaderSettings.Defaults) { LineHeight = 0 };

        Assert.Equal(1.0, draft.Apply().LineHeight);
    }

    [Fact]
    public void An_out_of_range_code_font_size_is_clamped()
    {
        Assert.Equal(8, new SettingsDraft(ReaderSettings.Defaults) { CodeFontSize = 1 }.Apply().CodeFontSize);
        Assert.Equal(32, new SettingsDraft(ReaderSettings.Defaults) { CodeFontSize = 999 }.Apply().CodeFontSize);
    }

    [Fact]
    public void A_column_width_outside_the_readable_range_is_clamped()
    {
        Assert.Equal(ReadingColumnMetrics.MinimumWidth,
            new SettingsDraft(ReaderSettings.Defaults) { ColumnWidth = 10 }.Apply().ColumnWidth);
        Assert.Equal(ReadingColumnMetrics.MaximumWidth,
            new SettingsDraft(ReaderSettings.Defaults) { ColumnWidth = 9000 }.Apply().ColumnWidth);
    }

    [Fact]
    public void Human_readable_sizes_are_formatted_sensibly()
    {
        Assert.Equal("0 bytes", SettingsDraft.FormatBytes(0));
        Assert.Equal("512 bytes", SettingsDraft.FormatBytes(512));
        Assert.Equal("1.0 KB", SettingsDraft.FormatBytes(1024));
        Assert.Equal("1.5 MB", SettingsDraft.FormatBytes(1024 * 1024 * 3 / 2));
        Assert.Equal("2.0 GB", SettingsDraft.FormatBytes(1024L * 1024 * 1024 * 2));
    }

    [Fact]
    public void The_alert_settings_round_trip_through_the_draft()
    {
        var draft = new SettingsDraft(ReaderSettings.Defaults)
        {
            EnableNotifications = false,
            NotifyOnlyWhenUnfocused = false,
            ShowStatusItem = true,
            CloseKeepsRunning = true
        };

        var applied = draft.Apply();

        Assert.False(applied.EnableNotifications);
        Assert.False(applied.NotifyOnlyWhenUnfocused);
        Assert.True(applied.ShowStatusItem);
        Assert.True(applied.CloseKeepsRunning);
    }

    /// <summary>
    /// The one clamp among the alert settings that is not tidiness. Keeping
    /// mylo running once the window has closed is only reachable through the
    /// status item, so saving that combination with the status item off would
    /// produce a running app with no window and no way to reach one.
    /// </summary>
    [Fact]
    public void Keeping_mylo_running_is_turned_off_with_the_status_item()
    {
        var applied = new SettingsDraft(ReaderSettings.Defaults)
        {
            ShowStatusItem = false,
            CloseKeepsRunning = true
        }.Apply();

        Assert.False(applied.CloseKeepsRunning);
    }

    [Fact]
    public void A_setting_this_dialog_does_not_expose_survives_a_round_trip()
    {
        // HasSeededDefaultFeeds has no control anywhere and must not be reset
        // by saving the dialog: a profile whose owner unsubscribed from
        // everything would be handed the starter list again on the next
        // launch.
        var original = ReaderSettings.Defaults with { HasSeededDefaultFeeds = true };

        Assert.True(new SettingsDraft(original).Apply().HasSeededDefaultFeeds);
    }
}
