using LucidReader.Core.Model;

namespace LucidReader.Views;

/// <summary>
/// The list-row projection of a FeedItem. Minimal for Task 1: the item list,
/// selection handling and reading pane are built out in later tasks. This
/// exists now so MainWindow.Items.cs and MainWindow.Reading.cs have a shared
/// type to compile against.
/// </summary>
public sealed class ItemRow
{
    public required long Id { get; init; }
    public required long FeedId { get; init; }
    public string? Title { get; init; }
    public bool IsRead { get; init; }
    public bool IsStarred { get; init; }
    public DateTimeOffset? PublishedUtc { get; init; }

    public static ItemRow FromFeedItem(FeedItem item) => new()
    {
        Id = item.Id,
        FeedId = item.FeedId,
        Title = item.Title,
        IsRead = item.IsRead,
        IsStarred = item.IsStarred,
        PublishedUtc = item.PublishedUtc
    };
}
