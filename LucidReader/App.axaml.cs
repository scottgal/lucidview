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
            Services = Task.Run(() => ReaderServices.StartAsync()).GetAwaiter().GetResult();
            desktop.MainWindow = new MainWindow(Services);
            desktop.ShutdownRequested += async (_, _) =>
            {
                if (Services is not null) await Services.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
