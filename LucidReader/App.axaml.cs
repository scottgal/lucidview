using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LucidReader.Views;

namespace LucidReader;

public class App : Application
{
    public ReaderServices? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Task.Run, not a bare GetResult: this runs on the UI thread with
            // AvaloniaSynchronizationContext current, so every await inside
            // StartAsync would post its continuation back to the thread we are
            // blocking. Running it on the pool lets those continuations resume
            // off-thread. Nothing under StartAsync touches the Dispatcher.
            var (databasePath, settingsPath) = ResolveDataPaths();
            Services = Task.Run(() => ReaderServices.StartAsync(databasePath, settingsPath))
                .GetAwaiter().GetResult();
            desktop.MainWindow = new MainWindow(Services);
            desktop.ShutdownRequested += async (_, _) =>
            {
                if (Services is not null) await Services.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Normally null/null, which makes ReaderServices use ReaderPaths' defaults
    /// under the platform application-data folder.
    ///
    /// In a Debug build only, LUCIDREADER_DATA_DIR redirects both the database
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
        var dir = Environment.GetEnvironmentVariable("LUCIDREADER_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            return (Path.Combine(dir, "reader.db"), Path.Combine(dir, "settings.json"));
        }
#endif
        return (null, null);
    }
}
