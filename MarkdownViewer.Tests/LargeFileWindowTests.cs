using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using MarkdownViewer.Services;
using MarkdownViewer.Views;

namespace MarkdownViewer.Tests;

[Collection("LargeFileAvalonia")]
public sealed class LargeFileWindowTests(LargeFileAvaloniaFixture fixture)
{
    [Fact]
    public async Task PageKeysContinueInsideALongLineAndReturnToItsStart()
    {
        await WithFile(".txt", new string('a', 65536) + "SECOND CHUNK" + new string('b', 65536) + "FINAL TAIL", async (path, file) =>
        {
            await fixture.DispatchAsync(async () =>
            {
                var window = new LargeFileWindow(path, file);
                try
                {
                    window.Show();
                    var source = Find<TextBox>(window, "SourceText");
                    await UntilAsync(() => source.Text?.Length == 65536);
                    source.Focus();
                    Assert.DoesNotContain("SECOND CHUNK", source.Text);
                    window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
                    await UntilAsync(() => source.Text?.StartsWith("SECOND CHUNK") == true);
                    Assert.Contains("65,537", Find<TextBlock>(window, "ReaderStatus").Text);
                    window.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.None);
                    await UntilAsync(() => source.Text?.StartsWith("aaaa") == true);
                    window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.Control);
                    await UntilAsync(() => source.Text?.EndsWith("FINAL TAIL") == true);
                    Assert.InRange(source.Text!.Length, 1, 65536);
                    window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.Control);
                    await UntilAsync(() => source.Text?.StartsWith("aaaa") == true);
                }
                finally { window.Close(); }
            });
        });
    }

    [Fact]
    public async Task SearchDisplaysAndSelectsTextBeyondTheFirstLongLinePage()
    {
        await WithFile(".txt", new string('a', 100_000) + "target at the tail", async (path, file) =>
        {
            await fixture.DispatchAsync(async () =>
            {
                var window = new LargeFileWindow(path, file);
                try
                {
                    window.Show();
                    var source = Find<TextBox>(window, "SourceText");
                    await UntilAsync(() => source.Text is not null);
                    Find<TextBox>(window, "ReaderSearch").Text = "target";
                    Find<Button>(window, "FindNext").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await UntilAsync(() => source.Text?.Contains("target at the tail") == true);
                    Assert.Equal("target", source.Text![source.SelectionStart..source.SelectionEnd]);
                    Assert.InRange(source.Text.Length, 6, 65536);
                }
                finally { window.Close(); }
            });
        });
    }

    [Fact]
    public async Task HeadingLinkOpensTheTargetAtTheTopOfAnotherPreviewPage()
    {
        var text = "[Jump](#later-heading)\n\n" + string.Join('\n', Enumerable.Range(0, 160)
            .SelectMany(i => new[] { $"Paragraph {i}.", "" })) + "\n# Later heading\n\nThe target.";
        await WithFile(".md", text, async (path, file) =>
        {
            await fixture.DispatchAsync(async () =>
            {
                var window = new LargeFileWindow(path, file);
                try
                {
                    window.Show();
                    await UntilAsync(() => Find<TextBlock>(window, "ReaderStatus").Text?.Contains("Markdown preview") == true);
                    var renderer = window.GetLogicalDescendants().OfType<LiveMarkdown.Avalonia.MarkdownRenderer>().Single();
                    renderer.RaiseEvent(new LiveMarkdown.Avalonia.LinkClickedEventArgs(
                        LiveMarkdown.Avalonia.MarkdownTextBlock.LinkClickEvent, renderer, new Uri("#later-heading", UriKind.Relative)));
                    await UntilAsync(() => window.GetLogicalDescendants().OfType<Mostlylucid.LucidView.Markdown.LucidMarkdownView>()
                        .SingleOrDefault()?.Markdown?.StartsWith("# Later heading") == true);
                    Assert.Contains("Markdown preview", Find<TextBlock>(window, "ReaderStatus").Text);
                }
                finally { window.Close(); }
            });
        });
    }

    [Fact]
    public async Task ScrollbarLoadsOnlyTheRequestedSourcePage()
    {
        await WithFile(".txt", string.Join('\n', Enumerable.Range(0, 1000).Select(i => $"row {i}")), async (path, file) =>
        {
            await fixture.DispatchAsync(async () =>
            {
                var window = new LargeFileWindow(path, file);
                try
                {
                    window.Show();
                    var source = Find<TextBox>(window, "SourceText");
                    await UntilAsync(() => source.Text?.StartsWith("row 0\n") == true);
                    Find<ScrollBar>(window, "FilePosition").Value = 800;
                    await UntilAsync(() => source.Text?.StartsWith("row 800\n") == true);
                    Assert.Contains("row 919", source.Text);
                    Assert.DoesNotContain("row 920", source.Text);
                }
                finally { window.Close(); }
            });
        });
    }

    [Fact]
    public async Task PageKeysNavigateWhileTheSourceHasFocus()
    {
        await WithFile(".txt", string.Join('\n', Enumerable.Range(0, 400).Select(i => $"row {i}")), async (path, file) =>
        {
            await fixture.DispatchAsync(async () =>
            {
                var window = new LargeFileWindow(path, file);
                try
                {
                    window.Show();
                    var source = Find<TextBox>(window, "SourceText");
                    await UntilAsync(() => source.Text?.StartsWith("row 0\n") == true);
                    source.Focus();
                    window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
                    await UntilAsync(() => source.Text?.StartsWith("row 120\n") == true);
                    window.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.None);
                    await UntilAsync(() => source.Text?.StartsWith("row 0\n") == true);
                }
                finally { window.Close(); }
            });
        });
    }

    [Fact]
    public async Task NextFindsBothMatchesOnOneLineAndHighlightsTheContext()
    {
        await WithFile(".txt", "target twice: target\nlast", async (path, file) =>
        {
            await fixture.DispatchAsync(async () =>
            {
                var window = new LargeFileWindow(path, file);
                try
                {
                    window.Show();
                    await UntilAsync(() => Find<TextBox>(window, "SourceText").Text is not null);
                    Find<TextBox>(window, "ReaderSearch").Text = "target";
                    var next = Find<Button>(window, "FindNext");
                    next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var context = Find<TextBox>(window, "MatchContext");
                    await UntilAsync(() => context.IsVisible && context.SelectionStart == 0);
                    next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await UntilAsync(() => context.IsVisible && context.SelectionStart == 14);
                    Assert.Equal(20, context.SelectionEnd);
                }
                finally { window.Close(); }
            });
        });
    }

    [Fact]
    public async Task MarkdownPreviewCompletesAndCanReturnToSource()
    {
        await WithFile(".md", "# Heading\n\nA **paragraph**.\n", async (path, file) =>
        {
            await fixture.DispatchAsync(async () =>
            {
                var window = new LargeFileWindow(path, file);
                try
                {
                    window.Show();
                    var status = Find<TextBlock>(window, "ReaderStatus");
                    await UntilAsync(() => status.Text?.Contains("Markdown preview") == true);
                    Assert.Contains(window.GetLogicalDescendants(), control => control is Mostlylucid.LucidView.Markdown.LucidMarkdownView);
                    var toggle = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Source"));
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await UntilAsync(() => window.GetLogicalDescendants().OfType<TextBox>()
                        .FirstOrDefault(control => control.Name == "SourceText")?.Text?.Contains("**paragraph**") == true);
                }
                finally { window.Close(); }
            });
        });
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Reader did not settle within five seconds.");
            await Task.Delay(20);
        }
    }

    private static async Task WithFile(string extension, string text, Func<string, IndexedTextFile, Task> test)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"lucidview-window-{Guid.NewGuid():N}{extension}");
        try
        {
            await File.WriteAllTextAsync(filePath, text);
            await test(filePath, await IndexedTextFile.OpenAsync(filePath));
        }
        finally { File.Delete(filePath); }
    }
}

public sealed class LargeFileAvaloniaFixture : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(LargeFileTestApp));
    public Task DispatchAsync(Func<Task> work) => _session.Dispatch<bool>(async () => { await work(); return true; }, CancellationToken.None);
    public void Dispose() => _session.Dispose();
}

public sealed class LargeFileTestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<LargeFileTestApp>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://LiveMarkdown.Avalonia/"))
        {
            Source = new Uri("avares://LiveMarkdown.Avalonia/Styles.axaml")
        });
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://LiveMarkdown.Avalonia/"))
        {
            Source = new Uri("avares://LiveMarkdown.Avalonia/Defaults.axaml")
        });
    }
}

[CollectionDefinition("LargeFileAvalonia", DisableParallelization = true)]
public sealed class LargeFileAvaloniaCollection : ICollectionFixture<LargeFileAvaloniaFixture>;
