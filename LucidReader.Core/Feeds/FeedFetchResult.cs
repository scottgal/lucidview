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
    ///
    /// RetryAfter is classification only for now - populated from a 429 (or
    /// any other) response's Retry-After header when present, but nothing
    /// currently honours it; BackoffPolicy computes its own curve regardless.
    /// Added now, while FeedFetchResult has no other consumer, so Plan 2 can
    /// wire it up without a breaking change to this type. Only the header's
    /// delta-seconds form is captured (Retry-After: 120); the http-date form
    /// (Retry-After: Wed, 21 Oct 2026 07:28:00 GMT) is left null rather than
    /// resolving it against DateTime.Now/UtcNow, which this codebase avoids in
    /// favour of an injected TimeProvider that FeedFetcher does not currently
    /// receive.
    /// </summary>
    public sealed record Failed(string Error, bool IsTransient, TimeSpan? RetryAfter = null)
        : FeedFetchResult;
}
