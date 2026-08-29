namespace LucidReader.Services;

/// <summary>
/// Owns the single pending search-debounce token and the
/// "ItemRows currently holds search results" flag.
///
/// This exists to fix a real race: <see cref="LoadSequenceGuard"/> orders
/// strictly by when <c>Begin()</c> is called, but a search's debounce
/// delays that call by up to 250ms after the keystroke that triggered it.
/// That decouples ticket order from the order the user actually acted in.
/// Sequence: the user types (debounce starts, no ticket yet); before it
/// elapses the user clicks a feed (that load takes a ticket and wins,
/// correctly); the debounce then elapses and the search takes a LATER
/// ticket, wins the guard on its own terms, and overwrites the feed's list
/// with stale search results. The guard cannot catch this because, by its
/// own ordering rule, the search genuinely is newer.
///
/// The fix is to stop the stale search before it can ever take a ticket:
/// <see cref="CancelForFeedChange"/> cancels the pending debounce
/// synchronously when the feed-tree selection changes, and clears
/// <see cref="IsShowingSearchResults"/> so nothing about the UI keeps
/// claiming to show search results the user has already moved past.
///
/// Plain, no-Avalonia class on purpose, same reasoning as
/// <see cref="DwellCoordinator"/>: this is the decision logic behind
/// MainWindow's search/feed-selection interaction, extracted so the
/// interleaving above is unit testable without constructing a Window.
/// </summary>
public sealed class SearchCoordinator : IDisposable
{
    private CancellationTokenSource? _debounceCts;

    /// <summary>True while ItemRows holds search results rather than a feed-tree selection's items.</summary>
    public bool IsShowingSearchResults { get; private set; }

    /// <summary>True while a debounce (or the search it triggers) is pending.</summary>
    public bool IsPending => _debounceCts is not null;

    /// <summary>
    /// Cancels any pending debounce and starts a new one, returning its
    /// token. The caller awaits the debounce delay against this token, then
    /// runs the search against the same token, so a call made here later
    /// (a fresh keystroke, or <see cref="CancelForFeedChange"/>) always
    /// invalidates whatever the previous call was doing.
    /// </summary>
    public CancellationToken BeginDebounce()
    {
        CancelPending();
        _debounceCts = new CancellationTokenSource();
        return _debounceCts.Token;
    }

    /// <summary>Call once a search's results have actually been applied to ItemRows.</summary>
    public void MarkShowingResults() => IsShowingSearchResults = true;

    /// <summary>Call once the search box is cleared and ItemRows reflects the feed-tree selection again.</summary>
    public void MarkShowingSelection() => IsShowingSearchResults = false;

    /// <summary>
    /// Call from the feed-tree selection setter, before the corresponding
    /// LoadItemsAsync call. Cancels any pending debounce (which also
    /// cancels a search already in flight, since both phases share the
    /// token from <see cref="BeginDebounce"/>) so it can never win the
    /// shared <see cref="LoadSequenceGuard"/> against the newly-selected
    /// feed's load, and clears <see cref="IsShowingSearchResults"/> since
    /// the list about to be shown is a feed selection, not search results.
    /// </summary>
    public void CancelForFeedChange()
    {
        CancelPending();
        IsShowingSearchResults = false;
    }

    /// <summary>Cancels the pending debounce, if any. Idempotent.</summary>
    public void CancelPending()
    {
        if (_debounceCts is null) return;
        _debounceCts.Cancel();
        _debounceCts.Dispose();
        _debounceCts = null;
    }

    public void Dispose() => CancelPending();
}
