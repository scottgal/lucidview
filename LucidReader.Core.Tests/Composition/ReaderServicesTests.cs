using LucidReader;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Core.Tests.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Composition;

public class ReaderServicesTests
{
    private static (string db, string settings, string dir) TempPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lucidreader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (Path.Combine(dir, "reader.db"), Path.Combine(dir, "settings.json"), dir);
    }

    [Fact]
    public async Task Starting_builds_every_component()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            await using var services = await ReaderServices.StartAsync(db, settings);

            Assert.NotNull(services.Database);
            Assert.NotNull(services.Folders);
            Assert.NotNull(services.Feeds);
            Assert.NotNull(services.Items);
            Assert.NotNull(services.Search);
            Assert.NotNull(services.Tags);
            Assert.NotNull(services.Refresh);
            Assert.NotNull(services.Scheduler);
            Assert.NotNull(services.Downloader);
            Assert.NotNull(services.Retention);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Disposing_then_starting_again_on_the_same_path_succeeds()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            await (await ReaderServices.StartAsync(db, settings)).DisposeAsync();
            await using var second = await ReaderServices.StartAsync(db, settings);
            Assert.NotNull(second.Database);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Disposing_twice_does_not_throw()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            var services = await ReaderServices.StartAsync(db, settings);
            await services.DisposeAsync();
            await services.DisposeAsync();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Settings_round_trip_and_raise_the_change_event()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            await using var services = await ReaderServices.StartAsync(db, settings);
            ReaderSettings? seen = null;
            services.SettingsChanged += s => seen = s;

            await services.UpdateSettingsAsync(services.Settings with { FontSize = 21 });

            Assert.Equal(21, services.Settings.FontSize);
            Assert.NotNull(seen);
            Assert.Equal(21, seen!.FontSize);
            Assert.Equal(21, (await SettingsStore.LoadAsync(settings)).FontSize);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Refresh_and_download_concurrency_come_from_settings()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            await SettingsStore.SaveAsync(settings, ReaderSettings.Defaults with
            {
                MaxConcurrentFetches = 7,
                MaxConcurrentDownloads = 3
            });

            await using var services = await ReaderServices.StartAsync(db, settings);

            Assert.Equal(7, services.ConfiguredFetchConcurrency);
            Assert.Equal(3, services.ConfiguredDownloadConcurrency);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task A_completed_refresh_that_found_items_queues_them_for_download()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            await using var services = await ReaderServices.StartAsync(db, settings);
            var feedId = await services.Feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });

            var itemId = await services.Items.UpsertAsync(new FeedItem
            {
                FeedId = feedId,
                Guid = "g1",
                Title = "An item",
                FirstSeenUtc = DateTimeOffset.UtcNow,
                OfflineState = OfflineState.Pending
            });

            var queued = await services.QueuePendingDownloadsAsync();

            Assert.True(queued >= 1);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task A_vacuum_conversion_failure_is_surfaced_rather_than_hidden()
    {
        var (db, settings, dir) = TempPaths();
        try
        {
            await using var services = await ReaderServices.StartAsync(db, settings);

            // Normally null. The property exists so a failed conversion is
            // visible to the app rather than dying silently in the migrator.
            Assert.Equal(SchemaMigrator.LastIncrementalVacuumConversionError, services.StartupWarning);
        }
        finally { Directory.Delete(dir, true); }
    }
}
