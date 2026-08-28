namespace LucidReader.Core.Feeds;

/// <summary>
/// SkippedItemCount records items the parser could not read at all. Partial
/// success is the normal case with real feeds: eighteen good items out of
/// twenty is a successful fetch, not a failure, but we surface the two.
/// </summary>
public sealed record ParsedFeed(
    string? Title,
    string? SiteUrl,
    IReadOnlyList<ParsedItem> Items,
    int SkippedItemCount);
