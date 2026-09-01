using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LucidReader.Core.Model;
using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// Tags: the sidebar's Tags section, the chip strip in the reading pane that
/// adds and removes them, and renaming or deleting one across every article
/// carrying it.
///
/// The whole feature is deliberately built out of pieces that already exist
/// rather than beside them. A tag row is a FeedTreeNode with a new Kind, so
/// it selects, highlights and repoints across a tree reload through the same
/// code every other row does. A tag view is an ItemQuery with a TagName, so
/// the All/Unread/Starred segment, the dedupe and the search scoping all keep
/// working inside it without a second code path.
///
/// Every route in here is a named, harness-reachable control. The T shortcut
/// and its modal comma-separated dialog still exist and still work, but a
/// modal is something the UI test harness cannot dismiss, so it cannot be the
/// only way to tag an article - the same argument that put Feed settings and
/// Resume feed on the toolbar next to their context-menu twins.
/// </summary>
public partial class MainWindow
{
    private string _tagEntryText = string.Empty;
    private bool _canEditArticleTags;
    private bool _isTagEntryExpanded;
    private string? _renamedTagToReselect;

    /// <summary>
    /// How wide the tag entry's reveal is when it is open: the 150px TextBox
    /// plus the 6px that separates it from the button. Named here rather than
    /// written into the XAML twice, since the collapsed state is the same
    /// number's absence.
    /// </summary>
    private const double TagEntryOpenWidth = 156;

    /// <summary>
    /// Where the selection should look for itself after a tree reload.
    ///
    /// Normally that is the row that was selected. The one exception is a tag
    /// that has just been renamed: IsSameRow matches a tag by name, and the
    /// name the selected node carries no longer exists, so the ordinary
    /// repoint would find nothing and silently drop the selection out from
    /// under a user who only renamed the thing they were reading. This hands
    /// the repoint a stand-in carrying the new name instead, once, and then
    /// forgets it.
    /// </summary>
    private FeedTreeNode RedirectAfterRename(FeedTreeNode previous)
    {
        if (_renamedTagToReselect is not { } newName) return previous;
        _renamedTagToReselect = null;

        return previous.Kind != FeedTreeNodeKind.Tag
            ? previous
            : new FeedTreeNode { Title = newName, Kind = FeedTreeNodeKind.Tag, TagName = newName };
    }

    /// <summary>
    /// The tags on the article currently in the reading pane. Rebuilt from
    /// the database after every add and remove rather than mutated in place,
    /// so the strip can only ever show tags that were really stored - which
    /// matters because an add can be rejected by the name rules or can land
    /// on a tag the article already carries.
    /// </summary>
    public ObservableCollection<ArticleTagChip> ArticleTags { get; } = [];

    /// <summary>
    /// Text in the reading pane's tag box. A comma-separated list is accepted
    /// here as well as in the T dialog, so "csharp, avalonia" in one go does
    /// what it looks like it does.
    /// </summary>
    public string TagEntryText
    {
        get => _tagEntryText;
        set { if (_tagEntryText == value) return; _tagEntryText = value; Raise(); }
    }

    /// <summary>
    /// Whether the tag editor is usable, which is exactly "there is an
    /// article in the reading pane". Bound by the whole strip's IsVisible:
    /// an empty tag box floating over an empty reading pane would be an
    /// invitation to tag nothing.
    /// </summary>
    public bool CanEditArticleTags
    {
        get => _canEditArticleTags;
        private set { if (_canEditArticleTags == value) return; _canEditArticleTags = value; Raise(); }
    }

    /// <summary>
    /// Whether the tag entry is open. Everything the strip shows about the
    /// add control - how wide the reveal is, and what the button says - is
    /// derived from this one flag, so the three can never disagree.
    /// </summary>
    public bool IsTagEntryExpanded
    {
        get => _isTagEntryExpanded;
        private set
        {
            if (_isTagEntryExpanded == value) return;
            _isTagEntryExpanded = value;
            Raise();
            Raise(nameof(TagEntryWidth));
            Raise(nameof(AddTagButtonLabel));
            Raise(nameof(AddTagButtonTip));
        }
    }

    /// <summary>
    /// The width the reveal animates to. Zero collapses it; the Border it is
    /// bound to clips its contents, so a zero here hides the field rather than
    /// leaving a squashed one behind.
    /// </summary>
    public double TagEntryWidth => IsTagEntryExpanded ? TagEntryOpenWidth : 0;

    /// <summary>
    /// "+" when there is nothing to add yet, "Add" once the field is open, so
    /// the one button reads as what it will do next.
    /// </summary>
    public string AddTagButtonLabel => IsTagEntryExpanded ? "Add" : "+";

    public string AddTagButtonTip =>
        IsTagEntryExpanded ? "Add what is typed as tags" : "Add a tag";

    /// <summary>
    /// Opens the entry and puts the caret in it, so a user who reached the
    /// button with Tab and pressed Space is typing immediately rather than
    /// having to Tab once more into a field that has only just appeared.
    /// </summary>
    private void ExpandTagEntry()
    {
        IsTagEntryExpanded = true;
        TagEntryBox.Focus();
    }

    /// <summary>
    /// Closes the entry, discarding whatever was typed.
    ///
    /// <paramref name="returnFocus"/> is what tells the two ways of closing it
    /// apart. Escape closes a field the caret is still in, and leaving the
    /// caret inside something about to be clipped to nothing is how a keyboard
    /// user gets stranded, so focus goes back to the button that opened it.
    /// Every other close - losing focus, switching article - is closing a
    /// field the focus has already left or is leaving of its own accord, and
    /// pulling it onto the button there would take it off whatever the user
    /// just clicked.
    ///
    /// Safe to call when already collapsed, which it routinely is: the Escape
    /// path moves the focus, which raises LostFocus on a box that is now
    /// empty, which asks to collapse a second time.
    /// </summary>
    private void CollapseTagEntry(bool returnFocus = false)
    {
        if (!IsTagEntryExpanded) return;

        TagEntryText = string.Empty;
        IsTagEntryExpanded = false;
        if (returnFocus) AddTagButton.Focus();
    }

    /// <summary>
    /// Reloads the chip strip for whichever article is being shown. Called
    /// from ShowArticleAsync, so switching articles switches the strip, and
    /// again after every write from the two handlers below.
    /// </summary>
    private async Task RefreshArticleTagsAsync(long? itemId)
    {
        ArticleTags.Clear();
        CanEditArticleTags = itemId is not null;

        if (itemId is not { } id) return;

        foreach (var name in await _services.Tags.GetForItemAsync(id))
            ArticleTags.Add(new ArticleTagChip { Name = name });
    }

    /// <summary>
    /// The one add control, doing whichever of its two jobs applies: open the
    /// entry when it is closed, commit what is in it when it is open.
    /// </summary>
    private async void OnAddArticleTagClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!IsTagEntryExpanded)
            {
                ExpandTagEntry();
                return;
            }

            await AddArticleTagsAsync(TagEntryText);
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not add that tag: " + ex.Message;
        }
    }

    /// <summary>
    /// Enter in the tag box does what the Add button does. A one-word tag
    /// followed by a reach for the mouse is the wrong shape for something
    /// meant to be quick.
    ///
    /// Escape closes the entry without adding anything, which is the other
    /// half of an expanding control: something that opens has to have a way
    /// out that does not commit.
    /// </summary>
    private async void OnTagEntryKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        try
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                e.Handled = true;
                CollapseTagEntry(returnFocus: true);
                return;
            }

            if (e.Key != Avalonia.Input.Key.Enter) return;

            // Marked handled before the await, not after: the window's own
            // bubbling KeyDown handler (MainWindow.Actions.cs) runs on the same
            // event, and although the text-entry guard already suppresses the
            // bare-letter shortcuts, leaving Enter unclaimed lets it reach the
            // dialog default-button machinery as well.
            e.Handled = true;

            // The entry deliberately stays open after a successful add, with
            // the box emptied and the caret still in it. Tagging an article
            // with two or three names is the ordinary case, not the unusual
            // one, and closing after each would make the second tag cost a
            // click to reopen what the user had not finished with. What closes
            // it is the user saying so: Escape, or clicking away from an empty
            // box, both below.
            await AddArticleTagsAsync(TagEntryText);
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not add that tag: " + ex.Message;
        }
    }

    /// <summary>
    /// Clicking or tabbing away from an empty entry closes it: an open field
    /// nobody is typing in is the permanent furniture this control exists to
    /// get rid of. A field with something half-typed in it is left alone,
    /// because losing focus is not the same as changing your mind and throwing
    /// away what someone was in the middle of writing would be.
    /// </summary>
    private void OnTagEntryLostFocus(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TagEntryText)) CollapseTagEntry();
    }

    /// <summary>
    /// Adds whatever the box holds to the selected article. Public and
    /// text-in so the flow can be driven without a pointer.
    /// </summary>
    public async Task AddArticleTagsAsync(string entry)
    {
        if (SelectedItemRow is not { } row) return;

        var parsed = TagName.ParseList(entry);

        if (parsed.Names.Count == 0)
        {
            // Errors first: a name refused for a stated reason has to say so,
            // or a rejected tag is indistinguishable from a silently broken
            // button. A blank box is not an error and says nothing.
            if (parsed.Errors.Count > 0) StatusMessage = string.Join(" ", parsed.Errors);
            return;
        }

        foreach (var name in parsed.Names)
            await _services.Tags.AddToItemAsync(row.Id, name);

        TagEntryText = string.Empty;
        await AfterTagWriteAsync(row.Id);

        StatusMessage = parsed.Errors.Count > 0
            ? $"Tagged with {string.Join(", ", parsed.Names)}. " + string.Join(" ", parsed.Errors)
            : $"Tagged with {string.Join(", ", parsed.Names)}.";
    }

    private async void OnRemoveArticleTagClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as Avalonia.StyledElement)?.DataContext is not ArticleTagChip chip) return;
            if (SelectedItemRow is not { } row) return;

            await _services.Tags.RemoveFromItemAsync(row.Id, chip.Name);
            await AfterTagWriteAsync(row.Id);
            StatusMessage = $"Removed the tag {chip.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not remove that tag: " + ex.Message;
        }
    }

    /// <summary>
    /// What every tag write has to do afterwards: refresh the chip strip, drop
    /// any tag that no longer has an item, and rebuild the tree so the Tags
    /// section reflects what is actually stored - a tag that has just been
    /// used for the first time appears, and one whose last article just lost
    /// it disappears rather than lingering as an empty row.
    ///
    /// The item list is reloaded too, but only when a tag row is selected:
    /// that is the one case where a tag write changes which articles belong in
    /// the list on screen.
    /// </summary>
    private async Task AfterTagWriteAsync(long itemId)
    {
        // Read before the reload, because the reload is what can take the
        // selected row away: removing an article's last use of a tag deletes
        // the tag, so a tree rebuild finds no row to repoint onto and
        // SelectedFeedNode comes back null.
        var wasViewingATag = SelectedFeedNode?.Kind == FeedTreeNodeKind.Tag;

        await _services.Tags.DeleteUnusedAsync();
        await RefreshArticleTagsAsync(itemId);
        await LoadFeedTreeAsync();

        if (!wasViewingATag) return;

        if (SelectedFeedNode is not null)
        {
            await LoadItemsAsync();

            // The reload throws every ItemRow away, so the article the user
            // was reading loses its selection and the reading pane goes blank
            // underneath the tag box they just typed into. Put it back if it
            // is still in the list; if the write took it out of this tag view,
            // it is not there to put back and the blank pane is the correct
            // answer.
            SelectedItemRow ??= ItemRows.FirstOrDefault(r => r.Id == itemId);
            return;
        }

        // The tag being viewed has just stopped existing. Emptying the list is
        // the honest answer: falling through to LoadItemsAsync with no
        // selection would quietly widen the pane to every article in every
        // feed, which looks like the removal did something much larger than it
        // did. The caller's own status message says what happened.
        _dwell.CancelPending();
        ItemRows.Clear();
    }

    /// <summary>
    /// The Tags section of the sidebar: every tag in use, with the number of
    /// unread articles carrying it.
    ///
    /// DeleteUnusedAsync runs first so the section cannot show a tag with
    /// nothing in it. Retention already calls it when it prunes articles, but
    /// a tag can also be emptied by unsubscribing from the only feed that
    /// carried its articles, which does not go through retention at all.
    ///
    /// An empty section renders as nothing: SidebarSection.IsVisible hides the
    /// header when there are no nodes, so a profile with no tags looks exactly
    /// as it did before this feature existed.
    /// </summary>
    private async Task<SidebarSection> BuildTagsSectionAsync()
    {
        await _services.Tags.DeleteUnusedAsync();

        var section = new SidebarSection { Title = "Tags" };

        foreach (var tag in await _services.Tags.GetUsageAsync())
        {
            section.Nodes.Add(new FeedTreeNode
            {
                Title = tag.Name,
                Kind = FeedTreeNodeKind.Tag,
                TagName = tag.Name,
                UnreadCount = tag.UnreadCount
            });
        }

        return section;
    }

    private async void OnRenameTagClicked(object? sender, RoutedEventArgs e)
    {
        var name = TagFromSender(sender) ?? SelectedFeedNode?.TagName;
        if (name is null) return;

        await RunGuardedAsync(() => RenameTagAsync(name), "rename this tag");
    }

    private async void OnDeleteTagClicked(object? sender, RoutedEventArgs e)
    {
        var name = TagFromSender(sender) ?? SelectedFeedNode?.TagName;
        if (name is null) return;

        await RunGuardedAsync(() => DeleteTagAsync(name), "delete this tag");
    }

    /// <summary>
    /// The tag a sidebar context-menu click came from. Null for the toolbar
    /// buttons, whose DataContext is the window rather than a row, which is
    /// why both handlers above fall back to the selection.
    /// </summary>
    private static string? TagFromSender(object? sender) =>
        ((sender as Avalonia.StyledElement)?.DataContext as FeedTreeNode)?.TagName;

    /// <summary>
    /// Renames a tag across every article carrying it.
    ///
    /// Renaming onto an existing tag merges the two (see
    /// TagRepository.RenameAsync), which is what "call these the same thing"
    /// means; the status line says so rather than letting the tag count drop
    /// by one with no explanation. An empty or invalid new name is refused
    /// with the rule that refused it, not silently ignored.
    /// </summary>
    public async Task RenameTagAsync(string oldName)
    {
        var dialog = new InputDialog("Rename Tag", "New name for this tag", oldName);
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } entered) return;

        if (!TagName.TryNormalise(entered, out var newName, out var error))
        {
            StatusMessage = error ?? "A tag name cannot be blank.";
            return;
        }

        // Exactly the same name, spelling included, is a dialog the user
        // dismissed with OK rather than an edit. A change of case is not this
        // case and does go through, since the stored spelling is what the
        // sidebar shows.
        if (oldName == newName) return;

        var existing = await _services.Tags.GetAllAsync();
        var merging = existing.Any(t => TagName.AreSame(t, newName) && !TagName.AreSame(t, oldName));

        if (!await _services.Tags.RenameAsync(oldName, newName))
        {
            StatusMessage = $"There is no tag called {oldName} any more.";
            return;
        }

        // Point the selection at the new name before the reload, so the tree
        // rebuild repoints onto the renamed row rather than losing the
        // selection: IsSameRow matches a tag by name, and the old name no
        // longer exists.
        if (SelectedFeedNode is { Kind: FeedTreeNodeKind.Tag } node && TagName.AreSame(node.TagName, oldName))
            _renamedTagToReselect = newName;

        await LoadFeedTreeAsync();
        if (SelectedFeedNode?.Kind == FeedTreeNodeKind.Tag) await LoadItemsAsync();

        StatusMessage = merging
            ? $"Merged {oldName} into {newName}."
            : $"Renamed {oldName} to {newName}.";
    }

    /// <summary>
    /// Removes a tag from every article carrying it, after confirming. The
    /// articles are not touched, and the confirmation says so in as many
    /// words: "delete" next to a count of articles reads like a bulk delete,
    /// which is exactly what this is not.
    /// </summary>
    public async Task DeleteTagAsync(string name)
    {
        var usage = (await _services.Tags.GetUsageAsync())
            .FirstOrDefault(t => TagName.AreSame(t.Name, name));

        var count = usage?.ArticleCount ?? 0;
        var articles = count == 1 ? "article" : "articles";

        var confirm = new ConfirmDialog(
            "Delete Tag",
            $"Remove the tag \"{name}\" from {count} {articles}? " +
            "The articles themselves are kept.",
            "Delete tag");
        await confirm.ShowDialog(this);
        if (!confirm.Confirmed) return;

        await _services.Tags.DeleteAsync(name);

        // Drop the selection if it pointed at the tag that just went away,
        // before the reload: leaving it set would leave the middle pane
        // querying a tag no article carries, showing nothing, with no row left
        // in the tree to click away from.
        if (SelectedFeedNode is { Kind: FeedTreeNodeKind.Tag } node && TagName.AreSame(node.TagName, name))
            SelectedFeedNode = null;

        await LoadFeedTreeAsync();
        StatusMessage = $"Deleted the tag {name}.";
    }
}
