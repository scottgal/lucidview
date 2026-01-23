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
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MarkdownViewer",
        "crash.log"
    );

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Log binding errors with filtering for known library issues
        Logger.Sink = new FilteringLogSink();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Data validators already cleared in Program.cs - don't double-remove
        // This prevents "Index was out of range" errors

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();

            // Subscribe to dispatcher exceptions
            if (Avalonia.Threading.Dispatcher.UIThread != null)
            {
                // Note: Avalonia doesn't have DispatcherUnhandledException like WPF
                // Unhandled exceptions are caught in Program.cs
            }
        }

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

    /// <summary>
    /// Log sink that filters out known harmless library warnings
    /// </summary>
    private class FilteringLogSink : ILogSink
    {
        // Known harmless errors from third-party libraries
        private static readonly string[] IgnorablePatterns =
        [
            "StaticBinding",
            "Unsupported IBinding",
            "Markdown.Avalonia.Extensions"
        ];

        public bool IsEnabled(LogEventLevel level, string area)
        {
            return level >= LogEventLevel.Warning;
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            // Filter out known harmless library errors
            if (IsIgnorable(messageTemplate, source))
                return;

            var message = $"[{level}] {area}: {messageTemplate} | Source: {source?.GetType().FullName ?? "null"}";
            Console.WriteLine(message);

            // Log errors to file for diagnostics
            if (level >= LogEventLevel.Error)
            {
                LogToFile(message);
            }
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
            params object?[] propertyValues)
        {
            // Check if any property value contains ignorable patterns
            var valuesStr = string.Join(", ", propertyValues?.Select(v => v?.ToString() ?? "null") ?? []);
            if (IsIgnorable(messageTemplate, source) || IsIgnorable(valuesStr, null))
                return;

            try
            {
                var message = $"[{level}] {area}: Template={messageTemplate} Values=[{valuesStr}] | Source: {source?.GetType().FullName ?? "null"}";
                Console.WriteLine(message);

                // Log errors to file for diagnostics
                if (level >= LogEventLevel.Error)
                {
                    LogToFile(message);
                }
            }
            catch
            {
                Console.WriteLine($"[{level}] {area}: {messageTemplate}");
            }
        }

        private static bool IsIgnorable(string? message, object? source)
        {
            if (string.IsNullOrEmpty(message))
                return false;

            // Check message against known patterns
            foreach (var pattern in IgnorablePatterns)
            {
                if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Check source type
            var sourceType = source?.GetType().FullName ?? "";
            foreach (var pattern in IgnorablePatterns)
            {
                if (sourceType.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void LogToFile(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(CrashLogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(CrashLogPath, $"[{timestamp}] {message}\n");
            }
            catch
            {
                // Ignore logging failures
            }
        }
    }
}