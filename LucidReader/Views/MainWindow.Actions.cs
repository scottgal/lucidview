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
