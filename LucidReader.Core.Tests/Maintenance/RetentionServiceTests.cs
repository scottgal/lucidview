using LucidReader.Core.Maintenance;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Core.Tests.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Maintenance;

public class RetentionServiceTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time =
        new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));

    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _feedId = await new FeedRepository(_db).AddAsync(
            new Feed { FeedUrl = "https://example.com/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private async Task<long> AddAsync(
        string guid, int ageDays, bool isRead, bool isStarred = false)
    {
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = guid,
            Title = guid,
            PublishedUtc = _time.GetUtcNow().AddDays(-ageDays),
            FirstSeenUtc = _time.GetUtcNow().AddDays(-ageDays)
        });
        if (isRead) await _items.SetReadAsync(id, true);
        if (isStarred) await _items.SetStarredAsync(id, true);
        return id;
    }

    private RetentionService Service(ReaderSettings settings) =>
        new(_db, () => settings, _time);

    private async Task<int> CountAsync() =>
        (await _items.QueryAsync(new ItemQuery(null, null, ItemFilter.All, 1000, 0))).Count;

    [Fact]
    public async Task Read_items_older_than_the_window_are_deleted()
    {
        await AddAsync("old-read", 40, isRead: true);
        await AddAsync("recent-read", 5, isRead: true);
        var service = Service(ReaderSettings.Defaults with { KeepReadArticlesDays = 30 });

        var deleted = await service.PruneAsync();

        Assert.Equal(1, deleted);
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task Unread_items_survive_when_keeping_unread_forever()
    {
        await AddAsync("old-unread", 400, isRead: false);
        var service = Service(ReaderSettings.Defaults with
        {
            KeepReadArticlesDays = 30,
            KeepUnreadForever = true
        });

        var deleted = await service.PruneAsync();

        Assert.Equal(0, deleted);
    }

    [Fact]
    public async Task Unread_items_are_pruned_when_a_window_is_configured()
    {
        await AddAsync("old-unread", 400, isRead: false);
        await AddAsync("recent-unread", 10, isRead: false);
        var service = Service(ReaderSettings.Defaults with
        {
            KeepUnreadForever = false,
            KeepUnreadDays = 180
        });

        var deleted = await service.PruneAsync();

        Assert.Equal(1, deleted);
    }

    [Fact]
    public async Task Starred_items_are_never_deleted_by_age()
    {
        await AddAsync("old-starred", 900, isRead: true, isStarred: true);
        var service = Service(ReaderSettings.Defaults with
        {
            KeepReadArticlesDays = 1,
            NeverDeleteStarred = true
        });

        var deleted = await service.PruneAsync();

        Assert.Equal(0, deleted);
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task The_per_feed_cap_keeps_the_newest_items()
    {
        for (var i = 0; i < 10; i++)
            await AddAsync($"item-{i:D2}", ageDays: i, isRead: false);
        var service = Service(ReaderSettings.Defaults with
        {
            KeepUnreadForever = true,
            MaxArticlesPerFeed = 5
        });

        await service.PruneAsync();

        var remaining = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));
        Assert.Equal(5, remaining.Count);
        Assert.Contains(remaining, item => item.Guid == "item-00");
        Assert.DoesNotContain(remaining, item => item.Guid == "item-09");
    }

    [Fact]
    public async Task The_per_feed_cap_still_spares_starred_items()
    {
        for (var i = 0; i < 10; i++)
            await AddAsync($"item-{i:D2}", ageDays: i, isRead: true, isStarred: i == 9);
        var service = Service(ReaderSettings.Defaults with
        {
            KeepReadArticlesDays = 365,
            MaxArticlesPerFeed = 5,
            NeverDeleteStarred = true
        });

        await service.PruneAsync();

        var remaining = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));
        Assert.Contains(remaining, item => item.Guid == "item-09");
    }

    [Fact]
    public async Task Pruning_removes_the_items_from_the_search_index_too()
    {
        var id = await AddAsync("old-read", 40, isRead: true);
        await _items.SetContentAsync(id, "distinctive haystack term", ContentSource.Feed);
        var service = Service(ReaderSettings.Defaults with { KeepReadArticlesDays = 30 });

        await service.PruneAsync();

        var results = await new SearchRepository(_db).SearchAsync("haystack", 10);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Pruning_an_empty_database_deletes_nothing_and_does_not_throw()
    {
        var service = Service(ReaderSettings.Defaults);

        Assert.Equal(0, await service.PruneAsync());
    }

    [Fact]
    public async Task Pruning_a_large_number_of_rows_shrinks_the_reported_database_size()
    {
        for (var i = 0; i < 2000; i++)
            await AddAsync($"bulk-{i:D5}", ageDays: 40, isRead: true);
        var service = Service(ReaderSettings.Defaults with { KeepReadArticlesDays = 30 });
        var sizeBefore = await service.GetDatabaseSizeBytesAsync();

        var deleted = await service.PruneAsync();

        var sizeAfter = await service.GetDatabaseSizeBytesAsync();
        Assert.Equal(2000, deleted);
        Assert.True(
            sizeAfter < sizeBefore,
            $"expected the database to shrink after pruning; before={sizeBefore} after={sizeAfter}");
    }
}
