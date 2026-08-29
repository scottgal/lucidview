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
    public void Human_readable_sizes_are_formatted_sensibly()
    {
        Assert.Equal("0 bytes", SettingsDraft.FormatBytes(0));
        Assert.Equal("512 bytes", SettingsDraft.FormatBytes(512));
        Assert.Equal("1.0 KB", SettingsDraft.FormatBytes(1024));
        Assert.Equal("1.5 MB", SettingsDraft.FormatBytes(1024 * 1024 * 3 / 2));
        Assert.Equal("2.0 GB", SettingsDraft.FormatBytes(1024L * 1024 * 1024 * 2));
    }
}
