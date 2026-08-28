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

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20);
    }
}
