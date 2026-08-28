using LucidReader.Core.Feeds;

namespace LucidReader.Core.Offline;

/// <summary>
/// Fetches an article page as HTML. Returns null rather than throwing on any
/// failure: a page we cannot get is a normal outcome, and the caller already
/// has the feed summary to fall back on.
/// </summary>
public sealed class ArticleFetcher(HttpClient http)
{
    private const int MaxArticleBytes = 8 * 1024 * 1024;

    public async Task<string?> FetchHtmlAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", FeedFetcher.UserAgentString);
            request.Headers.TryAddWithoutValidation(
                "Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");

            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode) return null;

            // Reject only content-types that plainly cannot be a page (an
            // image, a PDF, a download). Many servers - and, for that matter,
            // this codebase's own test stub when no explicit header is set -
            // omit the header or send a generic "text/plain", and text with
            // no declared type is still worth trying to convert.
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null
                && !mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
                && !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                return null;

            if (response.Content.Headers.ContentLength > MaxArticleBytes) return null;

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
