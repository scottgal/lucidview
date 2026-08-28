namespace LucidReader.Core.Sync;

public readonly record struct FeedRefreshRequest(long FeedId, bool IsManual);

public readonly record struct FeedRefreshOutcome(
    long FeedId,
    bool Success,
    int NewItemCount,
    bool NotModified,
    string? Error);
