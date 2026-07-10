using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Mostlylucid.LucidView.Markdown.Tests;

/// <summary>
/// xUnit collection fixture that boots a single Avalonia headless session for the
/// whole test run. Tests opt in via [Collection("Avalonia")] and use
/// DispatchAsync to marshal work onto the headless UI thread.
/// </summary>
public sealed class HeadlessAvaloniaFixture : IDisposable
{
    private readonly HeadlessUnitTestSession _session;

    public HeadlessAvaloniaFixture()
    {
        _session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
    }

    public Task<T> DispatchAsync<T>(Func<T> work) =>
        _session.Dispatch(work, CancellationToken.None);

    public Task<T> DispatchAsync<T>(Func<Task<T>> work) =>
        _session.Dispatch(work, CancellationToken.None);

    public Task DispatchAsync(Action work) =>
        _session.Dispatch(work, CancellationToken.None);

    public Task DispatchAsync(Func<Task> work) =>
        _session.Dispatch<bool>(async () => { await work(); return true; }, CancellationToken.None);

    public void Dispose() => _session.Dispose();
}

/// <summary>
/// Minimal Avalonia application used to bootstrap the headless platform.
/// BuildAvaloniaApp is discovered by HeadlessUnitTestSession.StartNew and used to
/// configure the AppBuilder.
/// </summary>
public sealed class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());

        // Suppress rendering exceptions from missing font glyphs in the headless Skia renderer.
        Dispatcher.UIThread.UnhandledExceptionFilter += (_, args) =>
        {
            if (args.Exception is InvalidOperationException ex
                && ex.Message.Contains("glyphTypeface", StringComparison.OrdinalIgnoreCase))
            {
                args.RequestCatch = true;
            }
        };
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            if (args.Exception is InvalidOperationException ex
                && ex.Message.Contains("glyphTypeface", StringComparison.OrdinalIgnoreCase))
            {
                args.Handled = true;
            }
        };
    }
}

/// <summary>Marker so all Avalonia tests share one headless session.</summary>
[CollectionDefinition("Avalonia")]
public sealed class AvaloniaCollection : ICollectionFixture<HeadlessAvaloniaFixture> { }
