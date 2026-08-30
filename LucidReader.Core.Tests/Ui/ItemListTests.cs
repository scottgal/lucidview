using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class ItemListTests : IAsyncLifetime
{
    private string _dir = string.Empty;
    private ReaderServices _services = null!;

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mylo-uitests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _services = await ReaderServices.StartAsync(
            Path.Combine(_dir, "reader.db"), Path.Combine(_dir, "settings.json"));
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task Selecting_a_feed_queries_only_that_feed()
    {
        var a = await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        var b = await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://b.example/feed.xml" });
        await AddItemAsync(a, "a1");
        await AddItemAsync(b, "b1");

        var forA = await _services.Items.QueryAsync(new ItemQuery(a, null, ItemFilter.All, 200, 0));

        Assert.Single(forA);
    }

    [Fact]
    public async Task Selecting_a_folder_queries_every_feed_in_it()
    {
        var folder = await _services.Folders.AddAsync("News");
        var a = await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml", FolderId = folder });
        var b = await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://b.example/feed.xml", FolderId = folder });
        await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://c.example/feed.xml" });
        await AddItemAsync(a, "a1");
        await AddItemAsync(b, "b1");

        var inFolder = await _services.Items.QueryAsync(new ItemQuery(null, folder, ItemFilter.All, 200, 0));

        Assert.Equal(2, inFolder.Count);
    }

    [Fact]
    public async Task The_unread_filter_excludes_read_items()
    {
        var feed = await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        var read = await AddItemAsync(feed, "r");
        await AddItemAsync(feed, "u");
        await _services.Items.SetReadAsync(read, true);

        var unread = await _services.Items.QueryAsync(new ItemQuery(feed, null, ItemFilter.Unread, 200, 0));

        Assert.Single(unread);
        Assert.Equal("u", unread[0].Guid);
    }

    private Task<long> AddItemAsync(long feedId, string guid) => _services.Items.UpsertAsync(new FeedItem
    {
        FeedId = feedId,
        Guid = guid,
        Title = guid,
        FirstSeenUtc = DateTimeOffset.Parse("2026-08-29T10:00:00Z")
    });
}
