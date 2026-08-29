using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LucidReader.Models;

/// <summary>
/// A collapsible group in the sidebar, the way Mail groups Favourites and
/// mailboxes. Subscribes to its nodes so a header count cannot go stale, which
/// matters because unread counts change on every article read.
/// </summary>
public sealed class SidebarSection : INotifyPropertyChanged
{
    private bool _isExpanded = true;

    public SidebarSection()
    {
        Nodes.CollectionChanged += OnNodesChanged;
    }

    public required string Title { get; init; }

    public ObservableCollection<FeedTreeNode> Nodes { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            Raise();
            Raise(nameof(IsCollapsed));
        }
    }

    /// <summary>
    /// Convenience for the chevron's rotation style trigger. A binding-syntax
    /// negation (<c>!IsExpanded</c>) is avoided here since this project runs
    /// with reflection bindings (AvaloniaUseCompiledBindingsByDefault is
    /// false); an explicit property is unambiguous either way.
    /// </summary>
    public bool IsCollapsed => !_isExpanded;

    /// <summary>Small-caps-style header text, the way Mail labels "FAVOURITES".</summary>
    public string HeaderText => Title.ToUpperInvariant();

    /// <summary>
    /// Excludes Folder-kind nodes: a folder's own UnreadCount is already the
    /// sum of its children's (see MainWindow.LoadFeedTreeAsync), so summing
    /// every node in the section including the folder itself would double
    /// count every feed that sits inside one.
    /// </summary>
    public int UnreadCount => Nodes.Where(n => n.Kind != FeedTreeNodeKind.Folder).Sum(n => n.UnreadCount);

    public string UnreadLabel => UnreadCount > 0 ? UnreadCount.ToString() : string.Empty;

    /// <summary>An empty section hides its header rather than showing a bare label.</summary>
    public bool IsVisible => Nodes.Count > 0;

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var node in e.OldItems?.OfType<FeedTreeNode>() ?? [])
            node.PropertyChanged -= OnNodeChanged;

        foreach (var node in e.NewItems?.OfType<FeedTreeNode>() ?? [])
            node.PropertyChanged += OnNodeChanged;

        RaiseCounts();
        Raise(nameof(IsVisible));
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FeedTreeNode.UnreadCount)) RaiseCounts();
    }

    private void RaiseCounts()
    {
        Raise(nameof(UnreadCount));
        Raise(nameof(UnreadLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
