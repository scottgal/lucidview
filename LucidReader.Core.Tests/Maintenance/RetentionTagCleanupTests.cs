using LucidReader.Core.Maintenance;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Core.Tests.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Maintenance;

/// <summary>
/// Deleting an item cascades its item_tags rows away but leaves the tag row
/// itself, and nothing called TagRepository.DeleteUnusedAsync, so the tag list
/// only ever grew. The retention pass is where the items go, so it is where
/// the tags they were the last user of go too.
/// </summary>
public class RetentionTagCleanupTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));

    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private FeedRepository _feeds = null!;
    private TagRepository _tags = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _feeds = new FeedRepository(_db);
        _tags = new TagRepository(_db);
        _feedId = await _feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private async Task<long> AddReadItemAsync(string guid, int ageDays)
    {
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = guid,
            Title = guid,
            PublishedUtc = _time.GetUtcNow().AddDays(-ageDays),
            FirstSeenUtc = _time.GetUtcNow().AddDays(-ageDays)
        });
        await _items.SetReadAsync(id, true);
        return id;
    }

    [Fact]
    public async Task A_tag_whose_last_item_was_pruned_is_removed()
    {
        var old = await AddReadItemAsync("old", 40);
        var recent = await AddReadItemAsync("recent", 1);
        await _tags.AddToItemAsync(old, "abandoned");
        await _tags.AddToItemAsync(recent, "still-used");

        var service = new RetentionService(
            _db, _feeds, () => ReaderSettings.Defaults with { KeepReadArticlesDays = 30 }, _time);
        await service.PruneAsync();

        var remaining = await _tags.GetAllAsync();
        Assert.Equal(["still-used"], remaining);
    }

    [Fact]
    public async Task A_prune_that_deletes_nothing_leaves_every_tag_alone()
    {
        var recent = await AddReadItemAsync("recent", 1);
        await _tags.AddToItemAsync(recent, "kept");

        var service = new RetentionService(
            _db, _feeds, () => ReaderSettings.Defaults with { KeepReadArticlesDays = 30 }, _time);
        Assert.Equal(0, await service.PruneAsync());

        Assert.Equal(["kept"], await _tags.GetAllAsync());
    }
}
