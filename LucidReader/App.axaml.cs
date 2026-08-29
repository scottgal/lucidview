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
            Services = ReaderServices.StartAsync().GetAwaiter().GetResult();
            desktop.MainWindow = new MainWindow(Services);
            desktop.ShutdownRequested += async (_, _) =>
            {
                if (Services is not null) await Services.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
