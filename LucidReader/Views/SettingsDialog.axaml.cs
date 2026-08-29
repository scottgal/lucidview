using Avalonia.Controls;
using Avalonia.Interactivity;
using LucidReader.Core.Maintenance;
using LucidReader.Core.Model;
using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// A thin shell over <see cref="SettingsDraft"/>: every value is read from
/// the draft into a named control when the dialog opens, and read back into
/// the draft when Save is clicked. All mapping and clamping lives in the
/// draft, not here, because a Window cannot be constructed in a unit test in
/// this repo - see SettingsDraftTests for the part that actually matters.
///
/// This has a public parameterless constructor purely so the generated
/// InitializeComponent() (from the XAML compiler, not a hand-written
/// override) populates every x:Name field, matching ConfirmDialog and
/// InputDialog. A hand-rolled InitializeComponent that only calls
/// AvaloniaXamlLoader.Load - the trap MainWindow has, documented in its own
/// constructor comment - would leave every field below null.
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly RetentionService? _retention;

    public SettingsDialog()
    {
        InitializeComponent();
        Draft = new SettingsDraft(ReaderSettings.Defaults);
    }

    public SettingsDialog(ReaderSettings current, RetentionService retention) : this()
    {
        _retention = retention;
        Draft = new SettingsDraft(current);

        DefaultRefreshIntervalBox.Value = Draft.DefaultRefreshIntervalMinutes;
        RefreshOnStartupCheck.IsChecked = Draft.RefreshOnStartup;
        PauseWhenOfflineCheck.IsChecked = Draft.PauseWhenOffline;
        MaxConcurrentFetchesBox.Value = Draft.MaxConcurrentFetches;
        EnableOnlineFeedSearchCheck.IsChecked = Draft.EnableOnlineFeedSearch;

        AutoDownloadCheck.IsChecked = Draft.AutoDownloadArticles;
        FetchFullTextCheck.IsChecked = Draft.FetchFullText;
        CacheImagesCheck.IsChecked = Draft.CacheImages;
        MaxConcurrentDownloadsBox.Value = Draft.MaxConcurrentDownloads;

        KeepReadDaysBox.Value = Draft.KeepReadArticlesDays;
        KeepUnreadForeverCheck.IsChecked = Draft.KeepUnreadForever;
        KeepUnreadDaysBox.Value = Draft.KeepUnreadDays;
        MaxArticlesPerFeedBox.Value = Draft.MaxArticlesPerFeed;
        NeverDeleteStarredCheck.IsChecked = Draft.NeverDeleteStarred;

        FontSizeBox.Value = (decimal)Draft.FontSize;
        ColumnWidthBox.Value = (decimal)Draft.ColumnWidth;
        MarkReadDwellBox.Value = Draft.MarkReadDwellMilliseconds;
        OpenLinksExternallyCheck.IsChecked = Draft.OpenLinksExternally;

        Opened += async (_, _) => await RefreshDatabaseSizeAsync();
    }

    public SettingsDraft Draft { get; }

    /// <summary>Null when the dialog was cancelled.</summary>
    public ReaderSettings? Result { get; private set; }

    private void OnTabChecked(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;

        UpdatesPanel.IsVisible = tag == "Updates";
        OfflinePanel.IsVisible = tag == "Offline";
        RetentionPanel.IsVisible = tag == "Retention";
        ReadingPanel.IsVisible = tag == "Reading";
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Draft.DefaultRefreshIntervalMinutes = (int)(DefaultRefreshIntervalBox.Value ?? Draft.DefaultRefreshIntervalMinutes);
        Draft.RefreshOnStartup = RefreshOnStartupCheck.IsChecked ?? Draft.RefreshOnStartup;
        Draft.PauseWhenOffline = PauseWhenOfflineCheck.IsChecked ?? Draft.PauseWhenOffline;
        Draft.MaxConcurrentFetches = (int)(MaxConcurrentFetchesBox.Value ?? Draft.MaxConcurrentFetches);
        Draft.EnableOnlineFeedSearch = EnableOnlineFeedSearchCheck.IsChecked ?? Draft.EnableOnlineFeedSearch;

        Draft.AutoDownloadArticles = AutoDownloadCheck.IsChecked ?? Draft.AutoDownloadArticles;
        Draft.FetchFullText = FetchFullTextCheck.IsChecked ?? Draft.FetchFullText;
        Draft.CacheImages = CacheImagesCheck.IsChecked ?? Draft.CacheImages;
        Draft.MaxConcurrentDownloads = (int)(MaxConcurrentDownloadsBox.Value ?? Draft.MaxConcurrentDownloads);

        Draft.KeepReadArticlesDays = (int)(KeepReadDaysBox.Value ?? Draft.KeepReadArticlesDays);
        Draft.KeepUnreadForever = KeepUnreadForeverCheck.IsChecked ?? Draft.KeepUnreadForever;
        Draft.KeepUnreadDays = (int)(KeepUnreadDaysBox.Value ?? Draft.KeepUnreadDays);
        Draft.MaxArticlesPerFeed = (int)(MaxArticlesPerFeedBox.Value ?? Draft.MaxArticlesPerFeed);
        Draft.NeverDeleteStarred = NeverDeleteStarredCheck.IsChecked ?? Draft.NeverDeleteStarred;

        Draft.FontSize = (double)(FontSizeBox.Value ?? (decimal)Draft.FontSize);
        Draft.ColumnWidth = (double)(ColumnWidthBox.Value ?? (decimal)Draft.ColumnWidth);
        Draft.MarkReadDwellMilliseconds = (int)(MarkReadDwellBox.Value ?? Draft.MarkReadDwellMilliseconds);
        Draft.OpenLinksExternally = OpenLinksExternallyCheck.IsChecked ?? Draft.OpenLinksExternally;

        Result = Draft.Apply();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private async void OnCleanUpNow(object? sender, RoutedEventArgs e)
    {
        if (_retention is null) return;

        DatabaseSizeText.Text = "Cleaning up...";
        var deleted = await _retention.PruneAsync();
        await RefreshDatabaseSizeAsync();
        DatabaseSizeText.Text += deleted == 1 ? "  (1 article removed)" : $"  ({deleted} articles removed)";
    }

    private async Task RefreshDatabaseSizeAsync()
    {
        if (_retention is null) return;

        try { DatabaseSizeText.Text = SettingsDraft.FormatBytes(await _retention.GetDatabaseSizeBytesAsync()); }
        catch (Exception ex) { DatabaseSizeText.Text = "Unavailable: " + ex.Message; }
    }
}
