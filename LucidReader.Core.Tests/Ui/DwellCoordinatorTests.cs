using LucidReader.Services;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class DwellCoordinatorTests
{
    [Fact]
    public void A_freshly_started_dwell_is_not_cancelled()
    {
        var dwell = new DwellCoordinator();

        var token = dwell.StartNew();

        Assert.False(token.IsCancellationRequested);
        Assert.True(dwell.IsPending);
    }

    [Fact]
    public void Starting_a_new_dwell_cancels_whatever_was_previously_pending()
    {
        // This is the reselection-scan case: holding J to move through the
        // list must not let an earlier item's dwell survive to fire.
        var dwell = new DwellCoordinator();
        var first = dwell.StartNew();

        var second = dwell.StartNew();

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public void CancelPending_cancels_the_current_token_and_clears_pending_state()
    {
        var dwell = new DwellCoordinator();
        var token = dwell.StartNew();

        dwell.CancelPending();

        Assert.True(token.IsCancellationRequested);
        Assert.False(dwell.IsPending);
    }

    [Fact]
    public void CancelPending_with_nothing_pending_is_a_no_op()
    {
        var dwell = new DwellCoordinator();

        dwell.CancelPending();

        Assert.False(dwell.IsPending);
    }

    [Fact]
    public void Dispose_cancels_any_pending_dwell()
    {
        // This is the window-close case: a pending dwell must not fire a
        // write after the window (and the services it depends on) is gone.
        var dwell = new DwellCoordinator();
        var token = dwell.StartNew();

        dwell.Dispose();

        Assert.True(token.IsCancellationRequested);
    }
}
