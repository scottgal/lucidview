using System.Text;
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

    private static string DeeplyNestedOpml(int depth)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?><opml version=\"2.0\">")
            .Append("<head><title>Deep</title></head><body>");
        for (var i = 0; i < depth; i++)
            builder.Append("<outline text=\"L").Append(i).Append("\">");
        builder.Append("<outline text=\"Leaf\" xmlUrl=\"https://leaf.example/feed.xml\"/>");
        for (var i = 0; i < depth; i++)
            builder.Append("</outline>");
        builder.Append("</body></opml>");
        return builder.ToString();
    }

    /// <summary>
    /// Throws on one chosen feed URL so tests can simulate a write failure
    /// partway through an import without a database fault to engineer.
    /// AddAsync is virtual on FeedRepository for exactly this purpose.
    /// </summary>
    private sealed class FailingFeedRepository(ReaderDatabase db, string urlToFail) : FeedRepository(db)
    {
        public override Task<long> AddAsync(Feed feed, CancellationToken ct = default) =>
            string.Equals(feed.FeedUrl, urlToFail, StringComparison.OrdinalIgnoreCase)
                ? throw new InvalidOperationException("Simulated write failure.")
                : base.AddAsync(feed, ct);
    }

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
    public async Task A_feed_outline_with_children_imports_both_the_feed_and_its_children()
    {
        var result = await _service.ImportAsync(Opml("nested-feed.opml"));

        Assert.Equal(2, result.FeedsAdded);
        Assert.Equal(0, result.FeedsFailed);

        var feedUrls = (await _feeds.GetAllAsync()).Select(f => f.FeedUrl).ToList();
        Assert.Contains("https://parent.example/feed.xml", feedUrls);
        Assert.Contains("https://child.example/feed.xml", feedUrls);
    }

    [Fact]
    public async Task A_feed_that_fails_to_write_is_counted_and_the_rest_still_import()
    {
        var failingFeeds = new FailingFeedRepository(_db, "https://another.example/atom.xml");
        var service = new OpmlService(_folders, failingFeeds);

        var result = await service.ImportAsync(Opml("flat.opml"));

        Assert.Equal(1, result.FeedsAdded);
        Assert.Equal(1, result.FeedsFailed);
        var failure = Assert.Single(result.FailedFeeds);
        Assert.Equal("https://another.example/atom.xml", failure.FeedUrl);
        Assert.Equal(nameof(InvalidOperationException), failure.ExceptionType);
        Assert.Equal("Simulated write failure.", failure.Message);

        var stored = await failingFeeds.GetAllAsync();
        var storedFeed = Assert.Single(stored);
        Assert.Equal("https://example.com/feed.xml", storedFeed.FeedUrl);
    }

    [Fact]
    public async Task Nesting_deeper_than_the_depth_limit_writes_nothing()
    {
        var opml = DeeplyNestedOpml(150);

        await Assert.ThrowsAsync<OpmlParseException>(() => _service.ImportAsync(opml));

        Assert.Empty(await _feeds.GetAllAsync());
        Assert.Empty(await _folders.GetAllAsync());
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
        var folders2 = new FolderRepository(db2);
        var feeds2 = new FeedRepository(db2);
        var service2 = new OpmlService(folders2, feeds2);

        var result = await service2.ImportAsync(exported);

        Assert.Equal(4, result.FeedsAdded);
        Assert.Equal(2, result.FoldersCreated);

        // Counts alone would pass even if feeds ended up under the wrong
        // folder, so assert the actual membership survived the round trip.
        var reimportedFolders = await folders2.GetAllAsync();
        var reimportedFeeds = await feeds2.GetAllAsync();

        var news = reimportedFolders.Single(f => f.Name == "News");
        var personal = reimportedFolders.Single(f => f.Name == "Personal");

        var newsUrls = reimportedFeeds.Where(f => f.FolderId == news.Id)
            .Select(f => f.FeedUrl).ToList();
        Assert.Equal(2, newsUrls.Count);
        Assert.Contains("https://news.example/world.xml", newsUrls);
        Assert.Contains("https://news.example/tech.xml", newsUrls);

        var personalFeed = Assert.Single(
            reimportedFeeds.Where(f => f.FolderId == personal.Id));
        Assert.Equal("https://friend.example/feed.xml", personalFeed.FeedUrl);

        var looseFeed = Assert.Single(
            reimportedFeeds.Where(f => f.FolderId is null));
        Assert.Equal("https://loose.example/feed.xml", looseFeed.FeedUrl);
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
