using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Core.Sync;
using LucidReader.Models;
using MarkdownViewer.Models;
using MarkdownViewer.Services;

namespace LucidReader.Views;

/// <summary>
/// The window is self-bound (DataContext = this) rather than backed by a
/// separate view model, matching lucidVIEW's MainWindow. Avalonia's
/// AvaloniaObject already implements INotifyPropertyChanged, so a `new` event
/// here hides rather than overrides that base implementation. Verified
/// through the Mostlylucid.Avalonia.UITesting harness (--ux-repl), not a
/// unit test that constructs a Window: see task-6-report.md for the
/// transcript, including the ObservableCollection-binding check.
///
/// This file (Task 6) provides the shell: named controls, theme application
/// and feed-tree loading. The article properties and every ICommand are
/// still stubs here; Tasks 7-11 fill them in in the sibling partial files
/// (MainWindow.Items.cs, MainWindow.Reading.cs, MainWindow.Actions.cs).
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ReaderServices _services;
    private readonly ThemeService _theme;
    private readonly Action<ReaderSettings> _onSettingsChanged;

    private FeedTreeNode? _selectedFeedNode;
    private ItemRow? _selectedItemRow;
    private string _searchText = string.Empty;
    private string _statusMessage = string.Empty;
    private ItemFilter _filter = ItemFilter.All;

    /// <summary>
    /// This constructor is the reason the build prints
    /// "AVLN3001: XAML resource ... won't be reachable via runtime loader,
    /// as no public constructor was found". MainWindow has no public
    /// parameterless constructor because App.axaml.cs constructs it directly
    /// with a ReaderServices instance (the composition root), never through
    /// AvaloniaXamlLoader's own activation path. That is expected and safe
    /// for the shipped app. The cost: the XAML previewer and hot reload for
    /// MainWindow.axaml do not work, because both rely on the loader being
    /// able to instantiate the class itself. Do not "fix" this by adding a
    /// parameterless constructor just to silence the warning; that would
    /// let the window be constructed without a ReaderServices and crash on
    /// first use of _services.
    ///
    /// InitializeComponent below is the one the XAML compiler generates, which
    /// both loads the XAML and assigns every x:Name backing field. Do not add a
    /// hand-written `private void InitializeComponent() =>
    /// AvaloniaXamlLoader.Load(this);` here: it does not override the generated
    /// method, it shadows it, so the XAML still loads but every named field
    /// stays null. This file carried exactly that for several tasks, and the
    /// same mistake in FeedSettingsDialog crashed the app on open.
    /// </summary>
    public MainWindow(ReaderServices services)
    {
        _services = services;
        InitializeComponent();
        DataContext = this;

        FetchFullArticleCommand = new RelayCommand(FetchFullArticleAsync);
        ConfigurePlatformKeyBindings();

        // Before the window is shown, and before InstallMenus, so the toolbar
        // margin and the extended-client-area hints are already the right ones
        // for this platform the first time anything measures. See
        // ConfigurePlatformChrome in MainWindow.Layout.cs.
        ConfigurePlatformChrome();

        // After ConfigurePlatformKeyBindings, which resolves the command
        // modifier the menu accelerators are built from, and after
        // InitializeComponent, which is what gives InstallWindowMenu a
        // WindowMenu to fill.
        InstallMenus();

        // FindControl rather than the generated ReadingPane field only because
        // the pane is optional to this constructor: it is the one named control
        // whose absence should not throw. The generated fields are populated by
        // now, so anything else can be reached directly.
        var readingPane = this.FindControl<Mostlylucid.LucidView.Markdown.LucidMarkdownView>("ReadingPane");
        if (readingPane is not null)
            readingPane.LinkClick += OnArticleLinkClicked;

        _theme = new ThemeService(Application.Current!);
        WatchReadingPaneSize();
        ApplySettings(_services.Settings);

        _onSettingsChanged = settings => Dispatcher.UIThread.Post(() => ApplySettings(settings));
        _services.SettingsChanged += _onSettingsChanged;

        // Wrapped, unlike the bare `async (_, _) => await OnOpenedAsync()`
        // this replaces. That is an async void over a database read at the
        // one moment the window is coming up: anything thrown past it reached
        // the synchronization context unhandled and killed the app during
        // window-open, with no window on screen to show a message in.
        Opened += async (_, _) =>
        {
            try
            {
                await OnOpenedAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = "Could not finish opening the reader: " + ex.Message;
            }
        };

        Closing += OnWindowClosing;

        // The system appearance can change while the app is running, and the
        // reading pane does not follow it on its own. Avalonia flips the
        // variant-scoped ThemeDictionaries by itself, so the panes go light,
        // but the six Color resources LiveMarkdown reads for code blocks and
        // blockquotes are pushed imperatively by ThemeService.ApplyDefinition
        // and were only ever pushed from this constructor and on a settings
        // change. The result was a half-stale theme: light panes with a dark
        // code palette and dark text on it, unreadable, and the same in
        // reverse. RefreshAutoTheme exists exactly for this and had no caller.
        if (Application.Current is { } app)
        {
            _onActualThemeVariantChanged = (_, _) => _theme.RefreshAutoTheme();
            app.ActualThemeVariantChanged += _onActualThemeVariantChanged;
        }
    }

    private EventHandler? _onActualThemeVariantChanged;
    private int _preparedForShutdown;

    /// <summary>
    /// Set by App.axaml.cs immediately before it asks the lifetime to shut
    /// down, so the close that follows is known to be a real quit and is not
    /// diverted into hiding the window.
    ///
    /// Without it, "keep running in the menu bar" would make the app
    /// unquittable: Quit closes the window, the Closing handler below cancels
    /// the close, and the shutdown the lifetime was in the middle of never
    /// gets its window closed.
    /// </summary>
    private bool _quitting;

    public void MarkQuitting() => _quitting = true;

    /// <summary>
    /// Closing the window either quits mylo or hides it, depending on the
    /// setting, and the difference matters more than it looks.
    ///
    /// Hiding must NOT run <see cref="PrepareForShutdown"/>. That method
    /// cancels the dwell, stops health monitoring, disposes the health
    /// cancellation source and disposes all four coordinators, and it is
    /// deliberately one-shot; a hide that ran it would leave a window that
    /// can be reopened from the status item but has no working search, no
    /// image resolution and no health readout, with nothing on screen to say
    /// so. So the cancel happens first and returns, and only a close that is
    /// really the end reaches the teardown.
    ///
    /// The status item is required, not merely preferred: with the setting on
    /// and no status item (a Linux session with no tray, an asset that failed
    /// to load) hiding the window would leave mylo running with no way to
    /// bring it back and no way to quit it. In that case the close is allowed
    /// through, which is the behaviour the app had before any of this.
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_quitting && _services.Settings.CloseKeepsRunning && HasUsableStatusItem)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        PrepareForShutdown();
    }

    /// <summary>
    /// Everything that has to stop before ReaderServices is disposed.
    ///
    /// This was the body of the Closing handler, and that ordering only holds
    /// when the user closes the window. On Cmd+Q, on Quit from the dock, and
    /// on an OS logout, the platform raises ShutdownRequested FIRST and
    /// ClassicDesktopStyleApplicationLifetime.DoShutdown closes the windows
    /// afterwards - so on the most common macOS quit path disposal began
    /// before any of this had run. App.axaml.cs now calls this from its
    /// ShutdownRequested handler before disposing, and Closing still calls it
    /// for the close-the-window path.
    ///
    /// Idempotent by the interlocked flag, because on Cmd+Q both callers fire.
    /// </summary>
    public void PrepareForShutdown()
    {
        if (Interlocked.Exchange(ref _preparedForShutdown, 1) != 0) return;

        _services.SettingsChanged -= _onSettingsChanged;

        if (_onNetworkAvailabilityChanged is not null)
            _services.NetworkAvailabilityChanged -= _onNetworkAvailabilityChanged;

        if (_onActualThemeVariantChanged is not null && Application.Current is { } app)
            app.ActualThemeVariantChanged -= _onActualThemeVariantChanged;

        // A pending dwell must not fire a write against ReaderServices
        // after this window closes: an in-flight dwell could otherwise call
        // SetReadAsync against a disposing (or already-disposed) store.
        _dwell.CancelPending();

        // Before the coordinators below: this unsubscribes from
        // FeedRefreshService.Completed, and that service outlives this window
        // on the close-to-status-item path.
        StopNotifications();
        StopRefreshProgress();
        StopLiveUpdates();

        // Same reason as the dwell above: a health tick that fired after
        // this point would read _services.Feeds against a store that is
        // already being disposed.
        StopHealthMonitoring();
        DisposeHealthMonitoring();

        _searchCoordinator.Dispose();
        _iconCoordinator.Dispose();
        _thumbnailCoordinator.Dispose();
        _heroCoordinator.Dispose();
    }


    /// <summary>
    /// Two collapsible groups, the way Mail groups Favourites and mailboxes:
    /// Favourites (the three smart rows) and Feeds (folders with their feeds
    /// nested, then unfoldered feeds). Replaces the flat FeedNodes binding
    /// from Task 6.
    /// </summary>
    public ObservableCollection<SidebarSection> Sidebar { get; } = [];

    /// <summary>
    /// Every node across every section, flattened. AdjustUnreadCount and
    /// BuildQuery both need to look at "all nodes" without caring which
    /// section a node lives in; keeping one flat view here means neither has
    /// to walk Sidebar's two levels itself.
    /// </summary>
    private IEnumerable<FeedTreeNode> AllFeedTreeNodes => Sidebar.SelectMany(s => s.Nodes);

    /// <summary>
    /// The item list. A BatchObservableCollection rather than a plain
    /// ObservableCollection so a whole page of rows arrives as one change:
    /// see the summary on that class for what the row-at-a-time version cost.
    /// </summary>
    public BatchObservableCollection<ItemRow> ItemRows { get; } = [];

    public double ColumnWidth => _services.Settings.ColumnWidth;

    public FeedTreeNode? SelectedFeedNode
    {
        get => _selectedFeedNode;
        set
        {
            if (ReferenceEquals(_selectedFeedNode, value)) return;

            // The sidebar has no single shared ListBox to own selection, so
            // this property is the one source of truth for which row looks
            // selected; each FeedTreeNode's own IsSelected flag just mirrors it.
            if (_selectedFeedNode is not null) _selectedFeedNode.IsSelected = false;
            _selectedFeedNode = value;
            if (_selectedFeedNode is not null) _selectedFeedNode.IsSelected = true;

            Raise();
            Raise(nameof(IsFeedSelected));
            Raise(nameof(IsTagSelected));
            Raise(nameof(IsPausedFeedSelected));

            // The update line describes the selected feed, so it changes with
            // the selection and is hidden entirely when the selection is not a
            // feed. See MainWindow.FeedUpdate.cs.
            RefreshFeedUpdateLine();

            // The Feed menu's items are gated on the same two answers the two
            // properties above give, and a NativeMenuItem has no DataContext
            // to bind them through, so it has to be told.
            UpdateMenuEnablement();

            // The scope toggle describes the selection, so both its enabled
            // state and its label change with it.
            Raise(nameof(CanScopeSearchToSelection));
            Raise(nameof(SearchScopeLabel));

            // The refresh control is offered for grouping rows now, not only
            // for feeds, so both of these change with any selection rather
            // than only when a feed is picked. See MainWindow.FeedUpdate.cs.
            Raise(nameof(CanRefreshSelection));
            Raise(nameof(IsFeedUpdateStripVisible));

            // A feed click must win over a search debounce that started
            // earlier but has not yet elapsed. LoadSequenceGuard alone
            // can't guarantee that: it orders by when Begin() is called,
            // and the debounce (see MainWindow.Search.cs) takes its ticket
            // up to 250ms after the keystroke that started it, so a stale
            // debounce could still take a LATER ticket than this load and
            // win the guard on its own terms. Cancelling here stops that
            // search before it can ever reach the guard. Clearing the
            // search box too: leaving a stale query visible while showing
            // a feed's articles would be its own kind of lie.
            _searchCoordinator.CancelForFeedChange();
            Raise(nameof(IsShowingSearchResults));
            if (_searchText.Length > 0)
            {
                _searchText = string.Empty;
                Raise(nameof(SearchText));
            }

            _ = LoadItemsAsync();
        }
    }

    /// <summary>
    /// True only when the sidebar selection is an actual feed. Folders and the
    /// smart rows (All items, Unread, Starred) select fine but have no FeedId,
    /// so the feed-scoped toolbar actions stay disabled on them.
    /// </summary>
    public bool IsFeedSelected => SelectedFeedNode?.FeedId is not null;

    /// <summary>
    /// True only when the sidebar selection is a tag row. The two tag-only
    /// toolbar buttons bind their visibility to this, the way the Resume
    /// button binds to IsPausedFeedSelected: collapsed rather than disabled,
    /// because renaming and deleting a tag are meaningless anywhere else in
    /// the tree.
    /// </summary>
    public bool IsTagSelected => SelectedFeedNode?.Kind == FeedTreeNodeKind.Tag;

    /// <summary>
    /// Wired from a PointerPressed on each sidebar row's Border (see
    /// MainWindow.axaml). Not a ListBox SelectedItem binding: the sidebar has
    /// one ItemsControl per section, and two ListBoxes TwoWay-bound to the
    /// same SelectedFeedNode would each try to reject the other's selection.
    /// </summary>
    private void OnFeedNodePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if ((sender as Avalonia.StyledElement)?.DataContext is FeedTreeNode node)
            SelectedFeedNode = node;
    }

    public ItemRow? SelectedItemRow
    {
        get => _selectedItemRow;
        set
        {
            if (ReferenceEquals(_selectedItemRow, value)) return;
            _selectedItemRow = value;
            Raise();
            UpdateMenuEnablement();
            _ = OnItemSelectedAsync(value);
        }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (_searchText == value) return; _searchText = value; Raise(); _ = OnSearchTextChangedAsync(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage == value) return; _statusMessage = value; Raise(); }
    }

    public bool IsFilterAll
    {
        get => _filter == ItemFilter.All;
        set { if (value) SetFilter(ItemFilter.All); }
    }

    public bool IsFilterUnread
    {
        get => _filter == ItemFilter.Unread;
        set { if (value) SetFilter(ItemFilter.Unread); }
    }

    public bool IsFilterStarred
    {
        get => _filter == ItemFilter.Starred;
        set { if (value) SetFilter(ItemFilter.Starred); }
    }

    private void SetFilter(ItemFilter filter)
    {
        if (_filter == filter) return;
        _filter = filter;
        Raise(nameof(IsFilterAll));
        Raise(nameof(IsFilterUnread));
        Raise(nameof(IsFilterStarred));

        // Search obeys the segment (see SearchQueryBuilder), so changing the
        // segment while results are on screen re-runs the query rather than
        // replacing the results with the feed selection's items. Dropping
        // back to LoadItemsAsync would look like the search had been
        // cancelled by a click on a filter chip.
        if (IsShowingSearchResults && !string.IsNullOrWhiteSpace(SearchText))
        {
            RerunSearchIfShowing();
            return;
        }

        _ = LoadItemsAsync();
    }

    protected ItemFilter CurrentFilter => _filter;

    private async Task OnOpenedAsync()
    {
        var hasStartupWarning = false;
        if (_services.StartupWarning is { } warning)
        {
            StatusMessage = "Storage maintenance could not run: " + warning.Message;
            hasStartupWarning = true;
        }

        await LoadFeedTreeAsync();

        // Open on the unread list rather than on everything.
        //
        // The reader opened with no sidebar selection, which reads as "All
        // items" and shows every article ever stored, most of them already
        // read. That is the wrong first screen for a feed reader: what someone
        // opens one to find out is what is new, and with retention keeping
        // months of history the unread articles were a minority of the list
        // they were handed.
        //
        // After LoadFeedTreeAsync, necessarily: the Unread row this selects
        // does not exist until the tree has been built.
        SelectUnreadList();

        // And then open the first article, so the reading pane is not blank.
        //
        // Awaited, and the load awaited before it, because selecting the
        // sidebar row only STARTS the item load: SelectedFeedNode's setter
        // fires LoadItemsAsync without awaiting it, so ItemRows is still
        // empty on the line after. Reading the first row without this would
        // reliably find nothing and quietly do nothing at all.
        await LoadItemsAsync();
        await ShowFirstArticleOnOpenAsync();

        // Said once, on the one launch it can be true, and only when nothing
        // more important is already on the status line: a first run that
        // arrives with five subscriptions nobody typed should say where they
        // came from.
        if (!hasStartupWarning && _services.SeededDefaultFeedCount > 0)
            StatusMessage = LucidReader.Core.Feeds.FirstRunSeedPolicy
                .DescribeSeed(_services.SeededDefaultFeedCount);

        // After the tree, so the first readout already knows how many feeds
        // came back auto-paused. StartHealthMonitoring only schedules the
        // repeats; the first check is this explicit one.
        //
        // Skipped entirely when a startup warning is on screen. A failed
        // database maintenance run is reported exactly once, here, and any
        // health line would overwrite it before the user could read it. The
        // health readout is not lost, only deferred: the first timer tick
        // takes over 30 seconds later.
        if (!hasStartupWarning) await CheckHealthAsync();
        StartHealthMonitoring();

        // After the tree, for the same reason the health check is: the first
        // unread count the status item is given has to be a real one, and the
        // tree is where that number comes from.
        StartNotifications();

        // After the tree, like everything else here: SyncRefreshState walks
        // the feed rows, and there are none until LoadFeedTreeAsync has run.
        StartRefreshProgress();

        // After the tree too: a refresh completing before the first list is
        // on screen has nothing to merge into.
        StartLiveUpdates();
        UpdateStatusItemUnreadCount();

        // PauseWhenOffline finally does something, so say so on the one
        // occasion it is worth saying: coming up with no network at all.
        // OfflineGate returns an empty string in every other case, including
        // the ordinary one, so this cannot stamp over anything.
        if (!hasStartupWarning && !_services.IsNetworkAvailable)
        {
            var offlineText = OfflineGate.DescribeTransition(
                _services.Settings.PauseWhenOffline, false);
            if (offlineText.Length > 0) StatusMessage = offlineText;
        }

        _onNetworkAvailabilityChanged = available => Dispatcher.UIThread.Post(() =>
        {
            var text = OfflineGate.DescribeTransition(
                _services.Settings.PauseWhenOffline, available);
            if (text.Length > 0) StatusMessage = text;
        });
        _services.NetworkAvailabilityChanged += _onNetworkAvailabilityChanged;
    }

    private Action<bool>? _onNetworkAvailabilityChanged;

    /// <summary>
    /// Runs on startup and again on every settings change (via SettingsChanged
    /// above), so the reading pane picks up a new font size, line height, code
    /// size or column width without a restart. Everything here must be
    /// idempotent for that reason.
    /// </summary>
    private void ApplySettings(ReaderSettings settings)
    {
        _theme.ApplyTheme(Enum.TryParse<AppTheme>(settings.Theme, true, out var parsed)
            ? parsed
            : AppTheme.Auto);

        RestoreLayoutMode(settings);
        ApplyReadingColumnWidth();
        ApplyReadingTypography(settings);

        // ColumnWidth is the saved preference; ResolvedColumnWidth is what the
        // pane can actually show right now. Both are raised because both are
        // public and either could be bound; note that Window already has its
        // own FontSize, so the typography values are deliberately not named
        // after their settings - a binding to "FontSize" would silently mean
        // the window's, not the article's.
        Raise(nameof(ColumnWidth));
        Raise(nameof(ResolvedColumnWidth));
        Raise(nameof(ReadingFontSize));
        Raise(nameof(ReadingLineHeight));
        Raise(nameof(ReadingCodeFontSize));
    }

    /// <summary>
    /// Rebuilds the sidebar into two sections: Favourites (the three smart
    /// rows) and Feeds (folders with their feeds nested under them, then
    /// feeds with no folder). Still assembled from the same flat queries as
    /// Task 6; only the grouping into SidebarSection is new here.
    /// </summary>
    public async Task LoadFeedTreeAsync()
    {
        var folders = await _services.Folders.GetAllAsync();
        var feeds = await _services.Feeds.GetAllAsync();

        // One query for every feed's count, not one query per feed. This
        // method runs on every refresh sweep, every notification sweep, every
        // tag edit and every feed menu action, so the loop it replaces was a
        // round trip per subscription each time. See
        // ItemRepository.GetUnreadCountsByFeedAsync.
        //
        // A feed with nothing unread is absent from the dictionary rather than
        // present as zero, which is why every read below goes through
        // GetValueOrDefault.
        var unreadByFeed = await _services.Items.GetUnreadCountsByFeedAsync();

        var favourites = new SidebarSection { Title = "Favourites" };
        favourites.Nodes.Add(new FeedTreeNode
        {
            Title = "All items", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.All
        });
        favourites.Nodes.Add(new FeedTreeNode
        {
            Title = "Unread", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.Unread,
            // Not unreadByFeed.Values.Sum(). An article carried by two
            // subscriptions has two rows and one entry in the Unread list, so
            // a sum of per-feed counts is a number that list can never reach.
            UnreadCount = await _services.Items.GetUnreadTotalAsync()
        });
        favourites.Nodes.Add(new FeedTreeNode
        {
            Title = "Starred", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.Starred
        });

        var feedsSection = new SidebarSection { Title = "Feeds" };
        foreach (var folder in folders)
        {
            var children = feeds.Where(f => f.FolderId == folder.Id).ToList();

            feedsSection.Nodes.Add(new FeedTreeNode
            {
                Title = folder.Name,
                Kind = FeedTreeNodeKind.Folder,
                FolderId = folder.Id,
                // Deduplicated for the same reason the Unread row above is:
                // two subscriptions to one site can easily sit in the same
                // folder.
                UnreadCount = await _services.Items.GetUnreadTotalAsync(folder.Id)
            });

            foreach (var feed in children) feedsSection.Nodes.Add(ToNode(feed, unreadByFeed));
        }

        foreach (var feed in feeds.Where(f => f.FolderId is null))
            feedsSection.Nodes.Add(ToNode(feed, unreadByFeed));

        var tagsSection = await BuildTagsSectionAsync();

        Sidebar.Clear();
        Sidebar.Add(favourites);
        Sidebar.Add(feedsSection);

        // Only when there is something in it. SidebarSection.IsVisible would
        // already hide an empty section's header and rows, so this is not
        // about what is drawn; it is so that "does this profile use tags at
        // all?" is answerable from the sidebar's own shape rather than only
        // from what happens to be painted. A profile that has never tagged
        // anything has the two sections it always had.
        if (tagsSection.Nodes.Count > 0) Sidebar.Add(tagsSection);

        RepointSelectionAfterTreeReload();

        // After the repoint, because the line reads its timestamps off the
        // selected node and the repoint is what swaps in the freshly loaded
        // one. This is the call that makes the line correct again after a
        // refresh: "Updated 2 hours ago" becomes "Updated just now".
        RefreshFeedUpdateLine();

        // The one place every unread count in the app is recomputed, so the
        // one place the status item can be kept in step without a second
        // count to maintain.
        UpdateStatusItemUnreadCount();

        // Fired without awaiting: the tree is already on screen with text
        // and the neutral placeholder glyph, and favicons fill in as they
        // resolve. See MainWindow.Images.cs.
        _ = ResolveSidebarIconsAsync();
    }

    /// <summary>
    /// Whether two nodes stand for the same sidebar row across a tree reload.
    /// LoadFeedTreeAsync throws every FeedTreeNode away and builds new ones,
    /// so reference equality says nothing; identity is the feed, the folder or
    /// the smart filter the row stands for. Static and node-only so it can be
    /// tested without a Window.
    /// </summary>
    internal static bool IsSameRow(FeedTreeNode a, FeedTreeNode b)
    {
        if (a.Kind != b.Kind) return false;

        return a.Kind switch
        {
            FeedTreeNodeKind.Feed => a.FeedId is not null && a.FeedId == b.FeedId,
            FeedTreeNodeKind.Folder => a.FolderId is not null && a.FolderId == b.FolderId,
            // A tag's identity is its name, matched the way the database
            // matches it (TagName.AreSame), so a reload that picked up a
            // different spelling of the same tag still repoints onto it. A
            // rename deliberately does NOT match: the old name is gone, so the
            // selection falls back to nothing rather than to a row that no
            // longer stands for what was selected - and RenameTagAsync
            // reselects the new name itself.
            FeedTreeNodeKind.Tag => a.TagName is not null && TagName.AreSame(a.TagName, b.TagName),
            _ => a.SmartFilter == b.SmartFilter
        };
    }

    /// <summary>
    /// Carries the selection across a tree reload and re-raises the properties
    /// whose answers the reload can have changed.
    ///
    /// Two things go wrong without this. The selection highlight is lost after
    /// any action that reloads the tree, because the highlighted node object
    /// no longer exists. And IsPausedFeedSelected goes stale: the old node is
    /// a frozen snapshot, so a feed auto-paused in the background while it was
    /// selected never grows a Resume button, and a paused feed whose refresh
    /// just succeeded keeps one.
    ///
    /// Assigns the backing field rather than going through SelectedFeedNode's
    /// setter on purpose. That setter cancels a running search, clears the
    /// search box and kicks off LoadItemsAsync; none of that is wanted for
    /// what is the same row the user already had selected, and the item reload
    /// in particular would race whatever the caller does next.
    /// </summary>
    private void RepointSelectionAfterTreeReload()
    {
        if (_selectedFeedNode is not { } previous) return;

        // RedirectAfterRename (MainWindow.Tags.cs) is the identity function
        // except immediately after a tag rename, where the row to look for is
        // the one carrying the new name.
        var target = RedirectAfterRename(previous);
        var replacement = AllFeedTreeNodes.FirstOrDefault(n => IsSameRow(target, n));

        previous.IsSelected = false;
        _selectedFeedNode = replacement;
        if (replacement is not null) replacement.IsSelected = true;

        Raise(nameof(SelectedFeedNode));
        Raise(nameof(IsFeedSelected));
        Raise(nameof(IsTagSelected));
        Raise(nameof(IsPausedFeedSelected));
        Raise(nameof(CanScopeSearchToSelection));
        Raise(nameof(SearchScopeLabel));

        // A reload can turn a healthy feed into a paused one and back, so the
        // Feed menu has to be re-gated here too, not only on a click.
        UpdateMenuEnablement();
    }

    private static FeedTreeNode ToNode(Feed feed, IReadOnlyDictionary<long, int> unread) => new()
    {
        Title = feed.DisplayTitle,
        Kind = FeedTreeNodeKind.Feed,
        FeedId = feed.Id,
        FolderId = feed.FolderId,
        UnreadCount = unread.GetValueOrDefault(feed.Id),
        ConsecutiveFailures = feed.ConsecutiveFailures,
        LastError = feed.LastError,
        IsAutoPaused = feed.AutoPausedUtc is not null,
        IsEnabled = feed.IsEnabled,
        IconUrl = feed.IconPath,
        LastFetchedUtc = feed.LastFetchedUtc,
        LastSuccessUtc = feed.LastSuccessUtc,
        NextDueUtc = feed.NextDueUtc,
        IsScraped = feed.IsScraped
    };

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
