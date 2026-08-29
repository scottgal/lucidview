namespace LucidReader.Views;

/// <summary>
/// The item list. Minimal for Task 1: loads nothing beyond a compiling
/// no-op, so later tasks can MODIFY this file (rather than create it) without
/// hitting a CS0102 duplicate-member error against MainWindow.axaml.cs.
/// </summary>
public partial class MainWindow
{
    // CA1822 (member could be static) is suppressed here on purpose: these
    // are placeholders that a later task fills in with real bodies that read
    // _services and other instance state.
#pragma warning disable CA1822
    public Task LoadItemsAsync() => Task.CompletedTask;

    private Task OnItemSelectedAsync(ItemRow? row) => Task.CompletedTask;
#pragma warning restore CA1822
}
