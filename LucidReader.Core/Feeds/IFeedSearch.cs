namespace LucidReader.Core.Feeds;

public readonly record struct FeedSearchResult(
    string FeedUrl,
    string? Title,
    string? SiteUrl,
    string? IconUrl,
    string? Description,
    int Subscribers);

/// <summary>
/// Looks up candidate feeds for a topic query against a third-party search
/// index. This is the only feature in the reader that sends anything the
/// user typed to a third party, so every implementation must treat
/// <see cref="Model.ReaderSettings.EnableOnlineFeedSearch"/> as a hard gate:
/// when it is off, no request may leave the machine.
/// </summary>
public interface IFeedSearch
{
    Task<IReadOnlyList<FeedSearchResult>> SearchAsync(
        string query, int limit, CancellationToken ct = default);
}
