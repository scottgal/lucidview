namespace LucidReader.Core.Feeds;

public abstract record FeedFetchResult
{
    private FeedFetchResult() { }

    public sealed record Fetched(string Content, string? ETag, string? LastModified)
        : FeedFetchResult;

    public sealed record NotModified : FeedFetchResult;

    /// <summary>
    /// IsTransient separates "try again later" (timeouts, 5xx, 429) from
    /// "this feed is broken" (404, 410, 401, 403). Only the latter should
    /// push a feed toward being auto-paused.
    /// </summary>
    public sealed record Failed(string Error, bool IsTransient) : FeedFetchResult;
}
