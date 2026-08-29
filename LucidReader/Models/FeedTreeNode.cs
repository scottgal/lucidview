using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    public bool HasUnread => _unreadCount > 0;
    public string UnreadLabel => _unreadCount > 0 ? _unreadCount.ToString() : string.Empty;
    public bool HasProblem => ConsecutiveFailures > 0 || IsAutoPaused;

    /// <summary>Indent for feeds inside a folder. Folders and smart rows sit flush.</summary>
    public double Indent => Kind == FeedTreeNodeKind.Feed && FolderId is not null ? 16 : 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum FeedTreeNodeKind { Smart, Folder, Feed }
