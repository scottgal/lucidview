using System.Text;
using System.Text.RegularExpressions;
using LucidReader.Core.Feeds;

namespace LucidReader.Core.Offline;

/// <summary>
/// What an article body turned out to be, so the caller knows whether it
/// still has to be converted.
/// </summary>
public enum ArticleBodyKind
{
    Html,
    Markdown
}

/// <summary>
/// An article body plus what kind of body it is. Markdown that came straight
/// from the site is the author's own source text, so running it through the
/// HTML converter would only degrade it.
/// </summary>
public sealed record FetchedArticle(string Body, ArticleBodyKind Kind);

/// <summary>
/// Fetches an article page. Returns null rather than throwing on any failure:
/// a page we cannot get is a normal outcome, and the caller already has the
/// feed summary to fall back on.
///
/// A site that publishes its own markdown is asked for it, in the two ways
/// that work without knowing anything about the site: an Accept header that
/// lists text/markdown below HTML, and a link element advertising a markdown
/// alternate. Either one yields the author's source text instead of a
/// round trip through rendered HTML.
/// </summary>
public sealed partial class ArticleFetcher(HttpClient http)
{
    private const int MaxArticleBytes = 8 * 1024 * 1024;
    private const int ReadBufferSize = 8192;

    /// <summary>
    /// text/markdown is offered below HTML on purpose. HTML stays the default
    /// for every site that does not publish markdown, and content negotiation
    /// gives us the better source only where one exists.
    /// </summary>
    private const string AcceptHeader =
        "text/html,application/xhtml+xml;q=0.9,text/markdown;q=0.8,*/*;q=0.7";

    private static readonly string[] MarkdownMediaTypes =
    [
        "text/markdown",
        "text/x-markdown"
    ];

    public async Task<FetchedArticle?> FetchArticleAsync(string url, CancellationToken ct = default)
    {
        // The URL is item.Link, straight out of feed XML, and auto-download
        // fetches it with no user action at all, so it needs the same gate as
        // the markdown alternate below rather than a scheme check on its own:
        // a feed publishing <link>http://127.0.0.1:9200/_cluster/settings</link>
        // would otherwise have that GET made for it and the response stored as
        // the article body.
        if (!FeedUrlPolicy.TryValidate(url, out var uri, out _) || uri is null)
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", FeedFetcher.UserAgentString);
            request.Headers.TryAddWithoutValidation("Accept", AcceptHeader);

            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode) return null;

            // Whatever comes back here is stored as the article and marks the
            // item Downloaded. There is no second "does this look like an
            // article" check downstream, so a login wall, a captcha page or a
            // CSV export must be turned away here rather than silently stored
            // as the article.
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var isMarkdown = IsMarkdown(mediaType);

            if (!isMarkdown
                && mediaType is not null
                && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
                && !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                return null;

            // Cheap early rejection when the server declares its length up
            // front. Most dynamically generated HTML is served chunked,
            // which never sets Content-Length, so this alone is not the real
            // cap - see ReadBoundedAsync for the one that actually is.
            if (response.Content.Headers.ContentLength > MaxArticleBytes) return null;

            var body = await ReadBoundedAsync(response.Content, ct);
            if (body is null) return null;

            // The site answered our Accept header with its own source text.
            // Nothing left to convert.
            if (isMarkdown) return new FetchedArticle(body, ArticleBodyKind.Markdown);

            // Redirects mean a relative markdown href has to resolve against
            // wherever the response actually came from, not the address we
            // asked for.
            var effectiveUri = response.RequestMessage?.RequestUri ?? uri;
            var markdown = await FollowMarkdownAlternateAsync(body, effectiveUri, ct);
            if (markdown is not null) return new FetchedArticle(markdown, ArticleBodyKind.Markdown);

            return new FetchedArticle(body, ArticleBodyKind.Html);
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
    /// Follows a &lt;link rel="alternate" type="text/markdown"&gt; when the
    /// page declares one, and returns its body. Null means there was nothing
    /// to follow or following it did not work, in which case the caller keeps
    /// the HTML it already has.
    ///
    /// The href came out of a downloaded document, so it goes through
    /// FeedUrlPolicy for the same reason every other document-supplied
    /// address in this app does: without it, any page we download could aim
    /// an unattended request at loopback or at a private network address.
    /// </summary>
    private async Task<string?> FollowMarkdownAlternateAsync(
        string html, Uri baseUri, CancellationToken ct)
    {
        var href = MarkdownAlternateHref(html);
        if (href is null) return null;
        if (!Uri.TryCreate(baseUri, href, out var absolute)) return null;
        if (!FeedUrlPolicy.IsAllowed(absolute.ToString())) return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, absolute);
            request.Headers.TryAddWithoutValidation("User-Agent", FeedFetcher.UserAgentString);
            request.Headers.TryAddWithoutValidation("Accept", "text/markdown,text/plain;q=0.9");

            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;

            // A site that advertises a markdown alternate and then answers
            // with HTML has sent us the thing we were trying to avoid, so the
            // page we already have is no worse and the extra body is dropped.
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null
                && !IsMarkdown(mediaType)
                && !mediaType.Equals("text/plain", StringComparison.OrdinalIgnoreCase))
                return null;

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

    private static string? MarkdownAlternateHref(string html)
    {
        foreach (Match match in LinkTagPattern().Matches(html))
        {
            var tag = match.Value;
            if (!HtmlAttributeParsing.HasToken(
                    HtmlAttributeParsing.AttributeValue(tag, "rel"), "alternate"))
                continue;

            var type = HtmlAttributeParsing.AttributeValue(tag, "type");
            if (!IsMarkdown(type)) continue;

            var href = HtmlAttributeParsing.AttributeValue(tag, "href");
            if (href.Length > 0) return href;
        }

        return null;
    }

    private static bool IsMarkdown(string? mediaType) =>
        mediaType is not null
        && MarkdownMediaTypes.Any(t => mediaType.Contains(t, StringComparison.OrdinalIgnoreCase));

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

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTagPattern();
}
