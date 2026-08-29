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
    /// </summary>
    public MainWindow(ReaderServices services)
    {
        _services = services;
        InitializeComponent();
        DataContext = this;

        FetchFullArticleCommand = new RelayCommand(FetchFullArticleAsync);

        // this.ReadingPane (the generated named-field access) is null here:
        // this window's InitializeComponent only calls AvaloniaXamlLoader.Load,
        // it does not go through the generated overload that also populates
        // named fields, so the control has to be looked up explicitly.
        var readingPane = this.FindControl<Mostlylucid.LucidView.Markdown.LucidMarkdownView>("ReadingPane");
        if (readingPane is not null)
            readingPane.LinkClick += OnArticleLinkClicked;

        _theme = new ThemeService(Application.Current!);
        ApplySettings(_services.Settings);

        _onSettingsChanged = settings => Dispatcher.UIThread.Post(() => ApplySettings(settings));
        _services.SettingsChanged += _onSettingsChanged;

        Opened += async (_, _) => await OnOpenedAsync();
        Closing += (_, _) =>
        {
            _services.SettingsChanged -= _onSettingsChanged;

            // A pending dwell must not fire a write against ReaderServices
            // after this window closes: App.axaml.cs disposes Services on
            // shutdown with no coordination beyond window-closing order, so
            // an in-flight dwell could otherwise call SetReadAsync against a
            // disposing (or already-disposed) store.
            _dwell.CancelPending();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public ObservableCollection<FeedTreeNode> FeedNodes { get; } = [];
    public ObservableCollection<ItemRow> ItemRows { get; } = [];

    public double ColumnWidth => _services.Settings.ColumnWidth;

    public FeedTreeNode? SelectedFeedNode
    {
        get => _selectedFeedNode;
        set
        {
            if (ReferenceEquals(_selectedFeedNode, value)) return;
            _selectedFeedNode = value;
            Raise();
            _ = LoadItemsAsync();
        }
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
        if (_services.StartupWarning is { } warning)
            StatusMessage = "Storage maintenance could not run: " + warning.Message;

        await LoadFeedTreeAsync();
    }

    private void ApplySettings(ReaderSettings settings)
    {
        _theme.ApplyTheme(Enum.TryParse<AppTheme>(settings.Theme, true, out var parsed)
            ? parsed
            : AppTheme.Auto);
        Raise(nameof(ColumnWidth));
    }

    /// <summary>
    /// Rebuilds the whole tree: three smart rows, then folders with their feeds
    /// nested under them, then feeds with no folder.
    /// </summary>
    public async Task LoadFeedTreeAsync()
    {
        var folders = await _services.Folders.GetAllAsync();
        var feeds = await _services.Feeds.GetAllAsync();

        var unreadByFeed = new Dictionary<long, int>();
        foreach (var feed in feeds)
            unreadByFeed[feed.Id] = await _services.Items.GetUnreadCountAsync(feed.Id);

        FeedNodes.Clear();

        FeedNodes.Add(new FeedTreeNode
        {
            Title = "All items", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.All
        });
        FeedNodes.Add(new FeedTreeNode
        {
            Title = "Unread", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.Unread,
            UnreadCount = unreadByFeed.Values.Sum()
        });
        FeedNodes.Add(new FeedTreeNode
        {
            Title = "Starred", Kind = FeedTreeNodeKind.Smart, SmartFilter = ItemFilter.Starred
        });

        foreach (var folder in folders)
        {
            var children = feeds.Where(f => f.FolderId == folder.Id).ToList();

            FeedNodes.Add(new FeedTreeNode
            {
                Title = folder.Name,
                Kind = FeedTreeNodeKind.Folder,
                FolderId = folder.Id,
                UnreadCount = children.Sum(f => unreadByFeed.GetValueOrDefault(f.Id))
            });

            foreach (var feed in children) FeedNodes.Add(ToNode(feed, unreadByFeed));
        }

        foreach (var feed in feeds.Where(f => f.FolderId is null))
            FeedNodes.Add(ToNode(feed, unreadByFeed));
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
        IsEnabled = feed.IsEnabled
    };

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
