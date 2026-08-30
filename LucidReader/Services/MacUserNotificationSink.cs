using System.Runtime.InteropServices;

namespace LucidReader.Services;

/// <summary>
/// Posts a real macOS notification through Foundation's
/// NSUserNotificationCenter, reached by talking to the Objective-C runtime
/// directly.
///
/// Why this shape rather than any of the alternatives:
///
/// - Avalonia has no system-notification API. Its notification manager draws
///   inside the window, which is not the same thing and is no use at all when
///   the window is the thing the user cannot see.
/// - Shelling out to osascript is not an option here even if it were a good
///   idea: Process.Start is confined to SafeLinkOpener in this codebase, and
///   spawning an interpreter to draw a banner would be a poor trade anyway.
/// - A third-party notification package would be a dependency shipped in
///   Release for one line of text.
///
/// So: four P/Invokes into libobjc, no package, nothing added to the Release
/// build but this file.
///
/// THE LIMIT, stated plainly because it decides what most people will
/// actually get. macOS refuses to deliver a notification for a process with
/// no bundle identifier, which means a bare binary run from bin/ - every
/// development run, every UI test run - cannot post one no matter what is
/// written here. <see cref="IsAvailable"/> is that check, and it is why it is
/// a bundle test rather than an operating-system test. In a packaged mylo.app
/// this route is live; running the binary directly it is not, and
/// <see cref="SystemNotifier"/> falls back to the in-window route. That
/// fallback is not a nicety: without it, development builds would appear to
/// have working notifications right up until someone looked for one.
///
/// NSUserNotification is deprecated in favour of UNUserNotificationCenter,
/// which additionally requires a signed bundle and an authorization prompt.
/// The deprecated API still delivers, needs no prompt, and its requirements
/// are the ones a plain packaged app can already meet, so it is what is used
/// here.
/// </summary>
public sealed class MacUserNotificationSink : ISystemNotifier
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string Foundation =
        "/System/Library/Frameworks/Foundation.framework/Foundation";

    [DllImport(LibObjC, EntryPoint = "objc_getClass", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetClass(string name);

    [DllImport(LibObjC, EntryPoint = "sel_registerName", CharSet = CharSet.Ansi)]
    private static extern IntPtr Selector(string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend", CharSet = CharSet.Ansi)]
    private static extern IntPtr SendUtf8(IntPtr receiver, IntPtr selector, string argument);

    private readonly bool _available;

    public MacUserNotificationSink()
    {
        _available = OperatingSystem.IsMacOS() && Probe();
    }

    public bool IsAvailable => _available;

    public string Route => "macOS notification centre";

    public void Post(string title, string body)
    {
        if (!_available) return;

        try
        {
            var center = Send(
                GetClass("NSUserNotificationCenter"),
                Selector("defaultUserNotificationCenter"));

            if (center == IntPtr.Zero) return;

            var notification = Send(Send(GetClass("NSUserNotification"), Selector("alloc")),
                Selector("init"));
            if (notification == IntPtr.Zero) return;

            try
            {
                Send(notification, Selector("setTitle:"), NsString(title));
                Send(notification, Selector("setInformativeText:"), NsString(body));
                Send(center, Selector("deliverNotification:"), notification);
            }
            finally
            {
                // alloc/init means this object is owned here. The two NSString
                // arguments are autoreleased by stringWithUTF8String: and are
                // deliberately not released.
                Send(notification, Selector("release"));
            }
        }
        catch (Exception)
        {
            // A notification is never worth taking the app down for, and this
            // is the one place in the app reaching outside managed code.
        }
    }

    /// <summary>
    /// True when this process can actually deliver: Foundation loads, the
    /// classes resolve, and the process has a bundle identifier.
    /// </summary>
    private static bool Probe()
    {
        try
        {
            // Avalonia's own native library has already pulled Foundation in
            // by the time this runs, but loading it explicitly means this
            // class does not depend on that having happened.
            if (!NativeLibrary.TryLoad(Foundation, out _)) return false;

            if (GetClass("NSUserNotificationCenter") == IntPtr.Zero) return false;
            if (GetClass("NSUserNotification") == IntPtr.Zero) return false;

            var bundleClass = GetClass("NSBundle");
            if (bundleClass == IntPtr.Zero) return false;

            var mainBundle = Send(bundleClass, Selector("mainBundle"));
            if (mainBundle == IntPtr.Zero) return false;

            // nil here is an unbundled process, and an unbundled process
            // cannot post. See the class remarks.
            return Send(mainBundle, Selector("bundleIdentifier")) != IntPtr.Zero;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IntPtr NsString(string value) =>
        SendUtf8(GetClass("NSString"), Selector("stringWithUTF8String:"), value);
}
