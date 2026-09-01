using System.Net;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Core.Sync;
using LucidReader.Core.Tests.Feeds;
using LucidReader.Core.Tests.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Sync;

/// <summary>
/// The categories a publisher puts on an item, and what a refresh does with
/// them.
///
/// The rule these tests exist to pin down is stated in full on
/// FeedRefreshService.ImportPublisherCategoriesAsync: import once, when the
/// ARTICLE first enters the database, and never again. The two tests that
/// matter most are the two that assert "never again" - a tag the user took
/// off must not be put back by the next poll, and must not be put back by
/// subscribing to the same site's other feed either.
/// </summary>
public class PublisherTagImportTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time =
        new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));

    private ReaderDatabase _db = null!;
    private FeedRepository _feeds = null!;
    private ItemRepository _items = null!;
    private TagRepository _tags = null!;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _feeds = new FeedRepository(_db);
        _items = new ItemRepository(_db);
        _tags = new TagRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private FeedRefreshService CreateService(StubHttpHandler handler) =>
        new(_feeds, _items, _tags,
            new FeedFetcher(handler.CreateClient()),
            new FeedParser(),
            new BackoffPolicy(new Random(11)),
            () => ReaderSettings.Defaults,
            _time);

    private static StubHttpHandler Serving(string fixture) =>
        StubHttpHandler.Returning(HttpStatusCode.OK, Fixtures.Feed(fixture));

    private Task<long> AddFeedAsync(string url) => _feeds.AddAsync(new Feed { FeedUrl = url });

    private async Task<long> FirstItemIdAsync(long feedId)
    {
        var stored = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 100, 0));
        return stored.Single(item => item.Title == "A well-filed post").Id;
    }

    [Fact]
    public async Task A_refresh_stores_the_publishers_categories_as_tags()
    {
        await using var service = CreateService(Serving("rss2-categories.xml"));
        var feedId = await AddFeedAsync("https://categories.example/rss");

        await service.RefreshNowAsync(feedId);

        Assert.Equal(
            ["AI", "Architecture", "ASP.NET", "Patterns", "Performance", "StyloBot"],
            await _tags.GetForItemAsync(await FirstItemIdAsync(feedId)));
    }

    /// <summary>
    /// The Tags section of the sidebar is built from GetUsageAsync, so this is
    /// the assertion that the tags actually show up where a user would look
    /// for them rather than only hanging off one item.
    /// </summary>
    [Fact]
    public async Task The_imported_tags_appear_in_the_tag_usage_the_sidebar_is_built_from()
    {
        await using var service = CreateService(Serving("rss2-categories.xml"));
        var feedId = await AddFeedAsync("https://categories.example/rss");

        await service.RefreshNowAsync(feedId);

        var usage = await _tags.GetUsageAsync();
        Assert.Contains(usage, tag => tag.Name == "StyloBot" && tag.ArticleCount == 1);

        // The second item's four refused categories created nothing, so the
        // only names here are the six good ones plus that item's one usable
        // name.
        Assert.Equal(7, usage.Count);
    }

    [Fact]
    public async Task A_feed_with_no_categories_creates_no_tags()
    {
        await using var service = CreateService(Serving("rss2-simple.xml"));
        var feedId = await AddFeedAsync("https://example.com/feed.xml");

        await service.RefreshNowAsync(feedId);

        Assert.Empty(await _tags.GetAllAsync());
    }

    /// <summary>
    /// The whole point. A feed relists every item on every fetch, categories
    /// included; a user who removes one is entitled to have it stay removed.
    /// </summary>
    [Fact]
    public async Task A_tag_the_user_removed_does_not_come_back_on_the_next_refresh()
    {
        await using var service = CreateService(Serving("rss2-categories.xml"));
        var feedId = await AddFeedAsync("https://categories.example/rss");

        await service.RefreshNowAsync(feedId);
        var itemId = await FirstItemIdAsync(feedId);
        await _tags.RemoveFromItemAsync(itemId, "Performance");

        await service.RefreshNowAsync(feedId);

        var tags = await _tags.GetForItemAsync(itemId);
        Assert.DoesNotContain("Performance", tags);
        Assert.Equal(5, tags.Count);
    }

    /// <summary>
    /// The same, for a user who removed every one of them: a refresh must not
    /// refill an article the user deliberately emptied.
    /// </summary>
    [Fact]
    public async Task An_article_the_user_stripped_of_tags_stays_stripped()
    {
        await using var service = CreateService(Serving("rss2-categories.xml"));
        var feedId = await AddFeedAsync("https://categories.example/rss");

        await service.RefreshNowAsync(feedId);
        var itemId = await FirstItemIdAsync(feedId);
        foreach (var name in await _tags.GetForItemAsync(itemId))
            await _tags.RemoveFromItemAsync(itemId, name);

        await service.RefreshNowAsync(feedId);

        Assert.Empty(await _tags.GetForItemAsync(itemId));
    }

    /// <summary>
    /// The dedupe interaction. mostlylucid.net publishes the same posts as RSS
    /// and as Atom; subscribing to both gives every article two rows sharing a
    /// canonical_id. The second row is new, the article is not, and importing
    /// its categories would re-apply to the article exactly the tags the user
    /// had removed from it.
    /// </summary>
    [Fact]
    public async Task A_second_feed_carrying_the_same_article_does_not_re_apply_its_categories()
    {
        // One handler, two addresses: the same two articles, once as RSS and
        // once as Atom, at the same links and so with the same canonical_id.
        var handler = new StubHttpHandler(request =>
            StubHttpHandler.Response(
                Fixtures.Feed(request.RequestUri!.AbsolutePath == "/rss"
                    ? "rss2-categories.xml"
                    : "atom-categories-twin.xml"),
                "application/xml"));

        await using var service = CreateService(handler);

        var rssFeedId = await AddFeedAsync("https://categories.example/rss");
        await service.RefreshNowAsync(rssFeedId);

        var itemId = await FirstItemIdAsync(rssFeedId);
        await _tags.RemoveFromItemAsync(itemId, "Performance");

        var twinFeedId = await AddFeedAsync("https://categories.example/atom");
        await service.RefreshNowAsync(twinFeedId);

        // The Atom copy really did arrive - the guard is worth having, since
        // an assertion about a tag not coming back passes just as well when
        // nothing was stored at all.
        var twinRows = await _items.QueryAsync(
            new ItemQuery(twinFeedId, null, ItemFilter.All, 100, 0));
        Assert.Equal(2, twinRows.Count);

        Assert.DoesNotContain("Performance", await _tags.GetForItemAsync(itemId));

        // And the removal held on the new copy as well: tags are article-level,
        // so a twin showing the tag the shown copy lost would be the same bug
        // seen from the other row.
        var twinId = twinRows.Single(item => item.Title == "A well-filed post").Id;
        Assert.DoesNotContain("Performance", await _tags.GetForItemAsync(twinId));
    }
}
