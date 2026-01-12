using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using MarkdownViewer.Views;
using System.Diagnostics;

namespace MarkdownViewer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Log binding errors to console
        Avalonia.Logging.Logger.Sink = new ConsoleLogSink();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Disable Avalonia's data validation to avoid conflicts with custom IBinding
        if (BindingPlugins.DataValidators.Count > 0)
            BindingPlugins.DataValidators.RemoveAt(0);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private class ConsoleLogSink : Avalonia.Logging.ILogSink
    {
        public bool IsEnabled(Avalonia.Logging.LogEventLevel level, string area)
        {
            return level >= Avalonia.Logging.LogEventLevel.Warning && area == "Binding";
        }

        public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate)
        {
            Console.WriteLine($"[{level}] {area}: {messageTemplate} | Source: {source?.GetType().Name ?? "null"}");
        }

        public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
        {
            try
            {
                // Build message with property values
                var values = propertyValues?.Select(v => v?.ToString() ?? "null").ToArray() ?? Array.Empty<string>();
                Console.WriteLine($"[{level}] {area}: Values=[{string.Join(", ", values)}] | Source: {source?.GetType().Name ?? "null"}");
            }
            catch
            {
                Console.WriteLine($"[{level}] {area}: {messageTemplate}");
            }
        }
    }

    public void SetTheme(ThemeVariant theme)
    {
        RequestedThemeVariant = theme;
    }

    public void ToggleTheme()
    {
        RequestedThemeVariant = ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }
}
