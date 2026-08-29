using LucidReader.Services;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// Covers <see cref="SearchCoordinator"/>, the decision logic behind
/// MainWindow's search box: debounce cancellation, and the fix for the
/// interleaving where a feed click during a typing pause lost to a stale
/// search. (Search-matching behaviour itself — titles, bodies, punctuation
/// safety — is already covered end to end by SearchRepositoryTests; these
/// tests exist to cover what Task 10 actually added on top of that, which
/// the original test file for this task did not touch at all.)
/// </summary>
public class SearchCoordinatorTests
{
    [Fact]
    public void A_pending_debounce_is_not_cancelled_on_its_own()
    {
        var coordinator = new SearchCoordinator();

        var token = coordinator.BeginDebounce();

        Assert.False(token.IsCancellationRequested);
        Assert.True(coordinator.IsPending);
    }

    [Fact]
    public void A_feed_selection_change_during_the_debounce_cancels_it()
    {
        // Reproduces the reported interleaving directly: the user types
        // (BeginDebounce, no LoadSequenceGuard ticket taken yet), then
        // clicks a feed before the 250ms debounce elapses. Without
        // CancelForFeedChange actually cancelling the token here, the
        // debounce would go on to elapse, call RunSearchAsync, and take a
        // LoadSequenceGuard ticket LATER than the feed's load purely
        // because of timing — winning the guard and overwriting the
        // feed's list with stale search results, even though the guard
        // itself behaved exactly as designed.
        var coordinator = new SearchCoordinator();
        var token = coordinator.BeginDebounce();

        coordinator.CancelForFeedChange();

        Assert.True(token.IsCancellationRequested);
        Assert.False(coordinator.IsPending);
    }

    [Fact]
    public void A_feed_selection_change_clears_showing_search_results_even_after_results_landed()
    {
        var coordinator = new SearchCoordinator();
        coordinator.BeginDebounce();
        coordinator.MarkShowingResults();
        Assert.True(coordinator.IsShowingSearchResults);

        coordinator.CancelForFeedChange();

        Assert.False(coordinator.IsShowingSearchResults);
    }

    [Fact]
    public void A_new_keystrokes_debounce_cancels_the_previous_one()
    {
        var coordinator = new SearchCoordinator();
        var first = coordinator.BeginDebounce();

        var second = coordinator.BeginDebounce();

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public void Clearing_the_search_box_restores_the_selection_state()
    {
        var coordinator = new SearchCoordinator();
        coordinator.BeginDebounce();
        coordinator.MarkShowingResults();

        coordinator.MarkShowingSelection();

        Assert.False(coordinator.IsShowingSearchResults);
    }

    [Fact]
    public void CancelPending_is_idempotent_when_nothing_is_pending()
    {
        var coordinator = new SearchCoordinator();

        coordinator.CancelPending();

        Assert.False(coordinator.IsPending);
    }
}
