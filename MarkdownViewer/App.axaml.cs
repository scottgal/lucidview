using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Logging;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using MarkdownViewer.Views;

namespace MarkdownViewer;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Log binding errors to console
        Logger.Sink = new ConsoleLogSink();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Disable Avalonia's data validation to avoid conflicts with custom IBinding
        if (BindingPlugins.DataValidators.Count > 0)
            BindingPlugins.DataValidators.RemoveAt(0);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
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

    private class ConsoleLogSink : ILogSink
    {
        public bool IsEnabled(LogEventLevel level, string area)
        {
            return level >= LogEventLevel.Warning && area == "Binding";
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            Console.WriteLine($"[{level}] {area}: {messageTemplate} | Source: {source?.GetType().Name ?? "null"}");
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
            params object?[] propertyValues)
        {
            try
            {
                // Build message with property values
                var values = propertyValues?.Select(v => v?.ToString() ?? "null").ToArray() ?? Array.Empty<string>();
                Console.WriteLine(
                    $"[{level}] {area}: Values=[{string.Join(", ", values)}] | Source: {source?.GetType().Name ?? "null"}");
            }
            catch
            {
                Console.WriteLine($"[{level}] {area}: {messageTemplate}");
            }
        }
    }
}