using System.Net;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

public class FeedlyFeedSearchTests
{
    private static string SearchFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Search", name));

    private static ReaderSettings Enabled() =>
        ReaderSettings.Defaults with { EnableOnlineFeedSearch = true };

    private static ReaderSettings Disabled() =>
        ReaderSettings.Defaults with { EnableOnlineFeedSearch = false };

    [Fact]
    public async Task Results_map_correctly_and_strip_the_feed_prefix_from_feedId()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK,
            SearchFixture("feedly-search-dotnet.json"),
            mediaType: "application/json");
        var search = new FeedlyFeedSearch(handler.CreateClient(), Enabled);

        var found = await search.SearchAsync("dotnet", 3);

        Assert.Equal(3, found.Count);

        var first = found[0];
        Assert.Equal("http://dotnet.developpez.com/index/rss", first.FeedUrl);
        Assert.Equal("Flux .NET Developpez", first.Title);
        Assert.Equal("https://dotnet.developpez.com/index/rss", first.SiteUrl);
        Assert.Equal(
            "http://storage.googleapis.com/site-assets/tkwINXmVi_vDMrTrLNuUMXCglgosXSEonsZ2TxRSttY_icon-1543e53eb16",
            first.IconUrl);
        Assert.Contains("Club des", first.Description);
        Assert.Equal(993, first.Subscribers);
    }

    [Fact]
    public async Task The_setting_being_off_returns_empty_and_makes_no_request_at_all()
    {
        // This is the test that matters most: it is the difference between
        // an opt-in and a claim of one. Asserting handler.Requests is empty
        // proves the gate is checked BEFORE anything is sent, not merely
        // before the result is handed back.
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK,
            SearchFixture("feedly-search-dotnet.json"),
            mediaType: "application/json");
        var search = new FeedlyFeedSearch(handler.CreateClient(), Disabled);

        var found = await search.SearchAsync("dotnet", 3);

        Assert.Empty(found);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_non_success_status_returns_empty_rather_than_throwing()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.InternalServerError);
        var search = new FeedlyFeedSearch(handler.CreateClient(), Enabled);

        Assert.Empty(await search.SearchAsync("dotnet", 3));
    }

    [Fact]
    public async Task Malformed_json_returns_empty_rather_than_throwing()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "{not valid json", mediaType: "application/json");
        var search = new FeedlyFeedSearch(handler.CreateClient(), Enabled);

        Assert.Empty(await search.SearchAsync("dotnet", 3));
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var handler = StubHttpHandler.Blocking();
        var search = new FeedlyFeedSearch(handler.CreateClient(), Enabled);

        using var cts = new CancellationTokenSource();
        var task = search.SearchAsync("dotnet", 3, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
    }

    [Fact]
    public async Task A_blank_query_returns_empty_without_a_request()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK,
            SearchFixture("feedly-search-dotnet.json"),
            mediaType: "application/json");
        var search = new FeedlyFeedSearch(handler.CreateClient(), Enabled);

        var found = await search.SearchAsync("   ", 3);

        Assert.Empty(found);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_result_whose_feedId_lacks_the_feed_prefix_is_skipped()
    {
        const string body = """
            {"results":[
              {"feedId":"http://no-prefix.example.com/rss","title":"No Prefix"},
              {"feedId":"feed/https://ok.example.com/rss","title":"OK"}
            ]}
            """;
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, body, mediaType: "application/json");
        var search = new FeedlyFeedSearch(handler.CreateClient(), Enabled);

        var found = await search.SearchAsync("dotnet", 2);

        var one = Assert.Single(found);
        Assert.Equal("https://ok.example.com/rss", one.FeedUrl);
    }

    [Fact]
    public async Task A_missing_subscribers_field_yields_zero_not_an_exception()
    {
        const string body = """
            {"results":[{"feedId":"feed/https://ok.example.com/rss","title":"OK"}]}
            """;
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, body, mediaType: "application/json");
        var search = new FeedlyFeedSearch(handler.CreateClient(), Enabled);

        var one = Assert.Single(await search.SearchAsync("dotnet", 1));
        Assert.Equal(0, one.Subscribers);
        Assert.Null(one.Description);
    }

    [Fact]
    public async Task A_result_missing_title_website_and_iconUrl_yields_nulls_not_an_exception()
    {
        const string body = """
            {"results":[{"feedId":"feed/https://ok.example.com/rss","subscribers":42,"description":"d"}]}
            """;
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, body, mediaType: "application/json");
        var search = new FeedlyFeedSearch(handler.CreateClient(), Enabled);

        var one = Assert.Single(await search.SearchAsync("dotnet", 1));
        Assert.Equal("https://ok.example.com/rss", one.FeedUrl);
        Assert.Null(one.Title);
        Assert.Null(one.SiteUrl);
        Assert.Null(one.IconUrl);
        Assert.Equal(42, one.Subscribers);
        Assert.Equal("d", one.Description);
    }

    // --- Query encoding ---
    //
    // Uri.EscapeDataString percent-encodes &, # and other characters that
    // would otherwise be interpreted as query-string structure. Nothing
    // exercised this before: a regression to naive string interpolation
    // would pass every other test while letting a search term smuggle an
    // extra query parameter into the request.

    [Fact]
    public async Task A_query_containing_ampersand_and_hash_is_percent_encoded_with_no_injected_parameter()
    {
        const string body = """{"results":[]}""";
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, body, mediaType: "application/json");
        var search = new FeedlyFeedSearch(handler.CreateClient(), Enabled);

        const string query = "rust&evil=1#frag";
        await search.SearchAsync(query, 3);

        var sentUri = Assert.Single(handler.Requests).RequestUri!;
        var rawQuery = sentUri.Query.TrimStart('?');
        var parameters = rawQuery.Split('&');

        // Exactly two parameters were sent - query and count - proving the
        // & and # inside the search term did not get interpreted as
        // query-string structure and inject a third.
        Assert.Equal(2, parameters.Length);
        Assert.StartsWith("query=", parameters[0]);
        Assert.StartsWith("count=", parameters[1]);

        Assert.Contains("%26", sentUri.Query);
        Assert.Contains("%23", sentUri.Query);

        var sentQueryValue = Uri.UnescapeDataString(parameters[0]["query=".Length..]);
        Assert.Equal(query, sentQueryValue);
    }

    // --- JSON shape tolerance ---
    //
    // Every case below must return empty rather than throw. Some do so by
    // skipping one bad entry and keeping the rest; others - a null entry, or
    // a body whose root is not an object - hit the broad catch in
    // SearchAsync and return an empty list for the whole call, which still
    // satisfies "never throws" even though it does not salvage a sibling
    // valid entry in the same response.

    [Fact]
    public async Task A_null_entry_in_results_returns_empty_rather_than_throwing()
    {
        const string body = """
            {"results":[null,{"feedId":"feed/https://ok.example.com/rss","title":"OK"}]}
            """;
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, body, mediaType: "application/json");
        var search = new FeedlyFeedSearch(handler.CreateClient(), Enabled);

        Assert.Empty(await search.SearchAsync("dotnet", 2));
    }

    [Fact]
    public async Task A_body_with_no_results_key_returns_empty_rather_than_throwing()
    {
        const string body = """{"success":true}""";
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, body, mediaType: "application/json");
        var search = new FeedlyFeedSearch(handler.CreateClient(), Enabled);

        Assert.Empty(await search.SearchAsync("dotnet", 3));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    public async Task A_valid_but_non_object_top_level_body_returns_empty_rather_than_throwing(string body)
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, body, mediaType: "application/json");
        var search = new FeedlyFeedSearch(handler.CreateClient(), Enabled);

        Assert.Empty(await search.SearchAsync("dotnet", 3));
    }
}
