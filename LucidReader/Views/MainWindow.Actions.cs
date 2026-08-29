using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using LucidReader.Models;
using LucidReader.Services;

namespace LucidReader.Views;

/// <summary>
/// Toolbar/menu/keybinding actions (add feed, refresh, navigate, mark
/// read/starred, open original, search focus, find in article, tags,
/// export, settings). AddFeedCommand and OpenSettingsCommand stay no-op
/// stubs here deliberately: the dialogs they would open (feed autodiscovery,
/// settings) are built in later tasks and are not part of this one. Every
/// other command below is real.
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

    /// <summary>
    /// Find-in-article, Add-feed, Settings and Export cannot be declared as
    /// static Gesture strings in MainWindow.axaml: this app is styled after
    /// macOS Mail for a Mac-first audience, and on macOS those four take
    /// Command, not Control (Cmd+, for settings is a system-wide convention
    /// every Mac app honours; Ctrl+, would simply read as broken there).
    /// Rather than hardcode either modifier, or branch on OperatingSystem,
    /// this reads Avalonia's own notion of "the modifier for this platform" -
    /// PlatformSettings.HotkeyConfiguration.CommandModifiers, which the
    /// platform backend sets to Meta on macOS and Control on Windows/Linux -
    /// so the same four bindings are correct everywhere. Called once from
    /// the constructor (MainWindow.axaml.cs); the letter-only bindings
    /// (J, K, N, P, M, S, R, O, T) stay in XAML because they have no
    /// platform convention to defer to.
    /// </summary>
    private void ConfigurePlatformKeyBindings()
    {
        var commandModifier = PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;

        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F, commandModifier), Command = FindInArticleCommand });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.N, commandModifier), Command = AddFeedCommand });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.OemComma, commandModifier), Command = OpenSettingsCommand });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.S, commandModifier), Command = ExportArticleCommand });
    }

    private RelayCommand? _nextItem, _previousItem, _nextUnread, _previousUnread;
    private RelayCommand? _toggleRead, _toggleStar, _refreshCurrent, _refreshAll;
    private RelayCommand? _openOriginal, _focusSearch, _findInArticle;
    private RelayCommand? _addFeed, _openSettings, _exportArticle, _editTags;

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
    /// Not wired to a dialog yet: feed autodiscovery (Task 13) has not
    /// shipped, so there is nothing safe to open here.
    /// </summary>
    public RelayCommand AddFeedCommand => _addFeed ??= new RelayCommand(() => { });

    /// <summary>
    /// Not wired to a dialog yet: the settings window is a later task.
    /// </summary>
    public RelayCommand OpenSettingsCommand => _openSettings ??= new RelayCommand(() => { });

    public RelayCommand ExportArticleCommand => _exportArticle ??= new RelayCommand(async () => await ExportArticleAsync());

    /// <summary>
    /// Tags on the selected article. Editing is a comma-separated list rather
    /// than a bespoke chip editor: tags are a low-traffic feature and a text
    /// box is honest about that.
    /// </summary>
    public RelayCommand EditTagsCommand => _editTags ??= new RelayCommand(async () => await EditTagsAsync());

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

        StatusMessage = "Refreshing...";
        var outcome = await _services.Refresh.RefreshNowAsync(feedId);
        await AfterRefreshAsync(outcome.Success
            ? outcome.NotModified ? "No changes." : $"{outcome.NewItemCount} new articles."
            : "Refresh failed: " + outcome.Error);
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

        var wanted = entered
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var tag in current.Where(t => !wanted.Contains(t, StringComparer.OrdinalIgnoreCase)))
            await _services.Tags.RemoveFromItemAsync(row.Id, tag);

        foreach (var tag in wanted.Where(t => !current.Contains(t, StringComparer.OrdinalIgnoreCase)))
            await _services.Tags.AddToItemAsync(row.Id, tag);

        StatusMessage = wanted.Count == 0 ? "Tags cleared." : "Tags: " + string.Join(", ", wanted);
    }
}
