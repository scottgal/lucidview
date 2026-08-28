using System.Net;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Core.Sync;
using LucidReader.Core.Tests.Feeds;
using LucidReader.Core.Tests.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Sync;

public class RefreshSchedulerTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time =
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
        // Paused so TickAsync's queueing can be observed without the work
        // racing to completion and clearing the in-flight set.
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

    [Fact]
    public async Task A_tick_queues_every_due_feed()
    {
        await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://a.example/feed.xml",
            NextDueUtc = _time.GetUtcNow().AddMinutes(-1)
        });
        await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://b.example/feed.xml",
            NextDueUtc = _time.GetUtcNow().AddMinutes(-5)
        });
        await using var scheduler = CreateScheduler();

        var queued = await scheduler.TickAsync();

        Assert.Equal(2, queued);
    }

    [Fact]
    public async Task A_tick_leaves_feeds_that_are_not_due_alone()
    {
        await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://a.example/feed.xml",
            NextDueUtc = _time.GetUtcNow().AddMinutes(30)
        });
        await using var scheduler = CreateScheduler();

        Assert.Equal(0, await scheduler.TickAsync());
    }

    [Fact]
    public async Task A_never_fetched_feed_is_due_immediately()
    {
        await _feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        await using var scheduler = CreateScheduler();

        Assert.Equal(1, await scheduler.TickAsync());
    }

    [Fact]
    public async Task A_disabled_feed_is_never_queued()
    {
        await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://a.example/feed.xml",
            IsEnabled = false,
            NextDueUtc = _time.GetUtcNow().AddMinutes(-10)
        });
        await using var scheduler = CreateScheduler();

        Assert.Equal(0, await scheduler.TickAsync());
    }

    [Fact]
    public async Task A_second_tick_does_not_re_queue_a_feed_that_is_still_in_flight()
    {
        await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://a.example/feed.xml",
            NextDueUtc = _time.GetUtcNow().AddMinutes(-1)
        });
        await using var scheduler = CreateScheduler();

        var first = await scheduler.TickAsync();
        var second = await scheduler.TickAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task Advancing_the_clock_past_the_interval_fires_a_tick()
    {
        await _feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        await using var scheduler = CreateScheduler();
        scheduler.Start();

        _time.Advance(TimeSpan.FromMinutes(1));
        await WaitForAsync(() => _refresh.PendingCount > 0);

        Assert.True(_refresh.PendingCount > 0);
    }

    [Fact]
    public async Task Stopping_prevents_any_further_ticks()
    {
        await _feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        await using var scheduler = CreateScheduler();
        scheduler.Start();
        await scheduler.StopAsync();

        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.False(scheduler.IsRunning);
        Assert.Equal(0, _refresh.PendingCount);
    }

    // --- Review findings from the first pass -------------------------------

    [Fact]
    public async Task DisposeAsync_is_idempotent()
    {
        var scheduler = CreateScheduler();

        await scheduler.DisposeAsync();
        // A second dispose (defensive cleanup in a finally, a double
        // `await using`) must be a no-op, not an ObjectDisposedException
        // from re-entering StopAsync against an already-disposed CTS.
        var exception = await Record.ExceptionAsync(() => scheduler.DisposeAsync().AsTask());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Start_after_StopAsync_still_fires_ticks()
    {
        await _feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        await using var scheduler = CreateScheduler();
        scheduler.Start();
        await scheduler.StopAsync();

        // Restarting must not be permanently poisoned by the cancelled
        // token left behind from the first Stop.
        scheduler.Start();
        _time.Advance(TimeSpan.FromMinutes(1));
        await WaitForAsync(() => _refresh.PendingCount > 0);

        Assert.True(_refresh.PendingCount > 0);
    }

    [Fact]
    public async Task Start_after_DisposeAsync_throws()
    {
        var scheduler = CreateScheduler();
        await scheduler.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => scheduler.Start());
    }

    [Fact]
    public async Task StopAsync_waits_for_an_in_flight_tick_before_disposing()
    {
        var gate = new TaskCompletionSource();
        var repo = new BlockingFeedRepository(_db, gate.Task);
        await _feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        await using var scheduler = new RefreshScheduler(repo, _refresh, _time, TimeSpan.FromMinutes(1));
        scheduler.Start();

        _time.Advance(TimeSpan.FromMinutes(1));
        await WaitForAsync(() => repo.CallCount > 0);

        var stopTask = scheduler.StopAsync();
        // The tick is still blocked inside GetDueAsync, so StopAsync must
        // still be waiting on it rather than having already torn down the
        // token and moved on.
        Assert.False(stopTask.IsCompleted);

        gate.SetResult();
        await stopTask;

        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public async Task A_tick_that_is_still_running_causes_an_overlapping_firing_to_be_skipped()
    {
        var repo = new BlockingOnceFeedRepository(_db);
        await _feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        await using var scheduler = new RefreshScheduler(repo, _refresh, _time, TimeSpan.FromMinutes(1));
        scheduler.Start();

        // First tick fires and blocks inside GetDueAsync.
        _time.Advance(TimeSpan.FromMinutes(1));
        await WaitForAsync(() => repo.CallCount > 0);

        // A second firing arrives while the first is still running. It must
        // be skipped, not queued up behind the first.
        _time.Advance(TimeSpan.FromMinutes(1));
        var overlapped = await WaitForAsync(() => repo.CallCount > 1, attempts: 25, delayMs: 20);

        Assert.False(overlapped);
        Assert.Equal(1, repo.CallCount);

        // Releasing the first tick lets the timer resume normal ticking.
        repo.Release();
        await WaitForAsync(() => _refresh.PendingCount > 0 || _refresh.ActiveCount > 0);
    }

    [Fact]
    public async Task A_tick_whose_query_throws_does_not_stop_the_timer_and_the_next_tick_still_queues_feeds()
    {
        var repo = new ThrowingOnceFeedRepository(_db);
        await _feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        await using var scheduler = new RefreshScheduler(repo, _refresh, _time, TimeSpan.FromMinutes(1));
        scheduler.Start();

        // First tick: GetDueAsync throws. The single property this class
        // guarantees is that this does not kill the timer.
        _time.Advance(TimeSpan.FromMinutes(1));
        await WaitForAsync(() => repo.CallCount >= 1);
        // Give the failing tick's own exception handling a moment to finish
        // unwinding before asserting on state it touches.
        await WaitForAsync(() => repo.CallCount >= 1 && _refresh.PendingCount == 0, attempts: 25, delayMs: 20);

        Assert.True(scheduler.IsRunning);
        Assert.Equal(0, _refresh.PendingCount);

        // Second tick: the timer is still alive, so it fires again and this
        // time the query succeeds.
        _time.Advance(TimeSpan.FromMinutes(1));
        await WaitForAsync(() => _refresh.PendingCount > 0);

        Assert.True(_refresh.PendingCount > 0);
        Assert.True(repo.CallCount >= 2);
    }

    [Fact]
    public async Task A_feed_past_the_two_hundred_feed_cap_is_reached_on_a_later_tick()
    {
        // Let queued refreshes actually complete (against the in-memory stub
        // handler, no real network) so their next_due_utc moves into the
        // future and the query can surface the remainder on a later tick.
        _refresh.Resume();

        const int total = 205;
        for (var i = 0; i < total; i++)
        {
            // Ascending offsets: index 0 is the most overdue (sorts first,
            // in the first batch), index (total-1) is the least overdue
            // (sorts last, at the back of the due list).
            await _feeds.AddAsync(new Feed
            {
                FeedUrl = $"https://batch.example/{i}.xml",
                NextDueUtc = _time.GetUtcNow().AddMinutes(-(total - i))
            });
        }
        await using var scheduler = CreateScheduler();

        var first = await scheduler.TickAsync();
        Assert.Equal(200, first);

        await WaitForAsync(
            () => _refresh.PendingCount == 0 && _refresh.ActiveCount == 0,
            attempts: 1000, delayMs: 20);

        var second = await scheduler.TickAsync();
        Assert.Equal(total - 200, second);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int attempts, int delayMs)
    {
        for (var i = 0; i < attempts && !condition(); i++)
            await Task.Delay(delayMs);
        return condition();
    }

    /// <summary>
    /// A FeedRepository whose GetDueAsync blocks on an externally-controlled
    /// gate. Used to prove that shutdown genuinely waits for an in-flight
    /// tick instead of racing ahead of it.
    /// </summary>
    private sealed class BlockingFeedRepository(ReaderDatabase db, Task gate) : FeedRepository(db)
    {
        private int _callCount;

        public int CallCount => _callCount;

        public override async Task<IReadOnlyList<Feed>> GetDueAsync(
            DateTimeOffset nowUtc, int limit, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            await gate;
            return await base.GetDueAsync(nowUtc, limit, ct);
        }
    }

    /// <summary>
    /// A FeedRepository whose first GetDueAsync call blocks until Release()
    /// is called; every later call runs normally. Used to prove that a
    /// timer firing while a tick is already running is skipped rather than
    /// queued behind it.
    /// </summary>
    private sealed class BlockingOnceFeedRepository(ReaderDatabase db) : FeedRepository(db)
    {
        private TaskCompletionSource? _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public int CallCount => _callCount;

        public void Release() => _gate?.TrySetResult();

        public override async Task<IReadOnlyList<Feed>> GetDueAsync(
            DateTimeOffset nowUtc, int limit, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            var gate = Interlocked.Exchange(ref _gate, null);
            if (gate is not null)
                await gate.Task;
            return await base.GetDueAsync(nowUtc, limit, ct);
        }
    }

    /// <summary>
    /// A FeedRepository whose first GetDueAsync call throws; every later
    /// call runs normally. Used to prove that a tick whose database query
    /// throws does not kill the timer - the property this whole class
    /// exists to guarantee, and which was previously asserted only in a
    /// comment.
    /// </summary>
    private sealed class ThrowingOnceFeedRepository(ReaderDatabase db) : FeedRepository(db)
    {
        private int _callCount;

        public int CallCount => _callCount;

        public override Task<IReadOnlyList<Feed>> GetDueAsync(
            DateTimeOffset nowUtc, int limit, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
                throw new InvalidOperationException("Simulated database failure.");
            return base.GetDueAsync(nowUtc, limit, ct);
        }
    }
}
