using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;

namespace LucidReader.Services;

/// <summary>
/// The fallback route: a toast drawn inside the mylo window, using Avalonia's
/// own WindowNotificationManager.
///
/// It is honestly weaker than a system notification and it is not pretended
/// otherwise. A toast inside a window the user cannot see is a toast the user
/// cannot see. It exists because every platform has to do something, and
/// there is one platform route in this app
/// (<see cref="MacUserNotificationSink"/>) that only works from a packaged
/// bundle. What carries the signal on the platforms and builds where this is
/// what is left is the status item: its unread count is visible whether or
/// not the window is, which is the whole reason the two features arrived
/// together.
///
/// Windows and Linux land here as well. Avalonia exposes no system
/// notification on either, and the alternatives are a WinRT toast (which
/// needs a registered AppUserModelID and a packaged identity) and a D-Bus
/// org.freedesktop.Notifications call (which needs a D-Bus dependency and a
/// running notification daemon). Neither is a small addition, and neither is
/// something this codebase can verify from here, so neither is claimed.
///
/// The click handler is what makes this more than decoration: it brings the
/// window forward and switches the list to unread, which is the one thing
/// somebody who just read "12 new articles" wants next.
/// </summary>
public sealed class InAppNotificationSink : ISystemNotifier
{
    private readonly WindowNotificationManager? _manager;
    private readonly Action? _onClicked;

    public InAppNotificationSink(Window host, Action? onClicked = null)
    {
        _onClicked = onClicked;

        try
        {
            _manager = new WindowNotificationManager(host)
            {
                Position = NotificationPosition.TopRight,
                MaxItems = 3
            };
        }
        catch (Exception)
        {
            // A host with no TopLevel yet, or a platform that will not give
            // one. IsAvailable then reports false rather than every Post
            // throwing.
        }
    }

    public bool IsAvailable => _manager is not null;

    public string Route => "mylo window";

    public void Post(string title, string body)
    {
        if (_manager is null) return;

        try
        {
            // Show has to run on the UI thread, and the caller is a refresh
            // completion, which does not. Posting rather than invoking: this
            // must never block whatever finished the refresh.
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    _manager.Show(new Notification(
                        title, body, NotificationType.Information,
                        TimeSpan.FromSeconds(8), _onClicked));
                }
                catch (Exception)
                {
                }
            });
        }
        catch (Exception)
        {
        }
    }
}
