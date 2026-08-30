using Avalonia.Controls;

namespace LucidReader.Services;

/// <summary>
/// Picks the best notification route the running build can actually use, and
/// says which one that was.
///
/// The ordering is the only interesting part, and it is capability-based
/// rather than platform-based on purpose. "Is this macOS" is the wrong
/// question: a macOS build run straight out of bin/ cannot post a system
/// notification, and a test that branched on the operating system would
/// claim it could. What is asked instead is whether the native route reports
/// itself usable, and if it does not, the in-window route is used.
/// </summary>
public static class SystemNotifier
{
    /// <summary>
    /// Builds the notifier for this process. <paramref name="onClicked"/> is
    /// what the in-window route runs when the toast is clicked; the native
    /// macOS route cannot deliver a click without an NSUserNotificationCenter
    /// delegate, so on that route the status item is the way back to the
    /// window and the argument is unused.
    /// </summary>
    public static ISystemNotifier Create(Window host, Action? onClicked = null)
    {
        if (OperatingSystem.IsMacOS())
        {
            var native = new MacUserNotificationSink();
            if (native.IsAvailable) return native;
        }

        return new InAppNotificationSink(host, onClicked);
    }
}
