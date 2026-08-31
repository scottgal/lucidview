using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using LucidReader.Core.Model;
using LucidReader.Models;
using LucidReader.Services;

namespace LucidReader.Views;

/// <summary>
/// Toolbar/menu/keybinding actions (add feed, refresh, navigate, mark
/// read/starred, open original, search focus, find in article, tags,
/// export, settings). OpenSettingsCommand is wired to
/// ShowSettingsDialogAsync (Task 11, MainWindow.Settings.cs) and
/// AddFeedCommand to ShowAddFeedDialogAsync (Task 13,
/// MainWindow.Subscriptions.cs). Every command below is real.
///
/// PDF export is out of scope for this app, per the plan-level decision that
/// lucidVIEW's PdfExportService could not be shared (it depends on a large
/// mermaid rendering service); ExportArticleCommand writes markdown only.
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

        // Every async void handler in this window is wrapped: an exception
        // escaping one lands on the synchronization context unhandled and
        // takes the process down, and each of these awaits a SQLite write or
        // an HTTP call. The failure belongs in the status bar.
        try
        {
            if (ReferenceEquals(row, SelectedItemRow)) _dwell.CancelPending();
            await ToggleReadAsync(row);
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not update this article: " + ex.Message;
        }
    }

    private async void OnRowToggleStarClicked(object? sender, EventArgs e)
    {
        if (RowFromSender(sender) is not { } row) return;

        try
        {
            var target = !row.IsStarred;
            await _services.Items.SetStarredAsync(row.Id, target);
            row.IsStarred = target;
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not star this article: " + ex.Message;
        }
    }

    private void OnRowOpenOriginalClicked(object? sender, EventArgs e)
    {
        if (RowFromSender(sender) is not { } row) return;

        if (!SafeLinkOpener.TryOpen(row.Item.Link, out var reason))
            StatusMessage = reason ?? "This article has no link to open.";
    }

    private static ItemRow? RowFromSender(object? sender) =>
        (sender as Avalonia.StyledElement)?.DataContext as ItemRow;

    private KeyModifiers _commandModifier = KeyModifiers.Control;

    /// <summary>
    /// Wires the single keyboard entry point for this window. Called once
    /// from the constructor (MainWindow.axaml.cs).
    ///
    /// Nothing uses Window.KeyBindings any more, letter-only or otherwise.
    /// KeyBindings are evaluated by the KeyboardDevice before the routed
    /// KeyDown is raised and have no text-input guard, which is what let the
    /// old bare-letter gestures fire while the user typed in the search box.
    /// A bubbling KeyDown handler runs after the focused control has had its
    /// turn, can ask FocusManager where focus actually is, and - unlike a
    /// KeyBinding - is reachable from the UI test harness, whose PressKey
    /// raises routed key events.
    ///
    /// The command modifier is read once from Avalonia's own
    /// PlatformSettings.HotkeyConfiguration.CommandModifiers rather than
    /// branching on OperatingSystem: it is Meta on macOS (Cmd+, for settings
    /// is a system-wide convention there; Ctrl+, would read as broken) and
    /// Control on Windows and Linux.
    /// </summary>
    private void ConfigurePlatformKeyBindings()
    {
        _commandModifier = PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);
    }

    /// <summary>
    /// True when this element is somewhere inside a text-entry control, so a
    /// printable key means a character rather than an action. Walks ancestors
    /// as well as testing the element itself, because a TextBox's inner
    /// TextPresenter can be the routed event's source.
    /// </summary>
    private static bool IsTextEntry(object? element)
    {
        if (element is TextBox) return true;
        return element is Visual visual && visual.FindAncestorOfType<TextBox>() is not null;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;

        // Both the focused element and the event's source are consulted. In
        // the running app they are the same thing; under the test harness,
        // which raises the routed event on a control it was pointed at, the
        // source is the one that is reliably right.
        var focusIsTextEntry = IsTextEntry(FocusManager?.GetFocusedElement()) || IsTextEntry(e.Source);

        var shortcut = ReaderShortcuts.Resolve(e.Key, e.KeyModifiers, focusIsTextEntry, _commandModifier);
        if (shortcut == ReaderShortcut.None) return;

        e.Handled = true;
        Run(shortcut);
    }

    /// <summary>
    /// Maps a resolved shortcut onto the command that performs it. Going
    /// through the RelayCommands rather than calling the async methods
    /// directly is what keeps this handler safe: RelayCommand.Execute is the
    /// one async void in this app that already catches everything.
    /// </summary>
    private void Run(ReaderShortcut shortcut)
    {
        var command = shortcut switch
        {
            ReaderShortcut.NextItem => NextItemCommand,
            ReaderShortcut.PreviousItem => PreviousItemCommand,
            ReaderShortcut.NextUnread => NextUnreadCommand,
            ReaderShortcut.PreviousUnread => PreviousUnreadCommand,
            ReaderShortcut.ToggleRead => ToggleReadCommand,
            ReaderShortcut.ToggleStar => ToggleStarCommand,
            ReaderShortcut.RefreshCurrentFeed => RefreshCurrentFeedCommand,
            ReaderShortcut.RefreshAll => RefreshAllCommand,
            ReaderShortcut.OpenOriginal => OpenOriginalCommand,
            ReaderShortcut.FocusSearch => FocusSearchCommand,
            ReaderShortcut.EditTags => EditTagsCommand,
            ReaderShortcut.FindInArticle => FindInArticleCommand,
            ReaderShortcut.AddFeed => AddFeedCommand,
            ReaderShortcut.OpenSettings => OpenSettingsCommand,
            ReaderShortcut.ExportArticle => ExportArticleCommand,
            ReaderShortcut.OpenUserManual => OpenUserManualCommand,

            // The three reading-size gestures have no command of their own:
            // they are menu actions, and the View menu is where they are named
            // (LucidReader.Models.ReaderMenu). Routing them through
            // RunMenuAction rather than giving them a second implementation
            // here is what keeps the keystroke and the menu item doing exactly
            // the same thing.
            ReaderShortcut.IncreaseFontSize => null,
            ReaderShortcut.DecreaseFontSize => null,
            ReaderShortcut.ResetFontSize => null,

            _ => null
        };

        if (command is not null)
        {
            command.Execute(null);
            return;
        }

        switch (shortcut)
        {
            case ReaderShortcut.IncreaseFontSize:
                RunMenuAction(ReaderMenuAction.IncreaseFontSize);
                break;
            case ReaderShortcut.DecreaseFontSize:
                RunMenuAction(ReaderMenuAction.DecreaseFontSize);
                break;
            case ReaderShortcut.ResetFontSize:
                RunMenuAction(ReaderMenuAction.ResetFontSize);
                break;
        }
    }

    private RelayCommand? _nextItem, _previousItem, _nextUnread, _previousUnread;
    private RelayCommand? _toggleRead, _toggleStar, _refreshCurrent, _refreshAll;
    private RelayCommand? _openOriginal, _focusSearch, _findInArticle;
    private RelayCommand? _addFeed, _openSettings, _exportArticle, _editTags, _openUserManual;

    public RelayCommand NextItemCommand => _nextItem ??= new RelayCommand(() => Move(true, false));
    public RelayCommand PreviousItemCommand => _previousItem ??= new RelayCommand(() => Move(false, false));
    public RelayCommand NextUnreadCommand => _nextUnread ??= new RelayCommand(() => Move(true, true));
    public RelayCommand PreviousUnreadCommand => _previousUnread ??= new RelayCommand(() => Move(false, true));
    public RelayCommand ToggleReadCommand => _toggleRead ??= new RelayCommand(async () => await MarkSelectedReadAsync());
    public RelayCommand ToggleStarCommand => _toggleStar ??= new RelayCommand(async () => await ToggleStarAsync());
    public RelayCommand RefreshCurrentFeedCommand => _refreshCurrent ??= new RelayCommand(async () => await RefreshCurrentFeedAsync());
    public RelayCommand RefreshAllCommand => _refreshAll ??= new RelayCommand(async () => await RefreshAllAsync());

    /// <summary>
    /// The dead button this task closes: this was `new(() => { })` from
    /// Task 6 onward, so O and the row's open-original hover button both
    /// silently did nothing. OpenOriginalArticle (MainWindow.Reading.cs,
    /// built in Task 8) already routes every link through SafeLinkOpener's
    /// http/https allowlist; this just wires the command to it.
    /// </summary>
    public RelayCommand OpenOriginalCommand => _openOriginal ??= new RelayCommand(OpenOriginalArticle);

    public RelayCommand FocusSearchCommand => _focusSearch ??= new RelayCommand(FocusSearch);

    /// <summary>
    /// Ctrl+F searches inside the current article. The reading pane is a
    /// markdown control with no built-in find, so this focuses the global
    /// search box pre-scoped to the current feed rather than pretending to
    /// offer something that does not exist.
    /// </summary>
    public RelayCommand FindInArticleCommand => _findInArticle ??= new RelayCommand(FocusFindInArticle);

    /// <summary>
    /// Task 13: opens the add-feed dialog, which resolves whatever address
    /// the user pasted through FeedAutodiscovery
    /// (MainWindow.Subscriptions.cs). This was a no-op stub from Task 6
    /// until autodiscovery shipped.
    /// </summary>
    public RelayCommand AddFeedCommand => _addFeed ??= new RelayCommand(async () => await ShowAddFeedDialogAsync());

    /// <summary>
    /// Task 11: opens the global settings dialog (MainWindow.Settings.cs).
    /// </summary>
    public RelayCommand OpenSettingsCommand => _openSettings ??= new RelayCommand(async () => await ShowSettingsDialogAsync());

    public RelayCommand ExportArticleCommand => _exportArticle ??= new RelayCommand(async () => await ExportArticleAsync());

    /// <summary>
    /// Tags on the selected article. Editing is a comma-separated list rather
    /// than a bespoke chip editor: tags are a low-traffic feature and a text
    /// box is honest about that.
    /// </summary>
    public RelayCommand EditTagsCommand => _editTags ??= new RelayCommand(async () => await EditTagsAsync());

    /// <summary>
    /// The bundled user manual, rendered in the reading pane. Reached from the
    /// Help menu and from F1. A command rather than a direct call from both,
    /// so the file read behind it goes through RelayCommand's catch like every
    /// other action here.
    /// </summary>
    public RelayCommand OpenUserManualCommand =>
        _openUserManual ??= new RelayCommand(async () => await ShowUserManualAsync());

    /// <summary>
    /// Navigation rule, kept static and list-shaped so it can be tested without
    /// a window. Deliberately does NOT wrap: running off the end of the list
    /// back to the top while holding J is disorienting.
    /// </summary>
    internal static int FindNextIndexIn(IReadOnlyList<bool> readStates, int current, bool forward, bool unreadOnly)
    {
        if (readStates.Count == 0) return -1;
        if (current < 0) return forward ? 0 : readStates.Count - 1;

        var step = forward ? 1 : -1;

        for (var i = current + step; i >= 0 && i < readStates.Count; i += step)
        {
            if (!unreadOnly || !readStates[i]) return i;
        }

        return current;
    }

    internal int FindNextIndex(int current, bool forward, bool unreadOnly) =>
        FindNextIndexIn(ItemRows.Select(r => r.IsRead).ToList(), current, forward, unreadOnly);

    private void Move(bool forward, bool unreadOnly)
    {
        var current = SelectedItemRow is null ? -1 : ItemRows.IndexOf(SelectedItemRow);
        var next = FindNextIndex(current, forward, unreadOnly);

        if (next < 0 || next == current) return;

        SelectedItemRow = ItemRows[next];

        var itemList = this.FindControl<ListBox>("ItemList");
        itemList?.ScrollIntoView(SelectedItemRow);
    }

    private async Task ToggleStarAsync()
    {
        if (SelectedItemRow is not { } row) return;

        var target = !row.IsStarred;
        await _services.Items.SetStarredAsync(row.Id, target);
        row.IsStarred = target;
    }

    private async Task RefreshCurrentFeedAsync()
    {
        if (SelectedFeedNode?.FeedId is not { } feedId)
        {
            await RefreshAllAsync();
            return;
        }

        await RefreshFeedAsync(feedId);
    }

    private async Task RefreshAllAsync()
    {
        StatusMessage = "Refreshing every feed...";

        var queued = await _services.Scheduler.TickAsync();
        if (queued == 0)
        {
            // Nothing was due. A manual Refresh All should still fetch, so
            // queue every enabled feed directly.
            foreach (var feed in await _services.Feeds.GetAllAsync())
                if (feed.IsEnabled) _services.Refresh.TryQueue(feed.Id, isManual: true);
        }

        StatusMessage = "Refresh started.";
    }

    private async Task AfterRefreshAsync(string message)
    {
        await LoadFeedTreeAsync();
        await LoadItemsAsync();
        StatusMessage = message;
    }

    private void FocusSearch()
    {
        var searchBox = this.FindControl<TextBox>("SearchBox");
        searchBox?.Focus();
    }

    private void FocusFindInArticle()
    {
        FocusSearch();
        StatusMessage = "Searching across your articles. Clear the box to go back to the list.";
    }

    private async Task ExportArticleAsync()
    {
        if (SelectedItemRow is not { } row) return;

        var item = await _services.Items.GetAsync(row.Id);
        if (item is null) return;

        var suggested = string.Concat((item.Title ?? "article")
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export article as markdown",
            SuggestedFileName = suggested + ".md",
            DefaultExtension = "md",
            FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }]
        });

        if (file is null) return;

        var body = item.ContentMarkdown ?? item.Summary ?? string.Empty;
        var header = $"# {item.Title}\n\n" +
                     (item.Link is { } link ? $"[{link}]({link})\n\n" : string.Empty);

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(header + body);

        StatusMessage = "Exported to " + file.Name;
    }

    private async Task EditTagsAsync()
    {
        if (SelectedItemRow is not { } row) return;

        var current = await _services.Tags.GetForItemAsync(row.Id);

        var dialog = new InputDialog(
            "Tags",
            "Comma-separated tags for this article",
            string.Join(", ", current));
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } entered) return;

        // Parsed by the same rules the reading pane's tag box uses
        // (LucidReader.Core.Model.TagName), rather than a second, looser
        // split that trimmed but did not collapse whitespace, did not bound
        // the length, and compared case with .NET's Unicode-aware
        // OrdinalIgnoreCase where the database compares with SQLite's
        // ASCII-only NOCASE.
        var parsed = TagName.ParseList(entered);
        var wanted = parsed.Names;

        foreach (var tag in current.Where(t => !wanted.Any(w => TagName.AreSame(w, t))))
            await _services.Tags.RemoveFromItemAsync(row.Id, tag);

        foreach (var tag in wanted.Where(t => !current.Any(c => TagName.AreSame(c, t))))
            await _services.Tags.AddToItemAsync(row.Id, tag);

        await AfterTagWriteAsync(row.Id);

        var summary = wanted.Count == 0 ? "Tags cleared." : "Tags: " + string.Join(", ", wanted);
        StatusMessage = parsed.Errors.Count > 0
            ? summary + " " + string.Join(" ", parsed.Errors)
            : summary;
    }
}
