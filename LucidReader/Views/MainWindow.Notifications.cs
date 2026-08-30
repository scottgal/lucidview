using Avalonia.Controls;
using Avalonia.Threading;
using LucidReader.Core.Notifications;
using LucidReader.Core.Storage;
using LucidReader.Core.Sync;
using LucidReader.Models;
using LucidReader.Services;

namespace LucidReader.Views;

/// <summary>
/// Telling the user that articles arrived, and keeping the status item's
/// unread count honest.
///
/// The shape of the problem: FeedRefreshService.Completed fires once per feed,
/// on the coordinator's thread, seconds apart across one sweep. Posting from
/// there directly would mean forty banners for one scheduler tick, from the
/// wrong thread, whether or not the user was looking at the window at the
/// time. So each completion is folded into a
/// <see cref="NewArticleAccumulator"/> on the UI thread, a slow timer asks
/// once a second whether the sweep has gone quiet, and only a settled sweep
/// is announced. <see cref="NotificationPolicy"/> owns both the decision and
/// the wording, and neither needs a window to be tested.
///
/// Nothing here posts anything by itself. Where a notification actually goes
/// is <see cref="ISystemNotifier"/>'s business, and on a development build it
/// goes into the window rather than the menu bar - see
/// <see cref="MacUserNotificationSink"/> for exactly why.
/// </summary>
public partial class MainWindow
{
    private readonly NewArticleAccumulator _newArticles = new();

    /// <summary>
    /// Held as a field so the same delegate instance can be unsubscribed.
    /// Assigned in the constructor rather than here: a field initializer
    /// cannot name an instance method, and a fresh
    /// <c>OnRefreshCompletedForNotifications</c> method group at the -= would
    /// be a different delegate that removes nothing, leaving the window
    /// subscribed to a service that outlives it.
    /// </summary>
    private Action<FeedRefreshOutcome>? _onRefreshCompletedForNotifications;

    private DispatcherTimer? _notificationTimer;
    private ISystemNotifier? _notifier;
    private StatusItem? _statusItem;
    private bool _notificationFailureReported;

    /// <summary>
    /// How often the settled-sweep question is asked. Deliberately far
    /// shorter than the quiet period it is testing, so the notification
    /// arrives within about a second of the sweep actually finishing rather
    /// than up to a whole poll interval later.
    /// </summary>
    private static readonly TimeSpan NotificationPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Called once from OnOpenedAsync, after the first tree load, so the
    /// first unread count the status item shows is a real one.
    /// </summary>
    private void StartNotifications()
    {
        _notifier ??= SystemNotifier.Create(this, ShowUnreadFromNotification);

        _onRefreshCompletedForNotifications ??= OnRefreshCompletedForNotifications;
        _services.Refresh.Completed += _onRefreshCompletedForNotifications;

        _notificationTimer = new DispatcherTimer { Interval = NotificationPollInterval };
        _notificationTimer.Tick += OnNotificationTimerTick;
        _notificationTimer.Start();
    }

    private void StopNotifications()
    {
        if (_onRefreshCompletedForNotifications is not null)
            _services.Refresh.Completed -= _onRefreshCompletedForNotifications;

        if (_notificationTimer is null) return;

        _notificationTimer.Stop();
        _notificationTimer.Tick -= OnNotificationTimerTick;
        _notificationTimer = null;
    }

    /// <summary>
    /// The status item is created by App.axaml.cs, which owns it because a
    /// tray icon belongs to the application rather than to a window, and is
    /// handed here so the unread count can be kept current. Null when the
    /// platform gave us no status item, or when the setting has it off.
    /// </summary>
    public void AttachStatusItem(StatusItem? statusItem)
    {
        _statusItem = statusItem;
        UpdateStatusItemUnreadCount();
    }

    /// <summary>
    /// The unread total the sidebar's Unread row already knows. Read from
    /// there rather than counted again out of the database: the sidebar is
    /// rebuilt on every action that can change it, and a second count would
    /// only be another thing to keep in step.
    /// </summary>
    public int TotalUnreadCount =>
        AllFeedTreeNodes.FirstOrDefault(n =>
            n.Kind == FeedTreeNodeKind.Smart && n.SmartFilter == ItemFilter.Unread)?.UnreadCount ?? 0;

    internal void UpdateStatusItemUnreadCount() => _statusItem?.SetUnreadCount(TotalUnreadCount);

    /// <summary>
    /// True when there is a status item the user could actually get the
    /// window back from. What "keep running when the window closes" is gated
    /// on, because without it that setting produces an app with no window and
    /// no way to reach one.
    /// </summary>
    internal bool HasUsableStatusItem => _statusItem is { IsSupported: true, IsVisible: true };

    /// <summary>
    /// Runs on the refresh coordinator's thread. Does nothing but hand the
    /// numbers to the UI thread: the accumulator is not thread safe and is
    /// touched from nowhere else.
    /// </summary>
    private void OnRefreshCompletedForNotifications(FeedRefreshOutcome outcome)
    {
        if (!outcome.Success || outcome.NewItemCount <= 0) return;

        var feedId = outcome.FeedId;
        var count = outcome.NewItemCount;

        Dispatcher.UIThread.Post(() =>
            _newArticles.Add(feedId, count, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// A DispatcherTimer Tick is a void-returning event, so an exception past
    /// it reaches the dispatcher unhandled and takes the process with it. A
    /// notification is never worth that; the first failure is written out so
    /// a route that is silently broken is still diagnosable.
    /// </summary>
    private async void OnNotificationTimerTick(object? sender, EventArgs e)
    {
        try
        {
            var sweep = _newArticles.TakeIfSettled(DateTimeOffset.UtcNow);
            if (!sweep.HasArticles) return;

            // The sweep brought articles in, so the counts on screen are
            // stale regardless of whether anything is posted. Reloading the
            // tree here is what keeps the status item's unread count true
            // while the window is hidden and nothing else is reloading it.
            await LoadFeedTreeAsync();
            UpdateStatusItemUnreadCount();

            if (!NotificationPolicy.ShouldNotify(_services.Settings, IsActive, sweep)) return;

            _notifier?.Post("mylo", NotificationPolicy.Describe(sweep));
        }
        catch (Exception ex)
        {
            if (!_notificationFailureReported)
            {
                _notificationFailureReported = true;
                Console.Error.WriteLine($"[Notify] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// What a clicked notification does: bring the window forward and show
    /// the unread articles, which is the one thing somebody who just read
    /// "12 new articles" wants next.
    /// </summary>
    private void ShowUnreadFromNotification()
    {
        try
        {
            ShowFromStatusItem();

            var unreadRow = AllFeedTreeNodes.FirstOrDefault(n =>
                n.Kind == FeedTreeNodeKind.Smart && n.SmartFilter == ItemFilter.Unread);

            if (unreadRow is not null) SelectedFeedNode = unreadRow;
            IsFilterUnread = true;
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not open the unread articles: " + ex.Message;
        }
    }

    // ---------------------------------------------------------------------
    // The status item's four menu entries. Public because App.axaml.cs binds
    // them into the tray menu, and each one has to work with the window
    // hidden, which is the case none of the toolbar's own handlers cover.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Brings the window back from hidden and focuses it. Show() on an
    /// already-visible window is harmless; Activate() is what actually raises
    /// it, and on macOS is what pulls the app out from behind whatever the
    /// user has been doing instead.
    /// </summary>
    public void ShowFromStatusItem()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (!IsVisible) Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[StatusItem] {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    public void RefreshAllFromStatusItem() =>
        Dispatcher.UIThread.Post(() => RefreshAllCommand.Execute(null));

    /// <summary>
    /// Every feed, not the selected one. See
    /// ItemRepository.MarkAllReadAsync for why the status item cannot mean
    /// the same thing the Feed menu's Mark All Read means.
    /// </summary>
    public void MarkAllReadFromStatusItem() =>
        Dispatcher.UIThread.Post(() =>
            _ = RunGuardedAsync(MarkEverythingReadAsync, "mark everything read"));

    public async Task MarkEverythingReadAsync()
    {
        var changed = await _services.Items.MarkAllReadAsync();

        await LoadFeedTreeAsync();
        await LoadItemsAsync();
        UpdateStatusItemUnreadCount();

        StatusMessage = changed == 1
            ? "1 article marked read."
            : $"{changed} articles marked read.";
    }
}
