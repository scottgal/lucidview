using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using LucidReader.Core.Notifications;

namespace LucidReader.Services;

/// <summary>
/// What the status item can be asked to do. A record of four callbacks rather
/// than a reference to the window, so this class never learns anything about
/// the shell and can be constructed in isolation.
/// </summary>
public sealed record StatusItemActions(
    Action Open,
    Action RefreshAll,
    Action MarkAllRead,
    Action Quit);

/// <summary>
/// The menu-bar item on macOS, the tray icon on Windows and Linux.
///
/// One class for all three because Avalonia's TrayIcon already is the
/// abstraction: it is an NSStatusItem on macOS, a shell notification-area
/// icon on Windows, and a StatusNotifierItem or a legacy tray icon on Linux.
/// The icons are attached to the Application rather than to a window, which
/// is not a style choice - a tray icon belongs to the process, and one
/// attached to a window would go when the window is hidden, which is the one
/// moment it is most needed.
///
/// What degrades, and where:
///
/// - macOS draws the icon in the menu bar, and Avalonia does not mark it as a
///   template image, so it does not invert itself with the menu bar. That is
///   handled here instead, by choosing between two monochrome assets; see
///   DarkGlyphUri and LightGlyphUri below.
/// - Windows scales the same asset into the notification area. The glyph is
///   drawn on a coarse grid for that reason: it survives 16x16.
/// - Linux depends entirely on the desktop. A session running a
///   StatusNotifierItem host shows it; a plain window manager with no tray at
///   all shows nothing, and there is nothing this code can do about that. The
///   unread count is also on the window title, so it is not the only place
///   the number appears.
/// - The unread count itself is text in the menu and in the tooltip, not a
///   badge drawn on the icon. Avalonia has no badge API, and rendering a
///   number into the image per change would mean regenerating a bitmap
///   whenever an article is read.
/// </summary>
public sealed class StatusItem : IDisposable
{
    /// <summary>
    /// Two assets, one glyph, and the reason is measured rather than assumed.
    ///
    /// A macOS menu-bar icon is supposed to be a template image: monochrome
    /// with an alpha channel, which AppKit then draws in whatever colour the
    /// menu bar currently wants. Avalonia builds its NSImage from raw bytes
    /// and does not mark it as one, and a full-screen capture of the menu bar
    /// with the system in dark appearance showed exactly that - every system
    /// item white, mylo's black and all but invisible.
    ///
    /// So the inversion is done here instead: the dark glyph for a light menu
    /// bar, the light glyph for a dark one, chosen from the application's own
    /// theme variant and swapped when it changes. The two files are the same
    /// artwork; only the colour differs.
    /// </summary>
    private const string DarkGlyphUri = "avares://mylo/Assets/status-item.png";
    private const string LightGlyphUri = "avares://mylo/Assets/status-item-light.png";

    private readonly Application _application;
    private readonly TrayIcon? _icon;
    private readonly NativeMenuItem? _unreadItem;
    private readonly EventHandler? _onThemeVariantChanged;
    private int _disposed;

    public StatusItem(Application application, StatusItemActions actions)
    {
        _application = application;

        try
        {
            _icon = new TrayIcon
            {
                Icon = LoadIcon(application),
                ToolTipText = NotificationPolicy.DescribeUnread(0),
                IsVisible = false
            };

            _unreadItem = new NativeMenuItem(NotificationPolicy.DescribeUnread(0))
            {
                IsEnabled = false
            };

            var open = new NativeMenuItem("Open mylo");
            open.Click += (_, _) => Run(actions.Open);

            var refresh = new NativeMenuItem("Refresh all");
            refresh.Click += (_, _) => Run(actions.RefreshAll);

            var markRead = new NativeMenuItem("Mark all read");
            markRead.Click += (_, _) => Run(actions.MarkAllRead);

            var quit = new NativeMenuItem("Quit mylo");
            quit.Click += (_, _) => Run(actions.Quit);

            var menu = new NativeMenu();
            menu.Add(_unreadItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(open);
            menu.Add(refresh);
            menu.Add(markRead);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(quit);

            _icon.Menu = menu;

            TrayIcon.SetIcons(_application, new TrayIcons { _icon });

            // The menu bar follows the system appearance and so does this.
            // Without it a machine that switches to dark at sunset ends up
            // with a black glyph on a black menu bar until the next launch.
            _onThemeVariantChanged = (_, _) => ApplyThemeAppropriateIcon();
            _application.ActualThemeVariantChanged += _onThemeVariantChanged;
        }
        catch (Exception ex)
        {
            // A desktop with no tray at all, or an asset that failed to load.
            // The app must still open; it simply has no status item.
            Console.Error.WriteLine($"[StatusItem] {ex.GetType().Name}: {ex.Message}");
            _icon = null;
        }
    }

    /// <summary>
    /// True when the platform actually gave us an item. Everything else here
    /// is a no-op when it is false, so callers do not have to check.
    /// </summary>
    public bool IsSupported => _icon is not null;

    public bool IsVisible
    {
        get => _icon?.IsVisible ?? false;
        set { if (_icon is not null) _icon.IsVisible = value; }
    }

    /// <summary>
    /// Updates the unread count shown in the tooltip and at the top of the
    /// menu. Safe to call from any thread; the tray icon is a UI object.
    /// </summary>
    public void SetUnreadCount(int unreadCount)
    {
        if (_icon is null) return;

        var text = NotificationPolicy.DescribeUnread(unreadCount);

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _icon.ToolTipText = text;
                if (_unreadItem is not null) _unreadItem.Header = text;
            }
            catch (Exception)
            {
                // A tray host that has gone away underneath us. Nothing here
                // is worth an exception on the dispatcher.
            }
        });
    }

    /// <summary>
    /// Runs a menu action without letting anything it throws reach the
    /// platform's menu callback, where there is no handler at all and the
    /// process would go.
    /// </summary>
    private static void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { Console.Error.WriteLine($"[StatusItem] {ex.GetType().Name}: {ex.Message}"); }
    }

    private void ApplyThemeAppropriateIcon()
    {
        if (_icon is null) return;

        try { _icon.Icon = LoadIcon(_application); }
        catch (Exception)
        {
            // Keeping the icon it already has is a better outcome than an
            // exception on the dispatcher from a theme change.
        }
    }

    private static WindowIcon LoadIcon(Application application)
    {
        var uri = application.ActualThemeVariant == ThemeVariant.Dark
            ? LightGlyphUri
            : DarkGlyphUri;

        using var stream = AssetLoader.Open(new Uri(uri));
        return new WindowIcon(stream);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_icon is null) return;

        try
        {
            if (_onThemeVariantChanged is not null)
                _application.ActualThemeVariantChanged -= _onThemeVariantChanged;

            _icon.IsVisible = false;

            // Detached from the Application as well as hidden. A TrayIcon left
            // in the attached collection is kept alive by it, and on the quit
            // path that is the difference between the menu-bar item going when
            // the app does and lingering until the platform notices.
            TrayIcon.SetIcons(_application, new TrayIcons());
            _icon.Dispose();
        }
        catch (Exception)
        {
        }
    }
}
