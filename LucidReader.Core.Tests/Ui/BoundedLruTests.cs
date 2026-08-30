using LucidReader.Views.Controls;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// The sidebar/list/reading-pane bitmap cache's bookkeeping, over plain
/// objects rather than Avalonia Bitmaps, which cannot be constructed without
/// a rendering platform.
///
/// Two properties, and the second is the point. The cap holding is what stops
/// the cache growing over a long session. But a cap on a dictionary proves
/// nothing on its own: PathToBitmapConverter deliberately does not dispose an
/// evicted bitmap, so if eviction left a reference anywhere the native memory
/// behind it would never come back and the cap would be decoration. The weak
/// reference test below is the one that says eviction actually releases.
/// </summary>
public class BoundedLruTests
{
    private static readonly DateTime Stamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Holds_its_capacity_no_matter_how_many_entries_are_added()
    {
        var cache = new BoundedLru<object>(48);

        for (var i = 0; i < 5_000; i++)
            cache.Add($"/cache/{i}.png", new object(), Stamp);

        Assert.Equal(48, cache.Count);
    }

    [Fact]
    public void Evicts_the_least_recently_used_entry_first()
    {
        var cache = new BoundedLru<object>(3);
        var first = new object();

        cache.Add("a", first, Stamp);
        cache.Add("b", new object(), Stamp);
        cache.Add("c", new object(), Stamp);

        // Touching "a" makes "b" the least recently used, so the fourth
        // insert must take "b" and leave "a" alone.
        Assert.True(cache.TryGet("a", Stamp, out _));
        cache.Add("d", new object(), Stamp);

        Assert.True(cache.TryGet("a", Stamp, out var stillThere));
        Assert.Same(first, stillThere);
        Assert.False(cache.TryGet("b", Stamp, out _));
    }

    [Fact]
    public void A_stamp_that_no_longer_matches_is_a_miss_and_drops_the_entry()
    {
        var cache = new BoundedLru<object>(4);
        cache.Add("a", new object(), Stamp);

        Assert.False(cache.TryGet("a", Stamp.AddSeconds(1), out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Replacing_a_key_does_not_leave_the_old_entry_occupying_a_slot()
    {
        var cache = new BoundedLru<object>(4);

        cache.Add("a", new object(), Stamp);
        cache.Add("a", new object(), Stamp.AddSeconds(1));

        Assert.Equal(1, cache.Count);
    }

    /// <summary>
    /// Eviction has to be the release of the only strong reference, or the
    /// cap bounds nothing that costs memory. Written with weak references and
    /// a forced collection because that is the only way to ask the question
    /// directly; the local function keeps the values out of a stack slot the
    /// collector would conservatively keep alive.
    /// </summary>
    [Fact]
    public void An_evicted_value_becomes_collectable()
    {
        var cache = new BoundedLru<object>(4);

        var evicted = FillPastCapacity(cache);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.All(evicted, weak => Assert.False(weak.TryGetTarget(out _)));

        // And the ones still inside the cap are still held, so the test is
        // not merely observing an over-eager collector.
        Assert.Equal(4, cache.Count);
    }

    private static List<WeakReference<object>> FillPastCapacity(BoundedLru<object> cache)
    {
        var evicted = new List<WeakReference<object>>();

        for (var i = 0; i < 20; i++)
        {
            var value = new object();
            if (i < 16) evicted.Add(new WeakReference<object>(value));
            cache.Add($"/cache/{i}.png", value, Stamp);
        }

        return evicted;
    }
}
