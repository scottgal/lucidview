using LucidReader.Models;
using LucidReader.Services;

namespace LucidReader.Views;

/// <summary>
/// Wires the toolbar search box to full-text search across every stored
/// article. Debounced so a query does not run per keystroke, and the
/// resulting load participates in the SAME <see cref="_loadGuard"/> that
/// <see cref="LoadItemsAsync"/> uses (see MainWindow.Items.cs), not a
/// parallel one: a keystroke racing a feed-tree selection change, or two
/// keystrokes racing each other, would otherwise both mutate ItemRows and
/// the slower one could win.
///
/// The load guard alone is not enough to make a feed click win over a
/// search debounce that started earlier but elapses later: the guard
/// orders strictly by when its ticket is taken, not by when the user
/// acted, and a debounce takes its ticket up to 250ms after the keystroke.
/// <see cref="_searchCoordinator"/> (SearchCoordinator) closes that gap by
/// letting a feed-tree selection change (see MainWindow.axaml.cs's
/// SelectedFeedNode setter) cancel a pending debounce outright, so a stale
/// search can never reach the guard at all. See SearchCoordinator's own
/// doc comment for the full interleaving this fixes.
/// </summary>
public partial class MainWindow
{
    private readonly SearchCoordinator _searchCoordinator = new();
    private bool _searchScopeToSelection;

    /// <summary>True while ItemRows holds search results rather than a feed-tree selection's items.</summary>
    public bool IsShowingSearchResults => _searchCoordinator.IsShowingSearchResults;

    /// <summary>
    /// The toolbar's scope toggle. Off by default: search spans every feed
    /// unless the user asks for it to be narrowed. See SearchQueryBuilder for
    /// why that is the default rather than scoping to whatever is selected.
    /// Flipping it re-runs the current query rather than waiting for the next
    /// keystroke, since the toggle is itself a statement about the query on
    /// screen.
    /// </summary>
    public bool SearchScopeToSelection
    {
        get => _searchScopeToSelection;
        set
        {
            if (_searchScopeToSelection == value) return;
            _searchScopeToSelection = value;
            Raise();
            RerunSearchIfShowing();
        }
    }

    /// <summary>
    /// Whether the current sidebar selection is something a search can be
    /// narrowed to. The scope toggle binds its IsEnabled to this so it cannot
    /// claim a scope SearchQueryBuilder would ignore.
    /// </summary>
    public bool CanScopeSearchToSelection => SearchQueryBuilder.CanScope(SelectedFeedNode);

    /// <summary>
    /// Label on the scope toggle. A folder is not a feed, and a toggle that
    /// says "This feed" while a folder is selected would be describing the
    /// wrong scope.
    /// </summary>
    public string SearchScopeLabel =>
        SelectedFeedNode?.Kind == FeedTreeNodeKind.Folder ? "This folder" : "This feed";

    /// <summary>
    /// Re-runs the query currently on screen, if there is one. Called when
    /// something other than the search text changes what the query means: the
    /// filter segment, or the scope toggle. Does nothing when the list is
    /// showing a feed-tree selection, which has its own reload path.
    /// </summary>
    internal void RerunSearchIfShowing()
    {
        if (!IsShowingSearchResults || string.IsNullOrWhiteSpace(SearchText)) return;
        _ = RunSearchAsync(SearchText);
    }

    /// <summary>
    /// Fired from the SearchText setter on every keystroke. Debounces by
    /// 250ms so a query is not run per keystroke, then either restores the
    /// feed-tree selection's list (blank query) or runs the search.
    /// </summary>
    public async Task OnSearchTextChangedAsync()
    {
        var query = SearchText;

        if (string.IsNullOrWhiteSpace(query))
        {
            _searchCoordinator.CancelPending();
            _searchCoordinator.MarkShowingSelection();
            Raise(nameof(IsShowingSearchResults));
            await LoadItemsAsync();
            return;
        }

        var token = _searchCoordinator.BeginDebounce();

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), token);
            await RunSearchAsync(query, token);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke, or a feed-tree selection change (via
            // SearchCoordinator.CancelForFeedChange), superseded this one;
            // whichever call did that owns whatever ItemRows shows now.
        }
    }

    /// <summary>
    /// Runs a search immediately, with no debounce. Public so a test (or a
    /// future "search all" affordance) can drive it directly.
    /// </summary>
    public Task RunSearchAsync(string query) => RunSearchAsync(query, CancellationToken.None);

    private async Task RunSearchAsync(string query, CancellationToken ct)
    {
        // Same staleness guard LoadItemsAsync uses, taken from the same
        // instance: a search result and a feed-selection load are two
        // producers for the one ItemRows collection, so they must share one
        // sequence counter or a slow search could still land after a faster
        // feed-tree load (or vice versa) and repopulate the list wrongly.
        _dwell.CancelPending();
        _thumbnailCoordinator.CancelPending();

        var ticket = _loadGuard.Begin();
        var searchQuery = SearchQueryBuilder.Build(
            query, SelectedFeedNode, CurrentFilter, SearchScopeToSelection, 500);
        var results = await _services.Search.SearchAsync(searchQuery, ct);
        var feeds = (await _services.Feeds.GetAllAsync(ct))
            .ToDictionary(f => f.Id, f => f.DisplayTitle);
        var now = DateTimeOffset.UtcNow;

        // Belt-and-braces on top of the guard check below: if a feed change
        // cancelled this call's token while the awaits above were in
        // flight, but SearchAsync/GetAllAsync happened not to observe the
        // cancellation themselves, this still stops the write.
        ct.ThrowIfCancellationRequested();

        // A newer load (another keystroke's search, or a feed-tree
        // selection) started while these awaits were in flight. Its result
        // is the one that matters now; this one must not touch ItemRows.
        if (!_loadGuard.IsCurrent(ticket)) return;

        _dwell.CancelPending();

        ItemRows.Clear();
        foreach (var hit in results)
        {
            var item = hit.Item;
            ItemRows.Add(new ItemRow
            {
                Item = item,
                FeedName = feeds.GetValueOrDefault(item.FeedId, "Unknown feed"),
                IsRead = item.IsRead,
                IsStarred = item.IsStarred,
                RelativeDate = ItemRow.FormatRelative(item.PublishedUtc ?? item.FirstSeenUtc, now),
                // Both are set. Snippet is the ordinary preview and stays
                // correct for the row; MatchedSnippet is the passage that
                // explains the hit, and IsSearchResult is what makes the row
                // show the second instead of the first.
                Snippet = Snippet.FromMarkdown(item.ContentMarkdown, item.Summary),
                MatchedSnippet = hit.Snippet,
                IsSearchResult = true
            });
        }

        _searchCoordinator.MarkShowingResults();
        Raise(nameof(IsShowingSearchResults));
        StatusMessage = results.Count == 0
            ? $"Nothing found for \"{query}\"."
            : $"{results.Count} results for \"{query}\"";

        _ = ResolveThumbnailsAsync(ItemRows.ToList());
    }
}
