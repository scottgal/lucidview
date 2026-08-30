using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
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

        Closing += (_, _) => PrepareForShutdown();

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

        if (_onActualThemeVariantChanged is not null && Application.Current is { } app)
            app.ActualThemeVariantChanged -= _onActualThemeVariantChanged;

        // A pending dwell must not fire a write against ReaderServices
        // after this window closes: an in-flight dwell could otherwise call
        // SetReadAsync against a disposing (or already-disposed) store.
        _dwell.CancelPending();

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

    public ObservableCollection<ItemRow> ItemRows { get; } = [];

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
            Raise(nameof(IsPausedFeedSelected));

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
    }

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

        var unreadByFeed = new Dictionary<long, int>();
        foreach (var feed in feeds)
            unreadByFeed[feed.Id] = await _services.Items.GetUnreadCountAsync(feed.Id);

        var favourites = new SidebarSection { Title = "Favourites" };
        favourites.Nodes.Add(new FeedTreeNode
        {
            Title = "All items", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.All
        });
        favourites.Nodes.Add(new FeedTreeNode
        {
            Title = "Unread", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.Unread,
            UnreadCount = unreadByFeed.Values.Sum()
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
                UnreadCount = children.Sum(f => unreadByFeed.GetValueOrDefault(f.Id))
            });

            foreach (var feed in children) feedsSection.Nodes.Add(ToNode(feed, unreadByFeed));
        }

        foreach (var feed in feeds.Where(f => f.FolderId is null))
            feedsSection.Nodes.Add(ToNode(feed, unreadByFeed));

        Sidebar.Clear();
        Sidebar.Add(favourites);
        Sidebar.Add(feedsSection);

        RepointSelectionAfterTreeReload();

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

        var replacement = AllFeedTreeNodes.FirstOrDefault(n => IsSameRow(previous, n));

        previous.IsSelected = false;
        _selectedFeedNode = replacement;
        if (replacement is not null) replacement.IsSelected = true;

        Raise(nameof(SelectedFeedNode));
        Raise(nameof(IsFeedSelected));
        Raise(nameof(IsPausedFeedSelected));
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
        IconUrl = feed.IconPath
    };

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
