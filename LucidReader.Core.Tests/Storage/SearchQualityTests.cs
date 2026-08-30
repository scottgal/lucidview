using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// What "the search is good" means, asserted against a real database rather
/// than against the query string the builder produced.
///
/// Four properties are covered here, each of which was a gap before the V5
/// index and the ranked query replaced "SELECT ... ORDER BY rank":
/// an item whose only body is its summary is findable at all; a partly-typed
/// word matches; a headline match outranks a passing mention; and a result
/// can say which passage it matched on.
/// </summary>
public class SearchQualityTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private SearchRepository _search = null!;
    private long _folderId;
    private long _alphaId;
    private long _betaId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _search = new SearchRepository(_db);

        var feeds = new FeedRepository(_db);
        _folderId = await new FolderRepository(_db).AddAsync("Folder");
        _alphaId = await feeds.AddAsync(new Feed
        {
            FeedUrl = "https://alpha.test/feed.xml",
            FolderId = _folderId
        });
        _betaId = await feeds.AddAsync(new Feed { FeedUrl = "https://beta.test/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private async Task<long> AddAsync(
        string guid,
        string? title = null,
        string? summary = null,
        string? author = null,
        string? content = null,
        long? feedId = null)
    {
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = feedId ?? _alphaId,
            Guid = guid,
            Title = title,
            Summary = summary,
            Author = author,
            FirstSeenUtc = DateTimeOffset.Parse("2026-08-28T10:00:00Z")
        });

        if (content is not null)
            await _items.SetContentAsync(id, content, ContentSource.Feed);

        return id;
    }

    // --- Gap 1: summary and author are indexed ---

    /// <summary>
    /// The case this whole migration exists for. An item whose article was
    /// never downloaded - auto-download off, a failed fetch, or the
    /// offline_state = 0 items GetPendingOfflineAsync never re-queues - has
    /// its body only in summary. Before V5 that item was findable by title
    /// and nothing else.
    /// </summary>
    [Fact]
    public async Task An_item_with_only_a_summary_is_findable_by_its_summary()
    {
        await AddAsync("a", title: "A headline that says nothing",
            summary: "The piece is actually about kingfishers.");

        var results = await _search.SearchAsync("kingfishers", 50);

        Assert.Single(results);
        Assert.Equal("a", results[0].Item.Guid);
    }

    [Fact]
    public async Task An_author_is_findable_by_name()
    {
        await AddAsync("a", title: "Untitled", author: "Marguerite Yourcenar");
        await AddAsync("b", title: "Something else");

        var results = await _search.SearchAsync("Yourcenar", 50);

        Assert.Single(results);
        Assert.Equal("a", results[0].Item.Guid);
    }

    [Fact]
    public async Task An_edit_to_the_summary_is_tracked_by_the_index()
    {
        var id = await AddAsync("a", title: "Stable title", summary: "about herons");
        Assert.Single(await _search.SearchAsync("herons", 50));

        await _items.UpsertAsync(new FeedItem
        {
            FeedId = _alphaId,
            Guid = "a",
            Title = "Stable title",
            Summary = "about kingfishers",
            FirstSeenUtc = DateTimeOffset.Parse("2026-08-29T10:00:00Z")
        });

        Assert.Empty(await _search.SearchAsync("herons", 50));
        Assert.Single(await _search.SearchAsync("kingfishers", 50));
        Assert.True(id > 0);
    }

    // --- Gap 2: prefix matching, so as-you-type search behaves ---

    [Fact]
    public async Task A_partly_typed_word_matches()
    {
        await AddAsync("a", title: "Compositor internals explained");

        foreach (var typed in new[] { "c", "co", "com", "compos", "composito", "compositor" })
            Assert.True(
                (await _search.SearchAsync(typed, 50)).Count == 1,
                $"typing \"{typed}\" found nothing");
    }

    [Fact]
    public async Task A_finished_word_is_matched_exactly()
    {
        await AddAsync("a", title: "Compositor internals");

        // Trailing space: the user has finished the word, so it is no longer
        // treated as a prefix and a different word starting the same way is
        // not a match.
        Assert.Empty(await _search.SearchAsync("compos ", 50));
        Assert.Single(await _search.SearchAsync("compositor ", 50));
    }

    // --- Gap 3: column weighting ---

    [Fact]
    public async Task A_title_match_outranks_a_body_match()
    {
        await AddAsync("body", title: "An unrelated headline",
            content: "Halfway down the article it mentions kingfishers once.");
        await AddAsync("title", title: "Kingfishers of the lower Wye",
            content: "Nothing else relevant in this body at all.");

        var results = await _search.SearchAsync("kingfishers", 50);

        Assert.Equal(2, results.Count);
        Assert.Equal("title", results[0].Item.Guid);
    }

    [Fact]
    public async Task A_summary_match_outranks_a_body_match()
    {
        await AddAsync("body", title: "One headline",
            content: "Somewhere in here the word kingfishers appears.");
        await AddAsync("summary", title: "Another headline",
            summary: "A piece about kingfishers.",
            content: "A long body with nothing else of interest.");

        var results = await _search.SearchAsync("kingfishers", 50);

        Assert.Equal(2, results.Count);
        Assert.Equal("summary", results[0].Item.Guid);
    }

    // --- Gap 4: snippets that show why a result matched ---

    [Fact]
    public async Task A_result_carries_the_passage_that_matched()
    {
        await AddAsync("a", title: "An entirely unremarkable headline",
            content: string.Join(" ", Enumerable.Repeat("filler words to push the match well past any preview.", 40))
                     + " And here at the end sit the kingfishers.");

        var hit = Assert.Single(await _search.SearchAsync("kingfishers", 50));

        Assert.Contains("kingfishers", hit.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SearchHit.MatchStart, hit.Snippet);
        Assert.Contains(SearchHit.MatchEnd, hit.Snippet);

        // The passage comes from the end of the body, so it is not the same
        // text a plain preview of this article would show.
        Assert.DoesNotContain("An entirely unremarkable headline", hit.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_title_hit_snippets_the_title()
    {
        await AddAsync("a", title: "Kingfishers of the lower Wye",
            content: "A body about something completely different.");

        var hit = Assert.Single(await _search.SearchAsync("kingfishers", 50));

        Assert.Contains("lower Wye", hit.Snippet, StringComparison.Ordinal);
    }

    // --- Gap 6: search respects the filter and can be scoped ---

    [Fact]
    public async Task The_unread_filter_applies_to_search()
    {
        var read = await AddAsync("read", title: "Kingfishers one");
        await AddAsync("unread", title: "Kingfishers two");
        await _items.SetReadAsync(read, true);

        var all = await _search.SearchAsync(
            new SearchQuery("kingfishers", null, null, ItemFilter.All, 50));
        var unread = await _search.SearchAsync(
            new SearchQuery("kingfishers", null, null, ItemFilter.Unread, 50));

        Assert.Equal(2, all.Count);
        Assert.Equal("unread", Assert.Single(unread).Item.Guid);
    }

    [Fact]
    public async Task The_starred_filter_applies_to_search()
    {
        await AddAsync("plain", title: "Kingfishers one");
        var starred = await AddAsync("starred", title: "Kingfishers two");
        await _items.SetStarredAsync(starred, true);

        var results = await _search.SearchAsync(
            new SearchQuery("kingfishers", null, null, ItemFilter.Starred, 50));

        Assert.Equal("starred", Assert.Single(results).Item.Guid);
    }

    [Fact]
    public async Task A_search_can_be_scoped_to_one_feed()
    {
        await AddAsync("alpha", title: "Kingfishers in alpha");
        await AddAsync("beta", title: "Kingfishers in beta", feedId: _betaId);

        var scoped = await _search.SearchAsync(
            new SearchQuery("kingfishers", _betaId, null, ItemFilter.All, 50));

        Assert.Equal("beta", Assert.Single(scoped).Item.Guid);
    }

    [Fact]
    public async Task A_search_can_be_scoped_to_one_folder()
    {
        await AddAsync("alpha", title: "Kingfishers in alpha");
        await AddAsync("beta", title: "Kingfishers in beta", feedId: _betaId);

        var scoped = await _search.SearchAsync(
            new SearchQuery("kingfishers", null, _folderId, ItemFilter.All, 50));

        // Only the alpha feed is in the folder.
        Assert.Equal("alpha", Assert.Single(scoped).Item.Guid);
    }

    [Fact]
    public async Task An_unscoped_search_spans_every_feed()
    {
        await AddAsync("alpha", title: "Kingfishers in alpha");
        await AddAsync("beta", title: "Kingfishers in beta", feedId: _betaId);

        Assert.Equal(2, (await _search.SearchAsync("kingfishers", 50)).Count);
    }

    // --- Safety, against a real database rather than a string ---

    /// <summary>
    /// The same shapes FtsQueryBuilderTests checks as strings, run through
    /// SQLite. A syntax error in an FTS5 MATCH expression is an exception, not
    /// an empty result, and search runs on every keystroke, so any of these
    /// throwing would take the window down mid-type.
    /// </summary>
    [Theory]
    [InlineData("\"")]
    [InlineData("\"unbalanced quote AND (")]
    [InlineData("(((")]
    [InlineData("*")]
    [InlineData("^")]
    [InlineData(":")]
    [InlineData("NEAR")]
    [InlineData("NEAR(a b)")]
    [InlineData("a OR b")]
    [InlineData("a AND b")]
    [InlineData("NOT")]
    [InlineData("title:kingfishers")]
    [InlineData("kingfishers*")]
    [InlineData("-- ; DROP TABLE items;")]
    [InlineData("   ")]
    [InlineData("!@#$%^&*()")]
    public async Task No_query_a_user_can_type_throws(string query)
    {
        await AddAsync("a", title: "Kingfishers of the lower Wye",
            summary: "A summary", author: "An Author", content: "A body.");

        var results = await _search.SearchAsync(query, 50);

        // Some of these legitimately match ("a OR b" searches for the word
        // or), some match nothing. Neither is a failure; throwing is.
        Assert.NotNull(results);
    }

    [Fact]
    public async Task An_empty_query_returns_nothing_rather_than_everything()
    {
        await AddAsync("a", title: "Kingfishers");

        Assert.Empty(await _search.SearchAsync("   ", 50));
        Assert.Empty(await _search.SearchAsync("!!!", 50));
    }
}
