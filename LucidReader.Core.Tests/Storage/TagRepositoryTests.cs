using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class TagRepositoryTests : IAsyncLifetime
{
    private static readonly string[] ReadingOnly = { "reading" };
    private static readonly string[] BOnly = { "b" };
    private static readonly string[] UsedOnly = { "used" };

    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private TagRepository _tags = null!;
    private ItemRepository _items = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _tags = new TagRepository(_db);
        _items = new ItemRepository(_db);
        _feedId = await new FeedRepository(_db).AddAsync(
            new Feed { FeedUrl = "https://example.com/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private Task<long> AddItemAsync(string guid) => _items.UpsertAsync(new FeedItem
    {
        FeedId = _feedId,
        Guid = guid,
        Title = guid,
        FirstSeenUtc = DateTimeOffset.Parse("2026-08-29T10:00:00Z")
    });

    [Fact]
    public async Task Creating_the_same_tag_twice_returns_the_same_id()
    {
        var first = await _tags.GetOrCreateAsync("dotnet");
        var second = await _tags.GetOrCreateAsync("dotnet");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Tag_names_are_matched_case_insensitively()
    {
        var lower = await _tags.GetOrCreateAsync("dotnet");
        var upper = await _tags.GetOrCreateAsync("DotNet");

        Assert.Equal(lower, upper);
        Assert.Single(await _tags.GetAllAsync());
    }

    [Fact]
    public async Task A_tag_can_be_added_to_an_item_and_read_back()
    {
        var id = await AddItemAsync("g1");

        await _tags.AddToItemAsync(id, "reading");

        Assert.Equal(ReadingOnly, await _tags.GetForItemAsync(id));
    }

    [Fact]
    public async Task Adding_the_same_tag_to_an_item_twice_is_harmless()
    {
        var id = await AddItemAsync("g1");

        await _tags.AddToItemAsync(id, "reading");
        await _tags.AddToItemAsync(id, "reading");

        Assert.Single(await _tags.GetForItemAsync(id));
    }

    [Fact]
    public async Task Removing_a_tag_leaves_the_others()
    {
        var id = await AddItemAsync("g1");
        await _tags.AddToItemAsync(id, "a");
        await _tags.AddToItemAsync(id, "b");

        await _tags.RemoveFromItemAsync(id, "a");

        Assert.Equal(BOnly, await _tags.GetForItemAsync(id));
    }

    [Fact]
    public async Task Items_can_be_listed_by_tag()
    {
        var one = await AddItemAsync("g1");
        var two = await AddItemAsync("g2");
        await AddItemAsync("g3");
        await _tags.AddToItemAsync(one, "keep");
        await _tags.AddToItemAsync(two, "keep");

        var tagged = await _tags.GetItemsWithTagAsync("keep", 50);

        Assert.Equal(2, tagged.Count);
    }

    [Fact]
    public async Task Deleting_an_item_removes_its_tag_links()
    {
        var id = await AddItemAsync("g1");
        await _tags.AddToItemAsync(id, "keep");

        await new FeedRepository(_db).DeleteAsync(_feedId);

        Assert.Empty(await _tags.GetItemsWithTagAsync("keep", 50));
    }

    [Fact]
    public async Task Unused_tags_can_be_cleaned_up_but_used_ones_survive()
    {
        var id = await AddItemAsync("g1");
        await _tags.AddToItemAsync(id, "used");
        await _tags.GetOrCreateAsync("orphan");

        var removed = await _tags.DeleteUnusedAsync();

        Assert.Equal(1, removed);
        Assert.Equal(UsedOnly, await _tags.GetAllAsync());
    }

    [Fact]
    public async Task A_blank_tag_name_is_rejected()
    {
        var id = await AddItemAsync("g1");

        await Assert.ThrowsAsync<ArgumentException>(() => _tags.AddToItemAsync(id, "   "));
    }

    [Fact]
    public async Task Concurrent_GetOrCreate_calls_for_case_variants_never_create_a_duplicate()
    {
        // Naive select-then-insert (a QueryAsync SELECT followed by a separate
        // WriteReturningIdAsync INSERT) races: two concurrent calls for
        // different-case spellings of the same name can both pass the
        // COLLATE NOCASE select before either has inserted, and ix_tags_name
        // is case-sensitive, so both inserts succeed. Firing a batch of
        // concurrent calls mixing case is the only way to exercise that
        // window; a single pair occasionally slips through even the broken
        // implementation, so this repeats the mix several times over to make
        // a false pass unlikely rather than asserting on one shot.
        string[] variants = ["dotnet", "DotNet", "DOTNET", "dOtNeT"];
        var calls = new List<Task<long>>();
        for (var round = 0; round < 10; round++)
            foreach (var variant in variants)
                calls.Add(_tags.GetOrCreateAsync(variant));

        var ids = await Task.WhenAll(calls);

        Assert.Single(ids.Distinct());
        Assert.Single(await _tags.GetAllAsync());
    }
}
