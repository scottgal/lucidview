namespace LucidReader.Views;

/// <summary>
/// The reading pane. Minimal for Task 1: a compiling no-op that a later task
/// modifies to actually render the selected article.
/// </summary>
public partial class MainWindow
{
    // CA1822 suppressed on purpose: a later task fills this in with a real
    // body that reads _services and other instance state.
#pragma warning disable CA1822
    public Task ShowArticleAsync(ItemRow? row) => Task.CompletedTask;
#pragma warning restore CA1822
}
