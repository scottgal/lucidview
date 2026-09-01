using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace LucidReader.Models;

/// <summary>
/// An ObservableCollection that can be refilled in one notification.
///
/// The item list is rebuilt by clearing it and adding the new rows one at a
/// time, and ObservableCollection raises a separate CollectionChanged for
/// every one of those adds. Avalonia treats each as its own change, so
/// selecting a feed did up to five hundred incremental container updates
/// where one rebuild would do - ItemQueryBuilder's page size is 500, and a
/// list that size is exactly when a user notices the difference. Every switch
/// of feed, folder, tag or search paid it.
///
/// ReplaceAll fills the underlying list directly and then raises a single
/// Reset, which is the notification "the contents are now different, look
/// again" and the one an ItemsControl handles by rebuilding once.
///
/// The cost of a Reset is that it carries no detail about what changed, so a
/// consumer that animates individual insertions cannot. Nothing here does:
/// the list is replaced wholesale on every load, which is precisely the case
/// Reset is for.
/// </summary>
public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Replaces the contents with <paramref name="items"/>, raising one Reset
    /// rather than one notification per row.
    ///
    /// CheckReentrancy first, exactly as the base class does before its own
    /// mutations: a handler that is in the middle of enumerating this
    /// collection must not have it rewritten underneath it, and the base's
    /// guard is what turns that into a clear exception rather than a
    /// confusing one from somewhere else.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();

        Items.Clear();
        foreach (var item in items) Items.Add(item);

        // Count and the indexer both changed, and both are raised before the
        // collection change, matching the order ObservableCollection itself
        // uses. A binding to Count that only saw the Reset would be updating
        // from a stale value. "Item[]" is the string WPF and Avalonia both
        // use for "any indexed value may have changed"; it is not a typo.
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
