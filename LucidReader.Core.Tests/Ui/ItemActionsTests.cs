using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Views;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class ItemActionsTests
{
    // read states of a five-item list: unread, read, unread, read, unread
    private static readonly bool[] Read = [false, true, false, true, false];

    private static int Next(int current, bool forward, bool unreadOnly) =>
        MainWindow.FindNextIndexIn(Read, current, forward, unreadOnly);

    [Fact]
    public void Next_moves_one_forward()
    {
        Assert.Equal(1, Next(0, forward: true, unreadOnly: false));
    }

    [Fact]
    public void Previous_moves_one_back()
    {
        Assert.Equal(1, Next(2, forward: false, unreadOnly: false));
    }

    [Fact]
    public void Next_unread_skips_read_items()
    {
        Assert.Equal(2, Next(0, forward: true, unreadOnly: true));
    }

    [Fact]
    public void Previous_unread_skips_read_items()
    {
        Assert.Equal(2, Next(4, forward: false, unreadOnly: true));
    }

    [Fact]
    public void Next_at_the_end_stays_put_rather_than_wrapping()
    {
        Assert.Equal(4, Next(4, forward: true, unreadOnly: false));
    }

    [Fact]
    public void Previous_at_the_start_stays_put_rather_than_wrapping()
    {
        Assert.Equal(0, Next(0, forward: false, unreadOnly: false));
    }

    [Fact]
    public void Next_unread_with_nothing_unread_ahead_stays_put()
    {
        bool[] allReadAhead = [false, true, true];

        Assert.Equal(0, MainWindow.FindNextIndexIn(allReadAhead, 0, true, true));
    }

    [Fact]
    public void Navigation_from_no_selection_starts_at_the_first_item()
    {
        Assert.Equal(0, Next(-1, forward: true, unreadOnly: false));
    }

    [Fact]
    public void Navigation_in_an_empty_list_returns_no_selection()
    {
        Assert.Equal(-1, MainWindow.FindNextIndexIn([], -1, true, false));
    }

    [Fact]
    public async Task Tags_round_trip_through_TagRepository()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mylo-tags-{Guid.NewGuid():N}.db");
        try
        {
            // Scoped, not `await using var`: a method-scoped declaration disposes
            // after the finally below, so the delete ran against a database that
            // was still open. Windows refuses that; Unix allows it, which is why
            // this only ever failed on Windows CI.
            await using var db = await ReaderDatabase.OpenAsync(dbPath);

            var feeds = new FeedRepository(db);
            var items = new ItemRepository(db);
            var tags = new TagRepository(db);

            var feedId = await feeds.AddAsync(new Feed { FeedUrl = "https://example.test/feed.xml" });
            var itemId = await items.UpsertAsync(new FeedItem
            {
                FeedId = feedId,
                Guid = "item-1",
                Title = "Test article"
            });

            await tags.AddToItemAsync(itemId, "dotnet");
            await tags.AddToItemAsync(itemId, "reading");

            var afterAdd = await tags.GetForItemAsync(itemId);
            Assert.Equal(["dotnet", "reading"], afterAdd.OrderBy(t => t, StringComparer.OrdinalIgnoreCase));

            await tags.RemoveFromItemAsync(itemId, "dotnet");

            var afterRemove = await tags.GetForItemAsync(itemId);
            Assert.Equal(["reading"], afterRemove);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
