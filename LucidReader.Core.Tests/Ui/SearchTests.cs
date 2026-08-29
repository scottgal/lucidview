using LucidReader.Core.Model;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class SearchTests : IAsyncLifetime
{
    private string _dir = string.Empty;
    private ReaderServices _services = null!;

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lucidreader-uitests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _services = await ReaderServices.StartAsync(
            Path.Combine(_dir, "reader.db"), Path.Combine(_dir, "settings.json"));

        var feed = await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        var one = await _services.Items.UpsertAsync(new FeedItem
        {
            FeedId = feed, Guid = "g1", Title = "Avalonia rendering internals",
            FirstSeenUtc = DateTimeOffset.Parse("2026-08-29T10:00:00Z")
        });
        await _services.Items.SetContentAsync(one, "Discussing the compositor in depth.", ContentSource.Feed);

        var two = await _services.Items.UpsertAsync(new FeedItem
        {
            FeedId = feed, Guid = "g2", Title = "Something unrelated",
            FirstSeenUtc = DateTimeOffset.Parse("2026-08-29T10:00:00Z")
        });
        await _services.Items.SetContentAsync(two, "Nothing to see.", ContentSource.Feed);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task Searching_matches_titles()
    {
        var results = await _services.Search.SearchAsync("Avalonia", 50);

        Assert.Single(results);
    }

    [Fact]
    public async Task Searching_matches_article_bodies()
    {
        var results = await _services.Search.SearchAsync("compositor", 50);

        Assert.Single(results);
    }

    [Fact]
    public async Task A_query_with_punctuation_returns_nothing_rather_than_throwing()
    {
        var results = await _services.Search.SearchAsync("\"unbalanced AND (", 50);

        Assert.Empty(results);
    }

    [Fact]
    public async Task A_blank_query_returns_nothing()
    {
        Assert.Empty(await _services.Search.SearchAsync("   ", 50));
    }
}
