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

/// <summary>
/// Auto-pause used to be decided from the Feed snapshot the refresh loaded up
/// to a minute earlier, while the counter itself was incremented in SQL. Two
/// refreshes of one feed could therefore both compute 4 while the database
/// went to 5, stepping over the threshold rather than hitting it, and a
/// stale is_enabled could re-pause a feed the user had just resumed.
/// </summary>
public class FeedRefreshFailureAccountingTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));

    private ReaderDatabase _db = null!;
    private FeedRepository _feeds = null!;
    private ItemRepository _items = null!;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _feeds = new FeedRepository(_db);
        _items = new ItemRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private FeedRefreshService CreateService(StubHttpHandler handler) =>
        new(_feeds, _items,
            new FeedFetcher(handler.CreateClient()),
            new FeedParser(),
            new BackoffPolicy(new Random(999)),
            () => ReaderSettings.Defaults,
            _time);

    private Task<long> AddFeedAsync() =>
        _feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(5);
        }
    }

    private async Task FailUntilAsync(long feedId, int count)
    {
        var now = _time.GetUtcNow();
        for (var i = 0; i < count; i++)
            await _feeds.RecordFailureAsync(feedId, "earlier failure", now, now, CancellationToken.None);
    }

    /// <summary>
    /// The snapshot this refresh loaded says zero failures. The database says
    /// one short of the threshold by the time the fetch comes back, and the
    /// database is what has to decide.
    /// </summary>
    [Fact]
    public async Task Auto_pause_fires_on_the_count_the_database_holds_not_the_snapshot()
    {
        var (handler, gate) = StubHttpHandler.Gated();
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        var refreshTask = service.RefreshNowAsync(feedId);
        await WaitUntilAsync(() => handler.Requests.Count > 0);

        // Everything that failed while this fetch was in flight.
        await FailUntilAsync(feedId, BackoffPolicy.AutoPauseThreshold - 1);

        gate.SetResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var outcome = await refreshTask;

        Assert.False(outcome.Success);
        var feed = await _feeds.GetAsync(feedId);
        Assert.Equal(BackoffPolicy.AutoPauseThreshold, feed!.ConsecutiveFailures);
        Assert.False(feed.IsEnabled);
    }

    /// <summary>
    /// The other direction: the snapshot is one short of the threshold, the
    /// user resumes the feed mid-fetch (which resets the counter), and the
    /// failure that lands afterwards must not pause it again.
    /// </summary>
    [Fact]
    public async Task A_feed_the_user_resumed_mid_fetch_is_not_paused_by_the_stale_snapshot()
    {
        var (handler, gate) = StubHttpHandler.Gated();
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();
        await FailUntilAsync(feedId, BackoffPolicy.AutoPauseThreshold - 1);

        var refreshTask = service.RefreshNowAsync(feedId);
        await WaitUntilAsync(() => handler.Requests.Count > 0);

        // The user clears the failure history by resuming the feed.
        await _feeds.SetEnabledAsync(feedId, true);

        gate.SetResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await refreshTask;

        var feed = await _feeds.GetAsync(feedId);
        Assert.Equal(1, feed!.ConsecutiveFailures);
        Assert.True(feed.IsEnabled);
    }

    /// <summary>
    /// RefreshNowAsync used to bypass _inFlight entirely, which is what made
    /// two concurrent refreshes of one feed an everyday occurrence rather than
    /// a timing coincidence: a scheduler tick and a click on Refresh land on
    /// the same feed routinely.
    /// </summary>
    [Fact]
    public async Task A_manual_refresh_does_not_start_a_second_fetch_of_a_feed_already_refreshing()
    {
        var (handler, gate) = StubHttpHandler.Gated();
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        var first = service.RefreshNowAsync(feedId);
        await WaitUntilAsync(() => handler.Requests.Count > 0);

        var second = await service.RefreshNowAsync(feedId);

        Assert.Single(handler.Requests);
        Assert.True(second.NotModified);
        Assert.Equal(0, second.NewItemCount);
        Assert.Null(second.Error);

        gate.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixtures.Feed("rss2-simple.xml"))
        });
        Assert.True((await first).Success);
    }

    [Fact]
    public async Task A_manual_refresh_blocks_the_queued_path_while_it_runs_and_releases_it_after()
    {
        var (handler, gate) = StubHttpHandler.Gated();
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        var refreshTask = service.RefreshNowAsync(feedId);
        await WaitUntilAsync(() => handler.Requests.Count > 0);

        Assert.False(service.TryQueue(feedId));

        gate.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixtures.Feed("rss2-simple.xml"))
        });
        await refreshTask;

        Assert.True(service.TryQueue(feedId));
    }

    [Fact]
    public async Task Two_manual_refreshes_one_after_the_other_both_fetch()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"), mediaType: "application/rss+xml");
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        Assert.True((await service.RefreshNowAsync(feedId)).Success);
        Assert.True((await service.RefreshNowAsync(feedId)).Success);

        Assert.Equal(2, handler.Requests.Count);
    }
}
