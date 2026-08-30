using System.Net;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Core.Sync;
using LucidReader.Core.Tests.Feeds;
using LucidReader.Core.Tests.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Sync;

/// <summary>
/// The scheduler over a long uptime: a laptop that sleeps, and a clock that
/// is corrected backwards. Driven through ScheduledTickAsync against a real
/// database and a fake clock, so what is asserted is the behaviour of the
/// whole path rather than of RefreshCatchUp's arithmetic on its own (that is
/// covered separately in RefreshCatchUpTests).
/// </summary>
public class RefreshSchedulerLongUptimeTests : IAsyncLifetime
{
    /// <summary>
    /// A clock the test can move in either direction.
    ///
    /// FakeTimeProvider deliberately refuses to go backwards, which is
    /// exactly the condition these tests exist to cover: a machine whose
    /// clock is corrected back by an NTP step, a manual change, or a virtual
    /// machine restored from a snapshot. So the clock has to be a local one.
    /// Everything else about it defers to the base class, including
    /// CreateTimer, which nothing here uses (every tick is invoked directly).
    /// </summary>
    private sealed class RewindableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Shift(TimeSpan by) => _now = _now.Add(by);
    }

    private readonly TempDatabase _temp = new();
    private readonly RewindableTimeProvider _time =
        new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));

    private ReaderDatabase _db = null!;
    private FeedRepository _feeds = null!;
    private FeedRefreshService _refresh = null!;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _feeds = new FeedRepository(_db);

        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));

        _refresh = new FeedRefreshService(
            _feeds, new ItemRepository(_db),
            new FeedFetcher(handler.CreateClient()), new FeedParser(),
            new BackoffPolicy(new Random(7)), () => ReaderSettings.Defaults, _time);

        // Paused for the same reason RefreshSchedulerTests pauses: queueing
        // is what is being asserted, and a body that runs to completion
        // clears the in-flight slot the count is read from.
        _refresh.Pause();
    }

    public async Task DisposeAsync()
    {
        await _refresh.DisposeAsync();
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private RefreshScheduler CreateScheduler() =>
        new(_feeds, _refresh, _time, TimeSpan.FromMinutes(1));

    private async Task AddOverdueFeedsAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await _feeds.AddAsync(new Feed
            {
                FeedUrl = $"https://feed{i}.example/feed.xml",
                NextDueUtc = _time.GetUtcNow().AddHours(-1)
            });
        }
    }

    [Fact]
    public async Task The_tick_after_a_long_sleep_queues_a_small_batch_rather_than_everything()
    {
        await using var scheduler = CreateScheduler();

        // A baseline tick with nothing due, so the gap the next tick measures
        // is measured from a known instant and nothing is in flight yet.
        Assert.Equal(0, await scheduler.ScheduledTickAsync());

        await AddOverdueFeedsAsync(40);
        _time.Shift(TimeSpan.FromHours(9));

        var queued = await scheduler.ScheduledTickAsync();

        Assert.True(scheduler.IsCatchingUp);
        Assert.Equal(RefreshCatchUp.CatchUpBatchSize, queued);
        Assert.True(queued < 40);
    }

    [Fact]
    public async Task Catch_up_mode_persists_while_the_backlog_is_still_there()
    {
        await using var scheduler = CreateScheduler();
        Assert.Equal(0, await scheduler.ScheduledTickAsync());

        await AddOverdueFeedsAsync(40);
        _time.Shift(TimeSpan.FromHours(9));
        await scheduler.ScheduledTickAsync();

        // An ordinary minute later. The gap is normal now, so nothing
        // re-enters catch-up mode; it has to still be in it from the wake.
        _time.Shift(TimeSpan.FromMinutes(1));
        await scheduler.ScheduledTickAsync();

        Assert.True(scheduler.IsCatchingUp);
    }

    [Fact]
    public async Task A_wake_gap_enters_catch_up_mode_and_leaves_it_once_the_backlog_fits_in_one_batch()
    {
        await using var scheduler = CreateScheduler();
        Assert.Equal(0, await scheduler.ScheduledTickAsync());

        await AddOverdueFeedsAsync(4);
        _time.Shift(TimeSpan.FromHours(9));

        // Four overdue feeds is under the catch-up batch of ten, so this tick
        // enters catch-up mode, drains the backlog inside one batch, and
        // leaves again in the same tick.
        Assert.Equal(4, await scheduler.ScheduledTickAsync());
        Assert.False(scheduler.IsCatchingUp);
    }

    [Fact]
    public async Task A_backwards_clock_step_pulls_impossible_due_times_back_into_range()
    {
        // A feed scheduled normally, then the clock corrected back a month.
        // Its next_due_utc is now a month in the future and no ordinary tick
        // will ever find it due again.
        var id = await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://a.example/feed.xml",
            NextDueUtc = _time.GetUtcNow().AddMinutes(30)
        });

        await using var scheduler = CreateScheduler();
        await scheduler.ScheduledTickAsync();

        _time.Shift(TimeSpan.FromDays(-30));
        await scheduler.ScheduledTickAsync();

        Assert.Equal(1, scheduler.ClockRewindsHandled);
        Assert.Equal(1, scheduler.LastClockRewindFeedsRescheduled);

        var feed = await _feeds.GetAsync(id);
        Assert.NotNull(feed);
        Assert.True(feed!.NextDueUtc <= _time.GetUtcNow());
    }

    [Fact]
    public async Task A_backwards_step_leaves_due_times_that_are_still_plausible_alone()
    {
        // Corrected back by two hours, not a month: the feed's due time is
        // now two and a half hours out, which is well inside what a real
        // schedule can be, so nothing should be touched.
        var id = await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://a.example/feed.xml",
            NextDueUtc = _time.GetUtcNow().AddMinutes(30)
        });
        var original = (await _feeds.GetAsync(id))!.NextDueUtc;

        await using var scheduler = CreateScheduler();
        await scheduler.ScheduledTickAsync();

        _time.Shift(TimeSpan.FromHours(-2));
        await scheduler.ScheduledTickAsync();

        Assert.Equal(1, scheduler.ClockRewindsHandled);
        Assert.Equal(0, scheduler.LastClockRewindFeedsRescheduled);
        Assert.Equal(original, (await _feeds.GetAsync(id))!.NextDueUtc);
    }

    [Fact]
    public async Task Refresh_all_is_never_trimmed_to_a_catch_up_batch()
    {
        await using var scheduler = CreateScheduler();
        Assert.Equal(0, await scheduler.ScheduledTickAsync());

        await AddOverdueFeedsAsync(25);
        _time.Shift(TimeSpan.FromHours(9));

        // The gap here would put a timer tick into catch-up mode. TickAsync
        // is the manual "Refresh all" path: it queues everything due, and it
        // does not touch the timer's own clock bookkeeping either.
        Assert.Equal(25, await scheduler.TickAsync());
        Assert.False(scheduler.IsCatchingUp);
    }
}
