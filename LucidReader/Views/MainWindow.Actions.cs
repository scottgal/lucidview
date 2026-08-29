using LucidReader.Models;
using LucidReader.Services;

namespace LucidReader.Views;

/// <summary>
/// Toolbar/menu/keybinding actions (add feed, refresh, navigate, mark
/// read/starred, open original, search focus, find in article, settings).
/// Every command here is a Task 6 stub, a no-op RelayCommand, so the shell
/// compiles and the KeyBindings and toolbar buttons in MainWindow.axaml have
/// something to bind to. Tasks 7-11 replace these with real bodies that read
/// _services and SelectedItemRow / SelectedFeedNode.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// The three hover row actions (Task 8a). RowActions never touches
    /// ReaderServices itself; it only raises events, so this window stays
    /// the sole place that calls into the engine, matching how
    /// MarkSelectedReadAsync and OpenOriginalArticle already work.
    /// </summary>
    private async void OnRowMarkReadClicked(object? sender, EventArgs e)
    {
        if (RowFromSender(sender) is not { } row) return;

        if (ReferenceEquals(row, SelectedItemRow)) _dwell.CancelPending();
        await ToggleReadAsync(row);
    }

    private async void OnRowToggleStarClicked(object? sender, EventArgs e)
    {
        if (RowFromSender(sender) is not { } row) return;

        var target = !row.IsStarred;
        await _services.Items.SetStarredAsync(row.Id, target);
        row.IsStarred = target;
    }

    private void OnRowOpenOriginalClicked(object? sender, EventArgs e)
    {
        if (RowFromSender(sender) is not { } row) return;

        if (!SafeLinkOpener.TryOpen(row.Item.Link, out var reason))
            StatusMessage = reason ?? "This article has no link to open.";
    }

    private static ItemRow? RowFromSender(object? sender) =>
        (sender as Avalonia.StyledElement)?.DataContext as ItemRow;

    public RelayCommand NextItemCommand { get; } = new(() => { });
    public RelayCommand PreviousItemCommand { get; } = new(() => { });
    public RelayCommand NextUnreadCommand { get; } = new(() => { });
    public RelayCommand PreviousUnreadCommand { get; } = new(() => { });
    public RelayCommand ToggleReadCommand { get; } = new(() => { });
    public RelayCommand ToggleStarCommand { get; } = new(() => { });
    public RelayCommand RefreshCurrentFeedCommand { get; } = new(() => { });
    public RelayCommand RefreshAllCommand { get; } = new(() => { });
    public RelayCommand OpenOriginalCommand { get; } = new(() => { });
    public RelayCommand FocusSearchCommand { get; } = new(() => { });
    public RelayCommand FindInArticleCommand { get; } = new(() => { });
    public RelayCommand AddFeedCommand { get; } = new(() => { });
    public RelayCommand OpenSettingsCommand { get; } = new(() => { });
}
