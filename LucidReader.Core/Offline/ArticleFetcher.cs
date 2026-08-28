using System.Text;
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
    private const int ReadBufferSize = 8192;

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

            // Whatever comes back here is handed straight to the markdown
            // converter and, on success, overwrites the item's content and
            // marks it Downloaded. There is no second "does this look like
            // an article" check downstream, so a login wall, a captcha page
            // or a CSV export must be turned away here rather than silently
            // stored as the article.
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null
                && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
                && !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                return null;

            // Cheap early rejection when the server declares its length up
            // front. Most dynamically generated HTML is served chunked,
            // which never sets Content-Length, so this alone is not the real
            // cap - see ReadBoundedAsync for the one that actually is.
            if (response.Content.Headers.ContentLength > MaxArticleBytes) return null;

            return await ReadBoundedAsync(response.Content, ct);
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

    /// <summary>
    /// Reads the body a chunk at a time and abandons the read the moment the
    /// total exceeds MaxArticleBytes, rather than calling
    /// HttpContent.ReadAsStringAsync and trusting Content-Length: a chunked
    /// response - the common case for dynamically generated HTML - never
    /// sets that header, so ContentLength is null, the fast-path check above
    /// is skipped entirely, and ReadAsStringAsync would buffer the whole
    /// body with no bound at all.
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
            if (buffer.Length > MaxArticleBytes) return null;
        }

        var encoding = GetEncoding(content.Headers.ContentType?.CharSet) ?? Encoding.UTF8;
        return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static Encoding? GetEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return null;
        try
        {
            return Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
