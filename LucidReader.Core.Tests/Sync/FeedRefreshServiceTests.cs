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

public class FeedRefreshServiceTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time =
        new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));

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

    private FeedRefreshService CreateService(StubHttpHandler handler, TimeSpan? maxFetchDuration = null) =>
        new(_feeds, _items,
            new FeedFetcher(handler.CreateClient()),
            new FeedParser(),
            new BackoffPolicy(new Random(999)),
            () => ReaderSettings.Defaults,
            _time,
            maxFetchDuration: maxFetchDuration);

    private Task<long> AddFeedAsync(string url = "https://example.com/feed.xml") =>
        _feeds.AddAsync(new Feed { FeedUrl = url });

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task A_successful_refresh_stores_the_items()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        var outcome = await service.RefreshNowAsync(feedId);

        Assert.True(outcome.Success);
        Assert.Equal(2, outcome.NewItemCount);
        var stored = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 100, 0));
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task A_successful_refresh_adopts_the_feeds_own_title()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        await service.RefreshNowAsync(feedId);

        var feed = await _feeds.GetAsync(feedId);
        Assert.Equal("Example Blog", feed!.Title);
        Assert.Equal("https://example.com/", feed.SiteUrl);
    }

    [Fact]
    public async Task A_second_refresh_of_unchanged_content_adds_nothing()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        await service.RefreshNowAsync(feedId);
        var second = await service.RefreshNowAsync(feedId);

        Assert.Equal(0, second.NewItemCount);
        var stored = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 100, 0));
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task An_item_with_no_guid_is_stored_under_a_stable_link_hash()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-no-guid.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        await service.RefreshNowAsync(feedId);
        var afterFirst = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 100, 0));
        await service.RefreshNowAsync(feedId);
        var afterSecond = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 100, 0));

        Assert.Single(afterFirst);
        Assert.Single(afterSecond);
        Assert.Equal(afterFirst[0].Guid, afterSecond[0].Guid);
    }

    [Fact]
    public async Task A_304_is_a_success_that_stores_nothing()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotModified);
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        var outcome = await service.RefreshNowAsync(feedId);

        Assert.True(outcome.Success);
        Assert.True(outcome.NotModified);
        Assert.Equal(0, outcome.NewItemCount);
    }

    [Fact]
    public async Task A_successful_refresh_records_the_validators_and_next_due()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"), etag: "\"v1\"");
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        await service.RefreshNowAsync(feedId);

        var feed = await _feeds.GetAsync(feedId);
        Assert.Equal("\"v1\"", feed!.ETag);
        Assert.Equal(0, feed.ConsecutiveFailures);
        Assert.Equal(_time.GetUtcNow().AddMinutes(30), feed.NextDueUtc);
    }

    [Fact]
    public async Task A_failed_refresh_records_the_error_and_backs_off()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.ServiceUnavailable);
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        var outcome = await service.RefreshNowAsync(feedId);

        Assert.False(outcome.Success);
        var feed = await _feeds.GetAsync(feedId);
        Assert.Equal(1, feed!.ConsecutiveFailures);
        Assert.Contains("503", feed.LastError);
        Assert.True(feed.NextDueUtc > _time.GetUtcNow());
    }

    [Fact]
    public async Task An_unparseable_response_is_a_failure_that_keeps_existing_items()
    {
        var okHandler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using (var service = CreateService(okHandler))
        {
            var seeded = await AddFeedAsync();
            await service.RefreshNowAsync(seeded);
        }

        var badHandler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("not-a-feed.html"));
        await using var second = CreateService(badHandler);
        var feedId = (await _feeds.GetByUrlAsync("https://example.com/feed.xml"))!.Id;

        var outcome = await second.RefreshNowAsync(feedId);

        Assert.False(outcome.Success);
        var stored = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 100, 0));
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task Reaching_the_auto_pause_threshold_disables_the_feed()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotFound);
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        for (var i = 0; i < BackoffPolicy.AutoPauseThreshold; i++)
            await service.RefreshNowAsync(feedId);

        var feed = await _feeds.GetAsync(feedId);
        Assert.False(feed!.IsEnabled);
    }

    [Fact]
    public async Task Queueing_a_feed_that_is_already_queued_is_refused()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();
        service.Pause();

        var first = service.TryQueue(feedId);
        var second = service.TryQueue(feedId);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task A_queued_feed_can_be_queued_again_once_it_has_finished()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        var completed = new TaskCompletionSource();
        service.Completed += _ => completed.TrySetResult();
        Assert.True(service.TryQueue(feedId));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(service.TryQueue(feedId));
    }

    [Fact]
    public async Task Completion_is_reported_for_every_queued_feed()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedIds = new List<long>();
        for (var i = 0; i < 5; i++)
            feedIds.Add(await AddFeedAsync($"https://example{i}.com/feed.xml"));

        var outcomes = new List<FeedRefreshOutcome>();
        var done = new TaskCompletionSource();
        service.Completed += outcome =>
        {
            lock (outcomes)
            {
                outcomes.Add(outcome);
                if (outcomes.Count == 5) done.TrySetResult();
            }
        };

        foreach (var id in feedIds) service.TryQueue(id);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(5, outcomes.Count);
    }

    [Fact]
    public async Task New_items_are_marked_pending_when_auto_download_is_on()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        await service.RefreshNowAsync(feedId);

        var pending = await _items.GetPendingOfflineAsync(100);
        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public async Task New_items_are_not_marked_pending_when_the_feed_opts_out()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            AutoDownload = false
        });

        await service.RefreshNowAsync(feedId);

        var pending = await _items.GetPendingOfflineAsync(100);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task A_stalled_fetch_records_a_failure_advances_next_due_and_still_raises_Completed()
    {
        var handler = StubHttpHandler.Blocking();
        await using var service = CreateService(handler, maxFetchDuration: TimeSpan.FromMilliseconds(50));
        var feedId = await AddFeedAsync();

        var completed = new TaskCompletionSource<FeedRefreshOutcome>();
        service.Completed += outcome => completed.TrySetResult(outcome);

        Assert.True(service.TryQueue(feedId));
        var outcome = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(outcome.Success);
        Assert.False(outcome.NotModified);

        var feed = await _feeds.GetAsync(feedId);
        Assert.Equal(1, feed!.ConsecutiveFailures);
        Assert.True(feed.NextDueUtc > _time.GetUtcNow());

        // The feed was released once the stall was recorded, not left
        // permanently in flight.
        Assert.True(service.TryQueue(feedId));
    }

    [Fact]
    public async Task Repeated_stalls_eventually_reach_auto_pause()
    {
        var handler = StubHttpHandler.Blocking();
        await using var service = CreateService(handler, maxFetchDuration: TimeSpan.FromMilliseconds(20));
        var feedId = await AddFeedAsync();

        for (var i = 0; i < BackoffPolicy.AutoPauseThreshold; i++)
        {
            var outcome = await service.RefreshNowAsync(feedId);
            Assert.False(outcome.Success);
        }

        var feed = await _feeds.GetAsync(feedId);
        Assert.False(feed!.IsEnabled);
        Assert.Equal(BackoffPolicy.AutoPauseThreshold, feed.ConsecutiveFailures);
    }

    [Fact]
    public async Task A_user_edit_made_while_a_refresh_is_in_flight_survives()
    {
        var (handler, gate) = StubHttpHandler.Gated();
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        var refreshTask = service.RefreshNowAsync(feedId);

        // Wait for the fetch to actually be under way (the handler has seen
        // the request) before landing the concurrent edit, so the edit lands
        // after RefreshCoreAsync has already loaded its now-stale snapshot.
        await WaitUntilAsync(() => handler.Requests.Count > 0);

        var beforeEdit = await _feeds.GetAsync(feedId);
        await _feeds.UpdateAsync(
            beforeEdit! with { AutoDownload = false, TitleOverride = "My Custom Title" },
            CancellationToken.None);

        gate.SetResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(Fixtures.Feed("rss2-simple.xml"))
        });

        var outcome = await refreshTask;
        Assert.True(outcome.Success);

        var feed = await _feeds.GetAsync(feedId);
        Assert.Equal(false, feed!.AutoDownload);
        Assert.Equal("My Custom Title", feed.TitleOverride);
        Assert.Equal("Example Blog", feed.Title);
        Assert.Equal("https://example.com/", feed.SiteUrl);
    }
}
