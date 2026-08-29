using LucidReader.Services;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class LoadSequenceGuardTests
{
    [Fact]
    public void A_single_ticket_is_current_until_another_begins()
    {
        var guard = new LoadSequenceGuard();

        var ticket = guard.Begin();

        Assert.True(guard.IsCurrent(ticket));
    }

    [Fact]
    public void An_earlier_ticket_is_stale_once_a_later_one_begins()
    {
        // This is the race Finding 1 describes: a slow, earlier
        // LoadItemsAsync call must recognise it lost the race to a faster,
        // later one and discard its result rather than applying it.
        var guard = new LoadSequenceGuard();
        var earlier = guard.Begin();

        var later = guard.Begin();

        Assert.False(guard.IsCurrent(earlier));
        Assert.True(guard.IsCurrent(later));
    }

    [Fact]
    public void Three_overlapping_loads_only_the_last_one_started_is_current()
    {
        var guard = new LoadSequenceGuard();
        var first = guard.Begin();
        var second = guard.Begin();
        var third = guard.Begin();

        Assert.False(guard.IsCurrent(first));
        Assert.False(guard.IsCurrent(second));
        Assert.True(guard.IsCurrent(third));
    }
}
