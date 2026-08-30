using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// UpdateAsync is the save path for the settings dialog. What it writes, and
/// what it deliberately leaves alone, is the whole subject here.
/// </summary>
public class FeedRepositoryUpdateScopeTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private FeedRepository _feeds = null!;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _feeds = new FeedRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private Task<long> AddAsync(Feed? feed = null) =>
        _feeds.AddAsync(feed ?? new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            Title = "Publisher's own name",
            SiteUrl = "https://example.com"
        });

    /// <summary>
    /// The reason UpdateTitleAndSiteUrlAsync exists: a refresh can adopt a new
    /// publisher title at any moment, including while the dialog is open, and
    /// the dialog does not edit either column.
    /// </summary>
    [Fact]
    public async Task Saving_feed_settings_does_not_write_the_publisher_owned_columns()
    {
        var id = await AddAsync();
        var snapshot = (await _feeds.GetAsync(id))!;

        // The refresh that landed while the dialog was open.
        await _feeds.UpdateTitleAndSiteUrlAsync(id, "Renamed by the publisher", "https://new.example");

        await _feeds.UpdateAsync(snapshot with { TitleOverride = "My name for it" });

        var saved = (await _feeds.GetAsync(id))!;
        Assert.Equal("Renamed by the publisher", saved.Title);
        Assert.Equal("https://new.example", saved.SiteUrl);
        Assert.Equal("My name for it", saved.TitleOverride);
    }

    [Fact]
    public async Task Saving_feed_settings_still_writes_everything_the_dialog_edits()
    {
        var id = await AddAsync();
        var snapshot = (await _feeds.GetAsync(id))!;
        var folderId = await new FolderRepository(_db).AddAsync("Reading");

        await _feeds.UpdateAsync(snapshot with
        {
            FolderId = folderId,
            TitleOverride = "Renamed",
            IsEnabled = false,
            RefreshIntervalMinutes = 15,
            AutoDownload = false,
            FetchFullText = true,
            RetentionDays = 30
        });

        var saved = (await _feeds.GetAsync(id))!;
        Assert.Equal(folderId, saved.FolderId);
        Assert.Equal("Renamed", saved.TitleOverride);
        Assert.False(saved.IsEnabled);
        Assert.Equal(15, saved.RefreshIntervalMinutes);
        Assert.False(saved.AutoDownload);
        Assert.True(saved.FetchFullText);
        Assert.Equal(30, saved.RetentionDays);
    }

    /// <summary>
    /// A feed moved from daily to quarter-hourly used to keep the due time
    /// the last fetch calculated under the old interval, so the change did
    /// not take effect for a day.
    /// </summary>
    [Fact]
    public async Task Shortening_the_interval_brings_the_due_time_forward()
    {
        var id = await AddAsync();
        var fetched = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        await _feeds.RecordSuccessAsync(id, null, null, fetched, fetched.AddHours(24));

        var snapshot = (await _feeds.GetAsync(id))!;
        await _feeds.UpdateAsync(snapshot with { RefreshIntervalMinutes = 15 });

        var saved = (await _feeds.GetAsync(id))!;
        Assert.Equal(fetched.AddMinutes(15), saved.NextDueUtc);
    }

    [Fact]
    public async Task Lengthening_the_interval_pushes_the_due_time_out()
    {
        var id = await AddAsync();
        var fetched = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        await _feeds.RecordSuccessAsync(id, null, null, fetched, fetched.AddMinutes(15));

        var snapshot = (await _feeds.GetAsync(id))!;
        await _feeds.UpdateAsync(snapshot with { RefreshIntervalMinutes = 1440 });

        var saved = (await _feeds.GetAsync(id))!;
        Assert.Equal(fetched.AddMinutes(1440), saved.NextDueUtc);
    }

    /// <summary>
    /// Back to "use the global setting", which this class does not know the
    /// value of. A null due time means the next scheduler pass picks the feed
    /// up and recalculates from there.
    /// </summary>
    [Fact]
    public async Task Clearing_the_interval_override_makes_the_feed_due_again()
    {
        var id = await AddAsync();
        var fetched = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        await _feeds.RecordSuccessAsync(id, null, null, fetched, fetched.AddHours(24));
        await _feeds.UpdateAsync((await _feeds.GetAsync(id))! with { RefreshIntervalMinutes = 60 });

        await _feeds.UpdateAsync((await _feeds.GetAsync(id))! with { RefreshIntervalMinutes = null });

        var saved = (await _feeds.GetAsync(id))!;
        Assert.Null(saved.NextDueUtc);
    }

    [Fact]
    public async Task Saving_without_changing_the_interval_leaves_the_due_time_alone()
    {
        var id = await AddAsync();
        var fetched = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        var due = fetched.AddHours(24);
        await _feeds.RecordSuccessAsync(id, null, null, fetched, due);

        await _feeds.UpdateAsync((await _feeds.GetAsync(id))! with { TitleOverride = "Renamed" });

        var saved = (await _feeds.GetAsync(id))!;
        Assert.Equal(due, saved.NextDueUtc);
    }

    // --- RecordFailureAsync's read-back ---

    [Fact]
    public async Task Recording_a_failure_returns_the_count_the_database_now_holds()
    {
        var id = await AddAsync();
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");

        var first = await _feeds.RecordFailureAsync(id, "boom", now, now.AddMinutes(5));
        var second = await _feeds.RecordFailureAsync(id, "boom", now, now.AddMinutes(5));

        Assert.True(first.Found);
        Assert.Equal(1, first.ConsecutiveFailures);
        Assert.True(first.IsEnabled);
        Assert.Equal(2, second.ConsecutiveFailures);
    }

    [Fact]
    public async Task Recording_a_failure_reports_a_feed_the_user_paused_as_disabled()
    {
        var id = await AddAsync();
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        await _feeds.SetEnabledAsync(id, false);

        var state = await _feeds.RecordFailureAsync(id, "boom", now, now.AddMinutes(5));

        Assert.True(state.Found);
        Assert.False(state.IsEnabled);
    }

    [Fact]
    public async Task Recording_a_failure_against_a_deleted_feed_reports_it_as_missing()
    {
        var id = await AddAsync();
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        await _feeds.DeleteAsync(id);

        var state = await _feeds.RecordFailureAsync(id, "boom", now, now.AddMinutes(5));

        Assert.False(state.Found);
    }

    /// <summary>
    /// Concurrent failures against one feed each add exactly one, which is
    /// what makes the auto-pause threshold reachable rather than steppable
    /// over.
    /// </summary>
    [Fact]
    public async Task Overlapping_failures_each_count_once()
    {
        var id = await AddAsync();
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");

        var counts = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
            (await _feeds.RecordFailureAsync(id, "boom", now, now.AddMinutes(5))).ConsecutiveFailures));

        Assert.Equal([1, 2, 3, 4, 5], counts.OrderBy(c => c).ToArray());
        Assert.Equal(5, (await _feeds.GetAsync(id))!.ConsecutiveFailures);
    }
}
