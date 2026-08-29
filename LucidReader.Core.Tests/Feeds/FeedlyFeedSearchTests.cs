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
}
