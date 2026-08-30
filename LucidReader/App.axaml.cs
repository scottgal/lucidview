using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using LucidReader.Core.Storage;
using LucidReader.Services;
using LucidReader.Views;

namespace LucidReader;

public class App : Application
{
    private ReaderServices? _services;

    public ReaderServices? Services => _services;

    /// <summary>
    /// Bounds how long shutdown will block waiting for the engine to close.
    /// Not optional: ReaderServices.DisposeAsync reaches
    /// SqliteSingleWriter.DisposeAsync, whose DrainAsync is unbounded, and
    /// the moment the handler blocks on it a wedged writer would hang the
    /// quit forever rather than merely losing the drain.
    /// </summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            InstallGlobalExceptionHooks();

#if DEBUG
            // Debug-only, and off unless MYLO_MEMORY_LOG names a file. This is
            // what the long-running soak in ux-scripts/run-memory-soak.sh reads
            // to say whether the trend is flat, rather than the run being
            // described from impressions.
            _memorySampler = MemorySampler.StartIfRequested();
#endif

            // Task.Run, not a bare GetResult: this runs on the UI thread with
            // AvaloniaSynchronizationContext current, so every await inside
            // StartAsync would post its continuation back to the thread we are
            // blocking. Running it on the pool lets those continuations resume
            // off-thread. Nothing under StartAsync touches the Dispatcher.
            var (databasePath, settingsPath) = ResolveDataPaths();

            try
            {
                _services = Task.Run(() => ReaderServices.StartAsync(databasePath, settingsPath))
                    .GetAwaiter().GetResult();

                // The one place the starter subscriptions can be written: this
                // is the application launching, which is the only event that
                // "first run" can possibly mean. ReaderServices.StartAsync
                // deliberately does not do it, because a unit test opening an
                // engine over a temporary directory would otherwise be handed
                // five real feeds. Task.Run for the same reason as above.
                //
                // Wrapped, because a failure to seed is not a reason the app
                // cannot open. The user gets an empty reader, which is exactly
                // what they had before this existed.
                try
                {
                    Task.Run(() => _services.SeedDefaultFeedsIfFirstRunAsync()).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Seed] {ex.GetType().Name}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                // SchemaMigrator composes a user-facing sentence for the cases
                // that land here - a database written by a newer build, a
                // corrupt file, a second instance holding the same path - and
                // until now nobody ever saw it: the exception propagated
                // unhandled out of OnFrameworkInitializationCompleted and the
                // process died with a stack trace. Show it and exit cleanly.
                ShowStartupFailure(desktop, ex);

                // Falls through to base.OnFrameworkInitializationCompleted
                // below rather than returning: the lifetime still has to be
                // told initialization finished, or the failure window it was
                // just given never gets a chance to be shown.
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var window = new MainWindow(_services);
            desktop.MainWindow = window;

            InstallStatusItem(desktop, window);

            // ShutdownMode is deliberately left at its default,
            // OnLastWindowClose, and that is what makes both behaviours work
            // without a second quit path.
            //
            // With "keep running in the menu bar" off, closing the window
            // closes it, it is the last one, and the app quits: exactly what
            // it did before any of this existed. With the setting on, the
            // window's Closing handler cancels the close and hides instead -
            // a hidden window has not closed, so the lifetime never counts it
            // and the app stays up. Switching to OnExplicitShutdown was tried
            // and is worse: it makes the ordinary close leave a running
            // process with no window at all unless the close handler itself
            // asks for a shutdown, which is a second route out of the app to
            // keep correct for no gain.

            // Synchronous and blocking on purpose. An async void handler
            // returns at its first await, and DoShutdown then closes the
            // windows and calls Dispatcher.UIThread.InvokeShutdown() in its
            // finally. Every await inside DisposeAsync posts its continuation
            // to that dispatcher, so the continuation was queued on a
            // dispatcher already torn down before it could be pumped: on a
            // normal quit the scheduler, the refresh service, the downloader,
            // the database, the HttpClient and the image cache were never
            // disposed at all. The WAL was never checkpointed, so -wal and
            // -shm grew across sessions, and writes still sitting in the
            // write coordinator's channel were dropped without a trace.
            desktop.ShutdownRequested += (_, _) =>
            {
                // Every quit route reaches here before any window closes:
                // Cmd+Q, the menu's Quit, the status item's Quit, an OS
                // logout, and the UI test harness's own Shutdown(0). Telling
                // the window first is what stops "keep running in the menu
                // bar" from cancelling the close that the shutdown in
                // progress depends on, which would otherwise make the app
                // unquittable by every one of those routes at once.
                try { window.MarkQuitting(); }
                catch (Exception ex) { Console.Error.WriteLine($"[Shutdown] {ex.GetType().Name}: {ex.Message}"); }

#if DEBUG
                try { _memorySampler?.Dispose(); }
                catch (Exception ex) { Console.Error.WriteLine($"[Shutdown] {ex.GetType().Name}: {ex.Message}"); }
#endif

                try { _statusItem?.Dispose(); }
                catch (Exception ex) { Console.Error.WriteLine($"[Shutdown] {ex.GetType().Name}: {ex.Message}"); }

                // Before disposal, not after. On Cmd+Q or an OS logout the
                // platform raises this BEFORE any window closes, so the
                // window's own Closing cleanup - cancelling the dwell,
                // stopping health monitoring, disposing the four
                // coordinators - would otherwise run after the engine had
                // already begun tearing down underneath it.
                try { window.PrepareForShutdown(); }
                catch (Exception ex) { Console.Error.WriteLine($"[Shutdown] {ex.GetType().Name}: {ex.Message}"); }

                var services = Interlocked.Exchange(ref _services, null);
                if (services is null) return;

                // Task.Run for the same reason as startup above: the awaits
                // inside DisposeAsync must not try to resume on the thread
                // this handler is blocking.
                try { Task.Run(async () => await services.DisposeAsync()).Wait(ShutdownTimeout); }
                catch (Exception ex) { Console.Error.WriteLine($"[Shutdown] {ex.GetType().Name}: {ex.Message}"); }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private StatusItem? _statusItem;

#if DEBUG
    private MemorySampler? _memorySampler;
#endif

    /// <summary>
    /// Creates the menu-bar item (macOS) or tray icon (Windows and Linux) and
    /// hands it to the window.
    ///
    /// It lives on the Application rather than on the window for the reason
    /// it has to: its whole purpose is to still be there when the window is
    /// not. The four menu entries are given as callbacks so
    /// <see cref="StatusItem"/> knows nothing about the shell, and every one
    /// of them works with the window hidden, which is the case none of the
    /// toolbar's own handlers cover.
    ///
    /// Visibility follows the setting, live: turning the status item off in
    /// the settings dialog hides it immediately rather than at the next
    /// launch, and turning it back on brings it back. Turning it off while
    /// "keep running in the menu bar" is on would strand the app with no
    /// window and no way back, so the window's own close handler checks for a
    /// usable status item rather than trusting the pair of settings to be
    /// consistent.
    /// </summary>
    private void InstallStatusItem(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window)
    {
        try
        {
            _statusItem = new StatusItem(this, new StatusItemActions(
                Open: window.ShowFromStatusItem,
                RefreshAll: window.RefreshAllFromStatusItem,
                MarkAllRead: window.MarkAllReadFromStatusItem,
                Quit: () => Dispatcher.UIThread.Post(() => desktop.Shutdown())));

            _statusItem.IsVisible = _services?.Settings.ShowStatusItem ?? true;
            window.AttachStatusItem(_statusItem);

            if (_services is not null)
                _services.SettingsChanged += settings =>
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_statusItem is not null) _statusItem.IsVisible = settings.ShowStatusItem;
                    });
        }
        catch (Exception ex)
        {
            // A desktop with no notification area at all. The reader opens
            // and works; it simply has no status item, and the window's close
            // handler will refuse to hide into one that is not there.
            Console.Error.WriteLine($"[StatusItem] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// A last line of defence, not a substitute for handling failures where
    /// they happen. Without these two hooks an unobserved task exception or a
    /// throw from a thread with no handler makes the app vanish with nothing
    /// written anywhere; with them there is at least a line to find.
    /// UnobservedTaskException is marked observed so a host that enables
    /// ThrowUnobservedTaskExceptions does not turn a lost background fetch
    /// into a process kill.
    /// </summary>
    private static void InstallGlobalExceptionHooks()
    {
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Console.Error.WriteLine($"[UnobservedTask] {e.Exception.GetType().Name}: {e.Exception.Message}");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var text = e.ExceptionObject is Exception ex
                ? $"{ex.GetType().Name}: {ex.Message}"
                : e.ExceptionObject.ToString();
            Console.Error.WriteLine($"[Unhandled] {text}");
        };
    }

    /// <summary>
    /// Shows why the reader could not open and then quits. A plain window
    /// rather than one of the Views dialogs: those are shown modally over a
    /// parent, and at this point there is no parent and no ReaderServices for
    /// one to sit over.
    /// </summary>
    private static void ShowStartupFailure(IClassicDesktopStyleApplicationLifetime desktop, Exception ex)
    {
        Console.Error.WriteLine($"[Startup] {ex.GetType().Name}: {ex.Message}");

        var message = new TextBlock
        {
            Text = ex.Message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20)
        };

        var quit = new Button
        {
            Content = "Quit",
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(20, 0, 20, 20)
        };

        var panel = new StackPanel();
        panel.Children.Add(message);
        panel.Children.Add(quit);

        var window = new Window
        {
            Title = "mylo could not start",
            Width = 480,
            Height = 200,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = panel
        };

        quit.Click += (_, _) => window.Close();
        window.Closed += (_, _) => desktop.Shutdown(1);

        // Assigned as MainWindow rather than shown here: the lifetime shows
        // it itself once initialization completes, and showing it before that
        // point puts a window up under a lifetime that has not started.
        desktop.MainWindow = window;
    }

    /// <summary>
    /// Normally null/null, which makes ReaderServices use ReaderPaths' defaults
    /// under the platform application-data folder.
    ///
    /// In a Debug build only, MYLO_DATA_DIR redirects both the database
    /// and settings.json to a directory the caller chooses. That exists for the
    /// UI test scripts in ux-scripts/: a script that has to assert on what is in
    /// the sidebar, the item list or the settings dialog needs a database whose
    /// contents it put there itself, and it must not decide those assertions by
    /// mutating the database a person is actually reading feeds from. The
    /// runner shell scripts point this at a temporary directory they seed and
    /// delete, so each script starts from a known state, can be run twice in a
    /// row, and leaves nothing behind.
    ///
    /// Debug-only, like the UI testing harness itself (see Program.cs): the
    /// Release build has no way to read anything but the real profile.
    /// </summary>
    private static (string? DatabasePath, string? SettingsPath) ResolveDataPaths()
    {
#if DEBUG
        var dir = Environment.GetEnvironmentVariable("MYLO_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            return (Path.Combine(dir, "reader.db"), Path.Combine(dir, "settings.json"));
        }
#endif
        MoveLegacyProfileIfNeeded();
        return (null, null);
    }

    /// <summary>
    /// The product was renamed from lucidREADER to mylo, and the profile
    /// directory under Application Data was renamed with it. Anyone who ran a
    /// build from before the rename has a database under the old name, and
    /// coming up silently empty beside it would look like data loss. So on the
    /// one case where the answer is unambiguous, an old directory and no new
    /// one, the directory is renamed. Nothing is copied, merged or deleted.
    ///
    /// Only reached for the real profile: a run pointed at a scratch directory
    /// by MYLO_DATA_DIR has already returned above.
    /// </summary>
    private static void MoveLegacyProfileIfNeeded()
    {
        var legacy = ReaderPaths.LegacyAppDataDirectory;
        var current = ReaderPaths.AppDataDirectory;

        var result = LegacyProfileMove.Apply(legacy, current);
        Console.WriteLine($"[Profile] {LegacyProfileMove.Describe(result, legacy, current)}");
    }
}
