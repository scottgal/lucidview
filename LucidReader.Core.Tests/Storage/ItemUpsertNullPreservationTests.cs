using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// FeedParser only requires one of guid, link or title, so a null title,
/// link, summary or date is an ordinary parse result. The upsert used to
/// write those nulls straight over what was already stored, which meant a
/// publisher moving item bodies from description to content:encoded blanked
/// every stored summary in the feed - and for an item whose download failed,
/// or that was stored while auto-download was off, the summary is the only
/// body there is.
/// </summary>
public class ItemUpsertNullPreservationTests : IAsyncLifetime
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
            new Feed { FeedUrl = "https://example.com/feed.xml", Title = "Example" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private FeedItem Full() => new()
    {
        FeedId = _feedId,
        Guid = "guid-1",
        Title = "The original headline",
        Link = "https://example.com/articles/1",
        Author = "A Writer",
        Summary = "The only copy of this article's text.",
        PublishedUtc = DateTimeOffset.Parse("2026-08-28T09:00:00Z"),
        UpdatedUtc = DateTimeOffset.Parse("2026-08-28T09:30:00Z"),
        FirstSeenUtc = DateTimeOffset.Parse("2026-08-28T10:00:00Z")
    };

    private FeedItem Emptied() => new()
    {
        FeedId = _feedId,
        Guid = "guid-1",
        FirstSeenUtc = DateTimeOffset.Parse("2026-08-29T10:00:00Z")
    };

    [Fact]
    public async Task Re_upserting_with_nulls_keeps_every_publisher_field_we_already_had()
    {
        var id = await _items.UpsertAsync(Full());

        await _items.UpsertAsync(Emptied());

        var loaded = await _items.GetAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("The original headline", loaded!.Title);
        Assert.Equal("https://example.com/articles/1", loaded.Link);
        Assert.Equal("A Writer", loaded.Author);
        Assert.Equal("The only copy of this article's text.", loaded.Summary);
        Assert.Equal(DateTimeOffset.Parse("2026-08-28T09:00:00Z"), loaded.PublishedUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-08-28T09:30:00Z"), loaded.UpdatedUtc);
    }

    [Fact]
    public async Task Re_upserting_with_nulls_leaves_the_search_entry_intact()
    {
        await _items.UpsertAsync(Full());
        await _items.UpsertAsync(Emptied());

        var hits = await _search.SearchAsync("headline", 10);

        Assert.Single(hits);
    }

    /// <summary>
    /// The rule is "keep the last non-null value", not "never change
    /// anything": a real correction still has to land.
    /// </summary>
    [Fact]
    public async Task A_real_publisher_correction_still_overwrites()
    {
        var id = await _items.UpsertAsync(Full());

        await _items.UpsertAsync(Full() with
        {
            Title = "The corrected headline",
            Summary = "Rewritten body."
        });

        var loaded = await _items.GetAsync(id);
        Assert.Equal("The corrected headline", loaded!.Title);
        Assert.Equal("Rewritten body.", loaded.Summary);
    }

    /// <summary>
    /// The batch path binds its own parameters rather than going through
    /// UpsertAsync, so it gets its own coverage.
    /// </summary>
    [Fact]
    public async Task The_batch_path_preserves_the_same_way()
    {
        await _items.UpsertManyAsync([Full()]);
        await _items.UpsertManyAsync([Emptied()]);

        var stored = Assert.Single(
            await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0)));
        Assert.Equal("The original headline", stored.Title);
        Assert.Equal("https://example.com/articles/1", stored.Link);
        Assert.Equal("The only copy of this article's text.", stored.Summary);
    }

    /// <summary>
    /// image_url and content_markdown were already protected, by being left
    /// out of the update list entirely. Confirming that here keeps the two
    /// halves of "the upsert never destroys what we hold" in one place.
    /// </summary>
    [Fact]
    public async Task Downloaded_content_and_the_captured_image_still_survive()
    {
        var id = await _items.UpsertAsync(Full());
        await _items.SetContentAsync(id, "# Downloaded", ContentSource.Extracted, "https://cdn/pic.png");

        await _items.UpsertAsync(Emptied());

        var loaded = await _items.GetAsync(id);
        Assert.Equal("# Downloaded", loaded!.ContentMarkdown);
        Assert.Equal("https://cdn/pic.png", loaded.ImageUrl);
    }

    [Fact]
    public async Task A_null_only_upsert_of_an_unseen_guid_still_inserts_the_row()
    {
        var id = await _items.UpsertAsync(Emptied());

        var loaded = await _items.GetAsync(id);
        Assert.NotNull(loaded);
        Assert.Null(loaded!.Title);
    }

    /// <summary>
    /// Reads the raw column rather than the mapped model, so a null that the
    /// mapper might paper over cannot pass unnoticed.
    /// </summary>
    [Fact]
    public async Task The_stored_columns_themselves_are_not_null_afterwards()
    {
        await _items.UpsertAsync(Full());
        await _items.UpsertAsync(Emptied());

        await using var connection = new SqliteConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT title, link, summary, published_utc, updated_utc, author FROM items;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        for (var column = 0; column < reader.FieldCount; column++)
            Assert.False(reader.IsDBNull(column), $"column {column} was null");
    }
}
