using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// Wires the toolbar search box to full-text search across every stored
/// article. Debounced so a query does not run per keystroke, and the
/// resulting load participates in the SAME <see cref="_loadGuard"/> that
/// <see cref="LoadItemsAsync"/> uses (see MainWindow.Items.cs), not a
/// parallel one: a keystroke racing a feed-tree selection change, or two
/// keystrokes racing each other, would otherwise both mutate ItemRows and
/// the slower one could win.
/// </summary>
public partial class MainWindow
{
    private CancellationTokenSource? _searchDebounceCts;

    /// <summary>
    /// True while ItemRows holds search results rather than a feed-tree
    /// selection's items.
    /// </summary>
    public bool IsShowingSearchResults { get; private set; }

    /// <summary>
    /// Fired from the SearchText setter on every keystroke. Debounces by
    /// 250ms so a query is not run per keystroke, then either restores the
    /// feed-tree selection's list (blank query) or runs the search.
    /// </summary>
    public async Task OnSearchTextChangedAsync()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();

        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        var query = SearchText;

        if (string.IsNullOrWhiteSpace(query))
        {
            IsShowingSearchResults = false;
            await LoadItemsAsync();
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cts.Token);
            await RunSearchAsync(query, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke (or a feed-tree selection) superseded this
            // one; that caller is responsible for whatever ItemRows ends up
            // showing, so this call simply stops.
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
        var results = await _services.Search.SearchAsync(query, 500, ct);
        var feeds = (await _services.Feeds.GetAllAsync(ct))
            .ToDictionary(f => f.Id, f => f.DisplayTitle);
        var now = DateTimeOffset.UtcNow;

        // A newer load (another keystroke's search, or a feed-tree
        // selection) started while these awaits were in flight. Its result
        // is the one that matters now; this one must not touch ItemRows.
        if (!_loadGuard.IsCurrent(ticket)) return;

        _dwell.CancelPending();

        ItemRows.Clear();
        foreach (var item in results)
        {
            ItemRows.Add(new ItemRow
            {
                Item = item,
                FeedName = feeds.GetValueOrDefault(item.FeedId, "Unknown feed"),
                IsRead = item.IsRead,
                IsStarred = item.IsStarred,
                RelativeDate = ItemRow.FormatRelative(item.PublishedUtc ?? item.FirstSeenUtc, now),
                Snippet = Snippet.FromMarkdown(item.ContentMarkdown, item.Summary)
            });
        }

        IsShowingSearchResults = true;
        StatusMessage = results.Count == 0
            ? $"Nothing found for \"{query}\"."
            : $"{results.Count} results for \"{query}\"";

        _ = ResolveThumbnailsAsync(ItemRows.ToList());
    }
}
