using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class FeedRepositoryTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private FeedRepository _feeds = null!;
    private FolderRepository _folders = null!;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _feeds = new FeedRepository(_db);
        _folders = new FolderRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private static Feed NewFeed(string url = "https://example.com/feed.xml") =>
        new() { FeedUrl = url, Title = "Example", SiteUrl = "https://example.com" };

    [Fact]
    public async Task Adding_a_feed_round_trips_every_field()
    {
        var id = await _feeds.AddAsync(NewFeed() with
        {
            RefreshIntervalMinutes = 15,
            AutoDownload = false,
            FetchFullText = true,
            RetentionDays = 30
        });

        var loaded = await _feeds.GetAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal("https://example.com/feed.xml", loaded!.FeedUrl);
        Assert.Equal("Example", loaded.Title);
        Assert.Equal(15, loaded.RefreshIntervalMinutes);
        Assert.False(loaded.AutoDownload);
        Assert.True(loaded.FetchFullText);
        Assert.Equal(30, loaded.RetentionDays);
        Assert.True(loaded.IsEnabled);
    }

    [Fact]
    public async Task Unset_overrides_round_trip_as_null_not_as_a_default()
    {
        var id = await _feeds.AddAsync(NewFeed());

        var loaded = await _feeds.GetAsync(id);

        Assert.Null(loaded!.RefreshIntervalMinutes);
        Assert.Null(loaded.AutoDownload);
        Assert.Null(loaded.FetchFullText);
        Assert.Null(loaded.RetentionDays);
    }

    [Fact]
    public async Task Adding_the_same_url_twice_is_rejected()
    {
        await _feeds.AddAsync(NewFeed());

        await Assert.ThrowsAnyAsync<Exception>(() => _feeds.AddAsync(NewFeed()));
    }

    [Fact]
    public async Task GetByUrl_finds_an_existing_subscription()
    {
        await _feeds.AddAsync(NewFeed());

        var found = await _feeds.GetByUrlAsync("https://example.com/feed.xml");

        Assert.NotNull(found);
    }

    [Fact]
    public async Task GetDue_returns_only_feeds_whose_next_due_has_passed()
    {
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        var dueId = await _feeds.AddAsync(NewFeed("https://a.example/feed.xml") with
        {
            NextDueUtc = now.AddMinutes(-1)
        });
        await _feeds.AddAsync(NewFeed("https://b.example/feed.xml") with
        {
            NextDueUtc = now.AddMinutes(30)
        });

        var due = await _feeds.GetDueAsync(now, limit: 10);

        Assert.Single(due);
        Assert.Equal(dueId, due[0].Id);
    }

    [Fact]
    public async Task GetDue_treats_a_never_fetched_feed_as_due()
    {
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        await _feeds.AddAsync(NewFeed());

        var due = await _feeds.GetDueAsync(now, limit: 10);

        Assert.Single(due);
    }

    [Fact]
    public async Task GetDue_skips_disabled_feeds()
    {
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        await _feeds.AddAsync(NewFeed() with { IsEnabled = false });

        var due = await _feeds.GetDueAsync(now, limit: 10);

        Assert.Empty(due);
    }

    [Fact]
    public async Task Recording_a_success_clears_the_failure_count_and_error()
    {
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        var id = await _feeds.AddAsync(NewFeed());
        await _feeds.RecordFailureAsync(id, "connection refused", now, now.AddMinutes(5));

        await _feeds.RecordSuccessAsync(id, "\"abc123\"", "Wed, 27 Aug 2026 10:00:00 GMT", now, now.AddMinutes(30));

        var loaded = await _feeds.GetAsync(id);
        Assert.Equal(0, loaded!.ConsecutiveFailures);
        Assert.Null(loaded.LastError);
        Assert.Equal("\"abc123\"", loaded.ETag);
        Assert.Equal("Wed, 27 Aug 2026 10:00:00 GMT", loaded.LastModified);
        Assert.Equal(now, loaded.LastSuccessUtc);
    }

    [Fact]
    public async Task Recording_failures_increments_the_count_each_time()
    {
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        var id = await _feeds.AddAsync(NewFeed());

        await _feeds.RecordFailureAsync(id, "timeout", now, now.AddMinutes(5));
        await _feeds.RecordFailureAsync(id, "timeout", now, now.AddMinutes(10));
        await _feeds.RecordFailureAsync(id, "500 Server Error", now, now.AddMinutes(20));

        var loaded = await _feeds.GetAsync(id);
        Assert.Equal(3, loaded!.ConsecutiveFailures);
        Assert.Equal("500 Server Error", loaded.LastError);
    }

    [Fact]
    public async Task Deleting_a_folder_orphans_its_feeds_rather_than_deleting_them()
    {
        var folderId = await _folders.AddAsync("News");
        var feedId = await _feeds.AddAsync(NewFeed() with { FolderId = folderId });

        await _folders.DeleteAsync(folderId);

        var loaded = await _feeds.GetAsync(feedId);
        Assert.NotNull(loaded);
        Assert.Null(loaded!.FolderId);
    }
}
