using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class SearchRepositoryTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private SearchRepository _search = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _search = new SearchRepository(_db);
        _feedId = await new FeedRepository(_db).AddAsync(
            new Feed { FeedUrl = "https://example.com/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private async Task<long> AddAsync(string guid, string title, string? content = null)
    {
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = guid,
            Title = title,
            FirstSeenUtc = DateTimeOffset.Parse("2026-08-28T10:00:00Z")
        });
        if (content is not null)
            await _items.SetContentAsync(id, content, ContentSource.Feed);
        return id;
    }

    [Fact]
    public async Task Searching_matches_on_title()
    {
        await AddAsync("a", "Avalonia rendering internals");
        await AddAsync("b", "Something unrelated");

        var results = await _search.SearchAsync("Avalonia", 50);

        Assert.Single(results);
        Assert.Equal("a", results[0].Guid);
    }

    [Fact]
    public async Task Searching_matches_on_article_body()
    {
        await AddAsync("a", "A title with nothing useful in it",
            "The body mentions SQLite and its writer lock.");
        await AddAsync("b", "Another title");

        var results = await _search.SearchAsync("writer lock", 50);

        Assert.Single(results);
        Assert.Equal("a", results[0].Guid);
    }

    [Fact]
    public async Task Content_added_after_insert_becomes_searchable()
    {
        var id = await AddAsync("a", "Placeholder title");

        await _items.SetContentAsync(id, "Now containing the word marmalade.", ContentSource.Extracted);

        var results = await _search.SearchAsync("marmalade", 50);
        Assert.Single(results);
    }

    [Fact]
    public async Task Deleting_an_item_removes_it_from_the_index()
    {
        await AddAsync("a", "Ephemeral coordinators");
        await new FeedRepository(_db).DeleteAsync(_feedId);

        var results = await _search.SearchAsync("Ephemeral", 50);

        Assert.Empty(results);
    }

    [Fact]
    public async Task A_query_with_fts_syntax_characters_does_not_throw()
    {
        await AddAsync("a", "Perfectly normal article");

        var results = await _search.SearchAsync("\"unbalanced quote AND (", 50);

        Assert.Empty(results);
    }

    [Fact]
    public async Task An_empty_query_returns_nothing_rather_than_everything()
    {
        await AddAsync("a", "Perfectly normal article");

        var results = await _search.SearchAsync("   ", 50);

        Assert.Empty(results);
    }
}
