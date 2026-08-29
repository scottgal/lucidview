using LucidReader.Core.Storage;
using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// The feed tree's context menu actions (Task 12): rename, move to folder,
/// mark all read, feed settings, unsubscribe. Rename and move-to-folder are
/// both expressed through the same feed-settings dialog rather than
/// separate one-off flows - ShowFeedSettingsAsync already carries a title
/// box and a folder picker - except rename also offers a fast standalone
/// path via InputDialog, matching how EditTagsAsync (MainWindow.Actions.cs)
/// already uses InputDialog for a single quick edit.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Every feed-tree context-menu item (MainWindow.axaml) is wired as a
    /// plain Click handler rather than an ICommand binding. A ContextMenu
    /// opens in a Popup, outside the ItemsControl's visual tree, so a
    /// $parent-based RelativeSource binding back up to the window's own
    /// DataContext (the way the toolbar buttons reach OpenSettingsCommand
    /// etc.) does not reach reliably; DataContext itself still flows down
    /// the logical tree from the row, though, so a Click handler can always
    /// recover the row's FeedTreeNode from sender.DataContext, matching how
    /// OnRowMarkReadClicked and friends already do it in MainWindow.Actions.cs.
    /// </summary>
    private static FeedTreeNode? NodeFromSender(object? sender) =>
        (sender as Avalonia.StyledElement)?.DataContext as FeedTreeNode;

    private async void OnRefreshFeedClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (NodeFromSender(sender)?.FeedId is not { } feedId) return;

        StatusMessage = "Refreshing...";
        var outcome = await _services.Refresh.RefreshNowAsync(feedId);
        await AfterRefreshAsync(outcome.Success
            ? outcome.NotModified ? "No changes." : $"{outcome.NewItemCount} new articles."
            : "Refresh failed: " + outcome.Error);
    }

    private async void OnRenameFeedClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (NodeFromSender(sender)?.FeedId is { } feedId) await RenameFeedAsync(feedId);
    }

    private async void OnMarkFeedReadClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (NodeFromSender(sender)?.FeedId is { } feedId) await MarkFeedReadAsync(feedId);
    }

    private async void OnFeedSettingsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (NodeFromSender(sender)?.FeedId is { } feedId) await ShowFeedSettingsAsync(feedId);
    }

    private async void OnUnsubscribeFeedClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (NodeFromSender(sender)?.FeedId is { } feedId) await UnsubscribeAsync(feedId);
    }

    /// <summary>
    /// Opens the full per-feed settings dialog. Enabling has to go through
    /// SetEnabledAsync: it is the only path that also clears
    /// consecutive_failures, last_error and auto_paused_utc, so a feed the
    /// user just re-enabled gets a clean slate rather than being auto-paused
    /// again on its very first subsequent failure. UpdateAsync deliberately
    /// never touches those columns, so it cannot be used for this half of
    /// the save.
    /// </summary>
    public async Task ShowFeedSettingsAsync(long feedId)
    {
        var feed = await _services.Feeds.GetAsync(feedId);
        if (feed is null) return;

        var folders = await _services.Folders.GetAllAsync();
        var dialog = new FeedSettingsDialog(feed, _services.Settings, folders);
        await dialog.ShowDialog(this);

        if (dialog.Result is not { } updated) return;

        if (updated.IsEnabled != feed.IsEnabled)
            await _services.Feeds.SetEnabledAsync(feedId, updated.IsEnabled);

        // UpdateAsync writes folder, title override, icon, is_enabled and the
        // four overridable columns, but never the fetch bookkeeping columns
        // (consecutive_failures, last_error, auto_paused_utc) - that's
        // exactly why SetEnabledAsync(true) above still had to run first.
        await _services.Feeds.UpdateAsync(updated);
        await LoadFeedTreeAsync();
        StatusMessage = "Feed settings saved.";
    }

    /// <summary>
    /// A fast rename that does not require opening the full settings
    /// dialog, the way Mail lets you rename a mailbox in place. Writes
    /// through UpdateTitleAndSiteUrlAsync so the feed's discovered site URL
    /// (used for favicon resolution) survives the rename untouched.
    /// </summary>
    public async Task RenameFeedAsync(long feedId)
    {
        var feed = await _services.Feeds.GetAsync(feedId);
        if (feed is null) return;

        var dialog = new InputDialog("Rename Feed", "New name for this feed", feed.DisplayTitle);
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } entered) return;

        // An empty rename means "go back to the feed's own title", not "set
        // an empty title" - the same null-means-inherit idea as the
        // settings dialog's overrides, just for this one field.
        var trimmed = entered.Trim();
        await _services.Feeds.UpdateTitleAndSiteUrlAsync(
            feedId,
            string.IsNullOrEmpty(trimmed) ? feed.Title : trimmed,
            feed.SiteUrl);

        await LoadFeedTreeAsync();
        StatusMessage = "Feed renamed.";
    }

    public async Task MarkFeedReadAsync(long feedId)
    {
        await _services.Items.MarkFeedReadAsync(feedId);
        await LoadFeedTreeAsync();
        await LoadItemsAsync();
    }

    /// <summary>
    /// Unsubscribing cascades: deleting a feed deletes its items and their
    /// tombstones, and it cannot be undone. So this confirms first, naming
    /// the feed and how many stored articles go with it - a silent bulk
    /// delete is not something a mail-style app should ever do.
    /// </summary>
    public async Task UnsubscribeAsync(long feedId)
    {
        var feed = await _services.Feeds.GetAsync(feedId);
        if (feed is null) return;

        var items = await _services.Items.QueryAsync(
            new ItemQuery(feedId, null, ItemFilter.All, 10000, 0));

        var confirm = new ConfirmDialog(
            "Unsubscribe",
            $"Remove \"{feed.DisplayTitle}\" and its {items.Count} stored " +
            (items.Count == 1 ? "article" : "articles") + "? This cannot be undone.",
            "Unsubscribe");
        await confirm.ShowDialog(this);
        if (!confirm.Confirmed) return;

        await _services.Feeds.DeleteAsync(feedId);
        await LoadFeedTreeAsync();
        await LoadItemsAsync();
        StatusMessage = $"Unsubscribed from {feed.DisplayTitle}.";
    }
}
