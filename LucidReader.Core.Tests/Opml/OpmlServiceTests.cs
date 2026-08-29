using LucidReader.Core.Model;
using LucidReader.Core.Opml;
using LucidReader.Core.Storage;
using LucidReader.Core.Tests.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Opml;

public class OpmlServiceTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private FolderRepository _folders = null!;
    private FeedRepository _feeds = null!;
    private OpmlService _service = null!;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _folders = new FolderRepository(_db);
        _feeds = new FeedRepository(_db);
        _service = new OpmlService(_folders, _feeds);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private static string Opml(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Opml", name));

    [Fact]
    public async Task Importing_a_flat_export_adds_every_feed_at_the_top_level()
    {
        var result = await _service.ImportAsync(Opml("flat.opml"));

        Assert.Equal(2, result.FeedsAdded);
        Assert.Equal(0, result.FoldersCreated);
        var feeds = await _feeds.GetAllAsync();
        Assert.Equal(2, feeds.Count);
        Assert.All(feeds, f => Assert.Null(f.FolderId));
    }

    [Fact]
    public async Task Importing_a_foldered_export_creates_the_folders()
    {
        var result = await _service.ImportAsync(Opml("foldered.opml"));

        Assert.Equal(2, result.FoldersCreated);
        Assert.Equal(4, result.FeedsAdded);

        var folders = await _folders.GetAllAsync();
        Assert.Contains(folders, f => f.Name == "News");
        Assert.Contains(folders, f => f.Name == "Personal");

        var feeds = await _feeds.GetAllAsync();
        var newsFolder = folders.Single(f => f.Name == "News");
        Assert.Equal(2, feeds.Count(f => f.FolderId == newsFolder.Id));
        Assert.Equal(1, feeds.Count(f => f.FolderId is null));
    }

    [Fact]
    public async Task A_feed_already_subscribed_is_skipped_rather_than_duplicated()
    {
        await _feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });

        var result = await _service.ImportAsync(Opml("flat.opml"));

        Assert.Equal(1, result.FeedsAdded);
        Assert.Equal(1, result.FeedsSkipped);
        Assert.Equal(2, (await _feeds.GetAllAsync()).Count);
    }

    [Fact]
    public async Task A_duplicate_within_the_same_file_is_only_added_once()
    {
        var result = await _service.ImportAsync(Opml("awkward.opml"));

        Assert.Equal(1, result.FeedsSkipped);
        Assert.Single((await _feeds.GetAllAsync()).Where(
            f => f.FeedUrl == "https://a.example/feed.xml"));
    }

    [Fact]
    public async Task Nesting_deeper_than_one_level_is_flattened_to_the_outermost_folder()
    {
        await _service.ImportAsync(Opml("awkward.opml"));

        var folders = await _folders.GetAllAsync();
        Assert.Contains(folders, f => f.Name == "Outer");
        Assert.DoesNotContain(folders, f => f.Name == "Inner");

        var outer = folders.Single(f => f.Name == "Outer");
        var deep = (await _feeds.GetAllAsync()).Single(f => f.FeedUrl == "https://b.example/feed.xml");
        Assert.Equal(outer.Id, deep.FolderId);
    }

    [Fact]
    public async Task Importing_the_same_folder_name_twice_reuses_the_folder()
    {
        await _service.ImportAsync(Opml("foldered.opml"));
        await _service.ImportAsync(Opml("foldered.opml"));

        Assert.Equal(2, (await _folders.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Exporting_then_importing_into_an_empty_database_reproduces_the_structure()
    {
        await _service.ImportAsync(Opml("foldered.opml"));
        var exported = await _service.ExportAsync(DateTimeOffset.Parse("2026-08-29T10:00:00Z"));

        using var second = new TempDatabase();
        await using var db2 = await ReaderDatabase.OpenAsync(second.Path);
        var service2 = new OpmlService(new FolderRepository(db2), new FeedRepository(db2));

        var result = await service2.ImportAsync(exported);

        Assert.Equal(4, result.FeedsAdded);
        Assert.Equal(2, result.FoldersCreated);
    }

    [Fact]
    public async Task Importing_a_file_that_is_not_opml_throws_before_writing_anything()
    {
        await Assert.ThrowsAsync<OpmlParseException>(
            () => _service.ImportAsync(Opml("not-opml.xml")));

        Assert.Empty(await _feeds.GetAllAsync());
        Assert.Empty(await _folders.GetAllAsync());
    }

    [Fact]
    public async Task An_outline_with_no_feed_and_no_children_creates_nothing()
    {
        await _service.ImportAsync(Opml("awkward.opml"));

        Assert.DoesNotContain(await _folders.GetAllAsync(), f => f.Name == "Empty container");
    }
}
