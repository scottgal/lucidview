using System.Text;
using System.Text.Json;
using LucidReader.Core.Model;

namespace LucidReader.Core.Feeds;

/// <summary>
/// Topic search against Feedly's keyless public search endpoint. This is the
/// only feature in the reader that sends the user's own input (the search
/// query) to a third party, so <see cref="ReaderSettings.EnableOnlineFeedSearch"/>
/// is checked BEFORE anything is built or sent, not merely before the result
/// is returned: when the setting is off, this class must not construct a
/// request at all, let alone send one.
/// </summary>
public sealed class FeedlyFeedSearch(HttpClient http, Func<ReaderSettings> settings) : IFeedSearch
{
    private const string SearchEndpoint = "https://cloud.feedly.com/v3/search/feeds";
    private const string FeedIdPrefix = "feed/";
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private const int ReadBufferSize = 8192;

    public async Task<IReadOnlyList<FeedSearchResult>> SearchAsync(
        string query, int limit, CancellationToken ct = default)
    {
        if (!settings().EnableOnlineFeedSearch) return [];
        if (string.IsNullOrWhiteSpace(query)) return [];

        var effectiveLimit = Math.Max(1, limit);
        var uri = new Uri(
            $"{SearchEndpoint}?query={Uri.EscapeDataString(query)}&count={effectiveLimit}");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", FeedFetcher.UserAgentString);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            // ResponseHeadersRead, then a bounded streamed read: the same
            // shape FeedFetcher and FeedAutodiscovery use, so a search index
            // response cannot buffer arbitrarily large content in memory.
            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return [];

            if (response.Content.Headers.ContentLength > MaxResponseBytes) return [];

            var body = await ReadBoundedAsync(response.Content, ct);
            if (body is null) return [];

            return Parse(body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Tolerant on purpose: a missing or unexpected field yields a null or
    /// default rather than throwing, and a result whose feedId is missing,
    /// unprefixed, or does not resolve to a usable http(s) URL is skipped
    /// rather than producing a broken subscription.
    /// </summary>
    private static IReadOnlyList<FeedSearchResult> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
            return [];

        var found = new List<FeedSearchResult>();
        foreach (var entry in results.EnumerateArray())
        {
            var feedId = GetString(entry, "feedId");
            if (feedId is null || !feedId.StartsWith(FeedIdPrefix, StringComparison.Ordinal))
                continue;

            var feedUrl = feedId[FeedIdPrefix.Length..];
            if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                continue;

            found.Add(new FeedSearchResult(
                feedUrl,
                GetString(entry, "title"),
                GetString(entry, "website"),
                GetString(entry, "iconUrl"),
                GetString(entry, "description"),
                GetInt(entry, "subscribers")));
        }

        return found;
    }

    private static string? GetString(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : 0;

    /// <summary>
    /// Mirrors FeedFetcher.ReadBoundedAsync and FeedAutodiscovery.ReadBoundedAsync:
    /// reads a chunk at a time and abandons the read once the total exceeds
    /// MaxResponseBytes, since a chunked response never sets Content-Length
    /// and would otherwise be buffered without limit.
    /// </summary>
    private static async Task<string?> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[ReadBufferSize];

        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxResponseBytes) return null;
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
