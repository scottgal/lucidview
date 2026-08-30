using LucidReader.Core.Model;
using LucidReader.Core.Opml;
using LucidReader.Core.Storage;
using LucidReader.Core.Tests.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Opml;

/// <summary>
/// Export wrote the displayed name and import read it back into feeds.title,
/// the publisher-owned column, so a rename survived a reinstall only until
/// the first successful refresh adopted the publisher's own title over it.
/// </summary>
public class OpmlRenameRoundTripTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private FeedRepository _feeds = null!;
    private OpmlService _service = null!;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _feeds = new FeedRepository(_db);
        _service = new OpmlService(new FolderRepository(_db), _feeds);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");

    private async Task<string> ExportWithRenameAsync()
    {
        var id = await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            Title = "Publisher's own name",
            SiteUrl = "https://example.com"
        });
        await _feeds.UpdateTitleOverrideAsync(id, "My name for it");

        var opml = await _service.ExportAsync(Now);

        // A reinstall: the file is all that is carried across.
        await _feeds.DeleteAsync(id);
        return opml;
    }

    [Fact]
    public async Task A_rename_survives_export_import_and_the_next_refresh()
    {
        var opml = await ExportWithRenameAsync();

        var result = await _service.ImportAsync(opml);
        Assert.Equal(1, result.FeedsAdded);

        var imported = (await _feeds.GetAllAsync())[0];
        Assert.Equal("My name for it", imported.TitleOverride);
        Assert.Equal("My name for it", imported.DisplayTitle);

        // The first successful refresh adopts the publisher's title, which is
        // exactly the write that used to destroy the rename.
        await _feeds.UpdateTitleAndSiteUrlAsync(
            imported.Id, "Publisher's own name", "https://example.com");

        var refreshed = (await _feeds.GetAllAsync())[0];
        Assert.Equal("Publisher's own name", refreshed.Title);
        Assert.Equal("My name for it", refreshed.DisplayTitle);
    }

    /// <summary>
    /// The exported file still has to read correctly in other readers, which
    /// know nothing of the extra attribute: text carries the displayed name.
    /// </summary>
    [Fact]
    public async Task The_exported_text_attribute_is_still_the_displayed_name()
    {
        var opml = await ExportWithRenameAsync();

        Assert.Contains("text=\"My name for it\"", opml, StringComparison.Ordinal);
        Assert.Contains("lucidTitleOverride=\"My name for it\"", opml, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file from another reader has no override attribute, and its text is
    /// the publisher's name, so it belongs in feeds.title exactly as before.
    /// </summary>
    [Fact]
    public async Task A_file_from_another_reader_still_imports_its_title_normally()
    {
        const string opml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <opml version="2.0"><head><title>Subs</title></head><body>
            <outline text="Some Blog" type="rss" xmlUrl="https://other.example/feed.xml"/>
            </body></opml>
            """;

        await _service.ImportAsync(opml);

        var imported = (await _feeds.GetAllAsync())[0];
        Assert.Equal("Some Blog", imported.Title);
        Assert.Null(imported.TitleOverride);
    }

    /// <summary>
    /// A feed with no rename exports no override attribute, so a round trip
    /// leaves it exactly where it was.
    /// </summary>
    [Fact]
    public async Task A_feed_with_no_rename_round_trips_unchanged()
    {
        var id = await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            Title = "Publisher's own name"
        });
        var opml = await _service.ExportAsync(Now);
        await _feeds.DeleteAsync(id);

        Assert.DoesNotContain("lucidTitleOverride", opml, StringComparison.Ordinal);

        await _service.ImportAsync(opml);

        var imported = (await _feeds.GetAllAsync())[0];
        Assert.Equal("Publisher's own name", imported.Title);
        Assert.Null(imported.TitleOverride);
    }
}
