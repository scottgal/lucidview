using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LucidReader.Core.Model;

namespace LucidReader.Views;

/// <summary>
/// A thin shell over <see cref="FeedSettingsDraft"/>: every value is read
/// from the draft into a named control when the dialog opens, and read back
/// into the draft when Save is clicked. All mapping and the inherit-versus-
/// override rule live in the draft, not here, because a Window cannot be
/// constructed in a unit test in this repo - see FeedSettingsDraftTests for
/// the part that actually matters.
///
/// Has a public parameterless constructor purely so the generated
/// InitializeComponent() populates every x:Name field, matching
/// ConfirmDialog, InputDialog and SettingsDialog.
/// </summary>
public partial class FeedSettingsDialog : Window
{
    public FeedSettingsDialog()
    {
        InitializeComponent();
        Draft = new FeedSettingsDraft(
            new Feed { FeedUrl = "https://example.com/feed.xml" },
            ReaderSettings.Defaults);
    }

    public FeedSettingsDialog(Feed feed, ReaderSettings globals, IReadOnlyList<Folder> folders) : this()
    {
        Draft = new FeedSettingsDraft(feed, globals);

        DisplayTitleText.Text = Draft.DisplayTitle;
        FeedUrlText.Text = Draft.FeedUrl;
        TitleOverrideBox.Text = Draft.TitleOverride;
        EnabledCheck.IsChecked = Draft.IsEnabled;

        var options = new List<FolderOption> { new(null, "No folder") };
        options.AddRange(folders.Select(f => new FolderOption(f.Id, f.Name)));
        FolderCombo.ItemsSource = options;
        FolderCombo.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(FolderOption.Name));
        FolderCombo.SelectedItem = options.FirstOrDefault(o => o.Id == Draft.FolderId) ?? options[0];

        OverrideRefreshIntervalCheck.IsChecked = Draft.OverrideRefreshInterval;
        RefreshIntervalBox.Value = Draft.RefreshIntervalMinutes;
        InheritedRefreshIntervalText.Text = Draft.InheritedRefreshIntervalLabel;

        OverrideAutoDownloadCheck.IsChecked = Draft.OverrideAutoDownload;
        AutoDownloadCheck.IsChecked = Draft.AutoDownload;
        InheritedAutoDownloadText.Text = Draft.InheritedAutoDownloadLabel;

        OverrideFetchFullTextCheck.IsChecked = Draft.OverrideFetchFullText;
        FetchFullTextCheck.IsChecked = Draft.FetchFullText;
        InheritedFetchFullTextText.Text = Draft.InheritedFetchFullTextLabel;

        OverrideRetentionCheck.IsChecked = Draft.OverrideRetention;
        RetentionDaysBox.Value = Draft.RetentionDays;
        InheritedRetentionText.Text = Draft.InheritedRetentionLabel;

        UpdateGating();
    }

    public FeedSettingsDraft Draft { get; }

    /// <summary>Null when the dialog was cancelled.</summary>
    public Feed? Result { get; private set; }

    /// <summary>
    /// The editor beneath each override switch is only meaningful while the
    /// switch is on; disabling it while off makes that visible rather than
    /// leaving a live-looking control whose value is about to be discarded.
    /// </summary>
    private void OnOverrideChanged(object? sender, RoutedEventArgs e) => UpdateGating();

    private void UpdateGating()
    {
        RefreshIntervalBox.IsEnabled = OverrideRefreshIntervalCheck.IsChecked ?? false;
        AutoDownloadCheck.IsEnabled = OverrideAutoDownloadCheck.IsChecked ?? false;
        FetchFullTextCheck.IsEnabled = OverrideFetchFullTextCheck.IsChecked ?? false;
        RetentionDaysBox.IsEnabled = OverrideRetentionCheck.IsChecked ?? false;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Draft.TitleOverride = TitleOverrideBox.Text ?? string.Empty;
        Draft.FolderId = (FolderCombo.SelectedItem as FolderOption)?.Id;
        Draft.IsEnabled = EnabledCheck.IsChecked ?? Draft.IsEnabled;

        Draft.OverrideRefreshInterval = OverrideRefreshIntervalCheck.IsChecked ?? false;
        Draft.RefreshIntervalMinutes = (int)(RefreshIntervalBox.Value ?? Draft.RefreshIntervalMinutes);

        Draft.OverrideAutoDownload = OverrideAutoDownloadCheck.IsChecked ?? false;
        Draft.AutoDownload = AutoDownloadCheck.IsChecked ?? Draft.AutoDownload;

        Draft.OverrideFetchFullText = OverrideFetchFullTextCheck.IsChecked ?? false;
        Draft.FetchFullText = FetchFullTextCheck.IsChecked ?? Draft.FetchFullText;

        Draft.OverrideRetention = OverrideRetentionCheck.IsChecked ?? false;
        Draft.RetentionDays = (int)(RetentionDaysBox.Value ?? Draft.RetentionDays);

        Result = Draft.Apply();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private sealed record FolderOption(long? Id, string Name);
}
