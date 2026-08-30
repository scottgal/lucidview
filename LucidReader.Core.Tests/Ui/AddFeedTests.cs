using LucidReader.Core.Model;
using LucidReader.Core.Opml;
using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class AddFeedTests : IAsyncLifetime
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
    public async Task Adding_a_feed_puts_it_in_the_tree()
    {
        await _services.Feeds.AddAsync(new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            Title = "Example"
        });

        var feeds = await _services.Feeds.GetAllAsync();

        Assert.Single(feeds);
        Assert.Equal("Example", feeds[0].DisplayTitle);
    }

    [Fact]
    public async Task Adding_a_url_that_is_already_subscribed_is_rejected_by_the_unique_index()
    {
        await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _services.Feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" }));
    }

    /// <summary>
    /// The check the add flow actually relies on to report "already
    /// subscribed" instead of throwing: GetByUrlAsync has to find the row the
    /// unique index would reject.
    /// </summary>
    [Fact]
    public async Task An_already_subscribed_url_is_found_before_the_insert_is_attempted()
    {
        await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });

        Assert.NotNull(await _services.Feeds.GetByUrlAsync("https://example.com/feed.xml"));
        Assert.Null(await _services.Feeds.GetByUrlAsync("https://other.example/feed.xml"));
    }

    [Fact]
    public async Task Importing_opml_creates_folders_and_feeds()
    {
        var service = new OpmlService(_services.Folders, _services.Feeds);
        const string opml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <opml version="2.0">
              <head><title>Subscriptions</title></head>
              <body>
                <outline text="News">
                  <outline text="World" type="rss" xmlUrl="https://news.example/world.xml"/>
                </outline>
                <outline text="Loose" type="rss" xmlUrl="https://loose.example/feed.xml"/>
              </body>
            </opml>
            """;

        var result = await service.ImportAsync(opml);

        Assert.Equal(1, result.FoldersCreated);
        Assert.Equal(2, result.FeedsAdded);
        Assert.Equal(2, (await _services.Feeds.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Exporting_then_reimporting_elsewhere_reproduces_the_subscriptions()
    {
        var folder = await _services.Folders.AddAsync("Tech");
        await _services.Feeds.AddAsync(new Feed
        {
            FeedUrl = "https://a.example/feed.xml", Title = "A", FolderId = folder
        });
        await _services.Feeds.AddAsync(new Feed { FeedUrl = "https://b.example/feed.xml", Title = "B" });

        var service = new OpmlService(_services.Folders, _services.Feeds);
        var exported = await service.ExportAsync(DateTimeOffset.Parse("2026-08-29T10:00:00Z"));

        Assert.Contains("https://a.example/feed.xml", exported);
        Assert.Contains("Tech", exported);
        Assert.Contains("https://b.example/feed.xml", exported);
    }

    // ================= AddFeedInput, the dialog's plain half =================

    [Theory]
    [InlineData("xkcd.com", "https://xkcd.com")]
    [InlineData("  xkcd.com  ", "https://xkcd.com")]
    [InlineData("www.example.com/feed.xml", "https://www.example.com/feed.xml")]
    public void An_address_with_no_scheme_is_looked_up_over_https(string typed, string expected) =>
        Assert.Equal(expected, AddFeedInput.Normalise(typed));

    [Theory]
    [InlineData("http://example.com/feed.xml")]
    [InlineData("https://example.com/feed.xml")]
    [InlineData("HTTP://example.com/feed.xml")]
    public void An_address_that_already_has_a_scheme_is_left_exactly_as_typed(string typed) =>
        Assert.Equal(typed, AddFeedInput.Normalise(typed));

    [Fact]
    public void An_empty_address_normalises_to_empty_rather_than_to_a_bare_scheme()
    {
        Assert.Equal(string.Empty, AddFeedInput.Normalise(null));
        Assert.Equal(string.Empty, AddFeedInput.Normalise("   "));
    }

    [Fact]
    public void The_discovery_message_distinguishes_none_one_and_several()
    {
        Assert.Equal("No feeds found at that address.", AddFeedInput.DescribeDiscovery(0));
        Assert.Equal("Found one feed.", AddFeedInput.DescribeDiscovery(1));
        Assert.Equal("Found 3 feeds. Choose the ones you want.", AddFeedInput.DescribeDiscovery(3));
    }

    [Fact]
    public void An_already_subscribed_url_is_reported_rather_than_silently_duplicated()
    {
        Assert.Equal("Added 2 feeds.", AddFeedInput.DescribeAdded(2, 0));
        Assert.Equal("Added 1 feed.", AddFeedInput.DescribeAdded(1, 0));
        Assert.Equal(
            "Added 0 feeds, skipped 1 already subscribed.",
            AddFeedInput.DescribeAdded(0, 1));
    }

    [Fact]
    public void A_feed_that_could_not_be_written_is_counted_apart_from_one_already_subscribed()
    {
        Assert.Equal(
            "Added 1 feed, skipped 1 already subscribed. 2 could not be added.",
            AddFeedInput.DescribeAdded(1, 1, 2));
    }

    [Theory]
    [InlineData("ftp://example.com/feed")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    public void An_address_with_an_unsupported_scheme_keeps_that_scheme(string typed) =>
        Assert.Equal(typed, AddFeedInput.Normalise(typed));

    [Fact]
    public void A_bare_host_and_port_is_not_mistaken_for_a_scheme() =>
        Assert.Equal("https://example.com:8080/feed", AddFeedInput.Normalise("example.com:8080/feed"));

    [Fact]
    public void An_unsupported_scheme_is_reported_as_such_rather_than_as_no_feeds_found() =>
        Assert.Equal(
            AddFeedInput.UnsupportedSchemeMessage,
            AddFeedInput.DescribeAddressProblem(AddFeedInput.Normalise("ftp://example.com/feed")));

    [Theory]
    [InlineData("https://attacker:token@internal.corp/")]
    [InlineData("http://admin@example.com/feed.xml")]
    public void An_address_with_embedded_credentials_is_refused(string typed) =>
        Assert.Equal(
            AddFeedInput.CredentialsMessage,
            AddFeedInput.DescribeAddressProblem(AddFeedInput.Normalise(typed)));

    [Theory]
    [InlineData("https://example.com/feed.xml")]
    [InlineData("http://example.com/feed.xml")]
    public void A_plain_web_address_has_no_problem_to_report(string typed) =>
        Assert.Null(AddFeedInput.DescribeAddressProblem(AddFeedInput.Normalise(typed)));

    [Fact]
    public void The_first_failure_reason_is_carried_into_the_add_message() =>
        Assert.Equal(
            "Added 1 feed. 2 could not be added. First problem: SqliteException: constraint failed",
            AddFeedInput.DescribeAdded(1, 0, 2, "SqliteException: constraint failed"));

    [Fact]
    public void The_import_message_names_the_feeds_that_failed()
    {
        Assert.Equal(
            "Imported 0 feeds into 0 new folders. 2 could not be imported. " +
            "Not imported: https://a.example/f, https://b.example/f.",
            AddFeedInput.DescribeImport(0, 0, 0, 2, ["https://a.example/f", "https://b.example/f"]));

        Assert.Equal(
            "Imported 0 feeds into 0 new folders. 5 could not be imported. " +
            "Not imported: a, b, c and 2 more.",
            AddFeedInput.DescribeImport(0, 0, 0, 5, ["a", "b", "c", "d", "e"]));
    }

    [Fact]
    public void The_queue_message_says_when_not_everything_went_in()
    {
        Assert.Equal(string.Empty, AddFeedInput.DescribeQueued(0, 0));
        Assert.Equal(" Queued 3 for an immediate fetch.", AddFeedInput.DescribeQueued(3, 3));
        Assert.Equal(" Queued 3 of 40 for an immediate fetch.", AddFeedInput.DescribeQueued(3, 40));
    }

    [Fact]
    public void The_import_message_reports_folders_feeds_skips_and_failures()
    {
        Assert.Equal(
            "Imported 2 feeds into 1 new folder.",
            AddFeedInput.DescribeImport(1, 2, 0, 0));
        Assert.Equal(
            "Imported 0 feeds into 0 new folders, skipped 5 already subscribed.",
            AddFeedInput.DescribeImport(0, 0, 5, 0));
        Assert.Equal(
            "Imported 3 feeds into 2 new folders. 1 could not be imported.",
            AddFeedInput.DescribeImport(2, 3, 0, 1));
    }
}
