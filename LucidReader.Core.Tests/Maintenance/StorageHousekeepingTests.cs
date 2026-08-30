using LucidReader.Core.Maintenance;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Core.Tests.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Maintenance;

/// <summary>
/// The two things the retention pass now does besides deleting rows, both of
/// which exist only because this app is left running for weeks.
///
/// The write-ahead log grows from ordinary writes and a passive checkpoint
/// never shortens it, so before this the -wal sat at its high-water mark for
/// a whole session and only a restart brought it back. The full-text index
/// grows from deletions, because FTS5 records a delete as a marker in a new
/// segment rather than removing terms from an old one, so an index over a
/// table that is pruned daily gets larger while holding less.
/// </summary>
public class StorageHousekeepingTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time =
        new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));

    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private FeedRepository _feeds = null!;
    private RetentionService _retention = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _feeds = new FeedRepository(_db);
        _retention = new RetentionService(_db, _feeds, () => ReaderSettings.Defaults, _time);
        _feedId = await _feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private Task AddAsync(string guid, int ageDays, bool isRead) =>
        _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = guid,
            Title = guid,
            ContentMarkdown = string.Join(' ', Enumerable.Repeat(guid, 200)),
            PublishedUtc = _time.GetUtcNow().AddDays(-ageDays),
            FirstSeenUtc = _time.GetUtcNow().AddDays(-ageDays),
            IsRead = isRead
        });

    [Fact]
    public async Task Checkpointing_the_write_ahead_log_reports_the_frames_it_moved()
    {
        for (var i = 0; i < 50; i++) await AddAsync($"item-{i}", ageDays: 1, isRead: false);

        // A truncating checkpoint returns the number of frames the log held.
        // Asserting only that it is not negative: whether frames are still
        // there depends on whether SQLite auto-checkpointed on the way, and a
        // test that demanded a specific number would be asserting SQLite's
        // internal thresholds rather than this app's behaviour.
        var frames = await _db.CheckpointWalAsync();
        Assert.True(frames >= 0);

        // The important half: it runs at all. Run inside a transaction it
        // would fail, which is the trap CheckpointWalAsync exists to avoid,
        // and the failure would be swallowed by the retention pass's own
        // catch and never seen again.
        Assert.True(await _db.CheckpointWalAsync() >= 0);
    }

    [Fact]
    public async Task The_retention_pass_leaves_the_full_text_index_searchable_after_a_merge()
    {
        var search = new SearchRepository(_db);

        for (var i = 0; i < 40; i++) await AddAsync($"keepme-{i}", ageDays: 1, isRead: false);
        for (var i = 0; i < 40; i++) await AddAsync($"dropme-{i}", ageDays: 400, isRead: true);

        var deleted = await _retention.PruneAsync();
        Assert.True(deleted > 0);

        // The merge is bounded housekeeping, so what matters is that it does
        // not damage the index: the surviving rows are still findable and the
        // pruned ones are gone from it.
        var kept = await search.SearchAsync("keepme", 100);
        Assert.NotEmpty(kept);

        var dropped = await search.SearchAsync("dropme", 100);
        Assert.Empty(dropped);
    }

    [Fact]
    public async Task Repeated_retention_passes_stay_safe_to_run_over_a_long_uptime()
    {
        // Six hours apart in the running app, so over a fortnight this runs
        // about fifty times. Running it repeatedly here is the cheapest way
        // to catch a housekeeping statement that only works the first time.
        for (var i = 0; i < 5; i++)
        {
            await AddAsync($"round-{i}", ageDays: 400, isRead: true);
            await _retention.PruneAsync();
        }

        Assert.True(await _retention.GetDatabaseSizeBytesAsync() > 0);
    }

    [Fact]
    public async Task Marking_everything_read_clears_every_feed_not_just_the_selected_one()
    {
        var second = await _feeds.AddAsync(new Feed { FeedUrl = "https://other.example/feed.xml" });

        await AddAsync("a", 1, isRead: false);
        await AddAsync("b", 1, isRead: false);
        await _items.UpsertAsync(new FeedItem
        {
            FeedId = second,
            Guid = "c",
            Title = "c",
            FirstSeenUtc = _time.GetUtcNow()
        });

        var changed = await _items.MarkAllReadAsync();

        Assert.Equal(3, changed);
        Assert.Equal(0, await _items.GetUnreadCountAsync(_feedId));
        Assert.Equal(0, await _items.GetUnreadCountAsync(second));

        // Nothing is left unread, so a second call writes no rows at all.
        // That matters more than it looks: every write to items fires the FTS
        // update trigger, so a statement without its "is_read = 0" guard would
        // reindex the whole table on every call.
        Assert.Equal(0, await _items.MarkAllReadAsync());
    }
}
