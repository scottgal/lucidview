using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using LucidReader.Core.Storage;

namespace LucidReader.Models;

/// <summary>
/// One row in the feed tree. Covers four shapes: the three smart rows at the
/// top, a folder, and a feed. Kept as one type rather than a hierarchy because
/// the tree binds to a single flat ObservableCollection.
/// </summary>
public sealed class FeedTreeNode : INotifyPropertyChanged
{
    private int _unreadCount;
    private bool _isExpanded = true;
    private bool _isSelected;
    private string? _iconPath;

    public required string Title { get; init; }
    public FeedTreeNodeKind Kind { get; init; }
    public long? FeedId { get; init; }
    public long? FolderId { get; init; }
    public ItemFilter SmartFilter { get; init; }

    /// <summary>Populated for feed rows only, so the tree can show a warning.</summary>
    public int ConsecutiveFailures { get; init; }
    public string? LastError { get; init; }
    public bool IsAutoPaused { get; init; }
    public bool IsEnabled { get; init; } = true;

    public int UnreadCount
    {
        get => _unreadCount;
        set { if (_unreadCount == value) return; _unreadCount = value; Raise(); Raise(nameof(HasUnread)); Raise(nameof(UnreadLabel)); }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; Raise(); }
    }

    /// <summary>
    /// Drives the sidebar row's selected visual (Task 8a). The sidebar is
    /// rendered as one ItemsControl per section rather than one shared
    /// ListBox, since two ListBoxes TwoWay-bound to the same SelectedItem
    /// source fight over it; a plain bool per node sidesteps that. Owned
    /// exclusively by MainWindow.SelectedFeedNode's setter.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; Raise(); }
    }

    /// <summary>
    /// Remote favicon URL for a feed row (Task 8b's discovered/guessed icon,
    /// stored on <c>Feed.IconPath</c>). Set once when the node is built;
    /// never re-raised itself.
    /// </summary>
    public string? IconUrl { get; init; }

    /// <summary>
    /// Local cached path for the sidebar favicon. Starts null - the row
    /// renders immediately with the neutral placeholder - and is assigned
    /// later, on the UI thread, once MainWindow's background resolution pass
    /// (Task 8c) fetches it via ImageResolver. Must raise change
    /// notification: the row is already on screen when this is set.
    /// </summary>
    public string? IconPath
    {
        get => _iconPath;
        set { if (_iconPath == value) return; _iconPath = value; Raise(); Raise(nameof(HasIcon)); }
    }

    public bool HasIcon => !string.IsNullOrEmpty(_iconPath);

    /// <summary>
    /// Only feed rows ever carry an IconUrl (ToNode sets it nowhere else),
    /// so HasIcon is already false for a smart/folder row. This gates the
    /// neutral placeholder specifically: without it, a smart row like "All
    /// items" or a folder header would show an empty grey icon box it has
    /// no concept of, when the brief says those rows "keep their existing
    /// glyphs" - i.e. no icon slot at all, not a placeholder one.
    /// </summary>
    public bool ShowIconPlaceholder => Kind == FeedTreeNodeKind.Feed && !HasIcon;

    /// <summary>
    /// True only for a real feed row. Gates the feed-only context-menu items
    /// (MainWindow.axaml): every one of those handlers bails out on a null
    /// FeedId, so on a folder or a smart row they were offered but did
    /// nothing at all when clicked. Hiding them says so up front.
    /// </summary>
    public bool IsFeed => Kind == FeedTreeNodeKind.Feed;

    public bool HasUnread => _unreadCount > 0;
    public string UnreadLabel => _unreadCount > 0 ? _unreadCount.ToString() : string.Empty;
    public bool HasProblem => ConsecutiveFailures > 0 || IsAutoPaused;

    /// <summary>
    /// Tooltip for the sidebar's problem marker. An auto-paused feed says so
    /// first: LastError alone describes why the last attempt failed but not
    /// that the feed has been taken out of rotation entirely, and a feed can
    /// be paused with no error text recorded at all, which used to leave the
    /// marker with an empty tooltip and no explanation anywhere.
    /// </summary>
    public string ProblemTip => IsAutoPaused
        ? "Paused after repeated failures. " +
          (string.IsNullOrWhiteSpace(LastError) ? "No error was recorded." : LastError)
        : LastError ?? string.Empty;

    /// <summary>
    /// Left indent for feeds inside a folder. Folders and smart rows sit flush.
    /// Typed as a Thickness, not a bare double: Avalonia's reflection-based
    /// binding pipeline (compiled bindings are off for this project) does not
    /// coerce a double into a Thickness-typed target property such as
    /// Margin, so a double here would silently no-op and the tree would
    /// render flat with no visible folder nesting.
    /// </summary>
    public Thickness Indent => Kind == FeedTreeNodeKind.Feed && FolderId is not null
        ? new Thickness(16, 0, 0, 0)
        : default;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum FeedTreeNodeKind { Smart, Folder, Feed }
