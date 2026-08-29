using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// The item list. Minimal for Task 6: loads nothing beyond a compiling
/// no-op, so later tasks (7-11) can MODIFY this file (rather than create it)
/// without hitting a CS0102 duplicate-member error against MainWindow.axaml.cs.
/// A later task fills ItemRows from SelectedFeedNode / SearchText / CurrentFilter
/// and populates ItemRows on the window.
/// </summary>
public partial class MainWindow
{
    // CA1822 (member could be static) is suppressed here on purpose: these
    // are placeholders that a later task fills in with real bodies that read
    // _services and other instance state.
#pragma warning disable CA1822
    public Task LoadItemsAsync() => Task.CompletedTask;

    private Task OnItemSelectedAsync(ItemRow? row) => Task.CompletedTask;

    /// <summary>Stub: a later task re-queries ItemRows against SearchText.</summary>
    private Task OnSearchTextChangedAsync() => Task.CompletedTask;
#pragma warning restore CA1822
}
