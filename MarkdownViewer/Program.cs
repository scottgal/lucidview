using Avalonia;
using Avalonia.Data.Core.Plugins;

namespace MarkdownViewer;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        // Disable ALL data validators to avoid IBinding errors from Markdown.Avalonia's StaticBinding
        BindingPlugins.DataValidators.Clear();

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}