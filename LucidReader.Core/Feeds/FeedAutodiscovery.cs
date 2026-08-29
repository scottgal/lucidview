using System.Text;
using System.Text.RegularExpressions;

namespace LucidReader.Core.Feeds;

public readonly record struct DiscoveredFeed(string FeedUrl, string? Title, string? IconUrl);

/// <summary>
/// Turns whatever the user pasted into feed URLs worth subscribing to.
///
/// Uses a regex over the head rather than a full HTML parser on purpose: the
/// only thing being read is link elements, and pulling AngleSharp into this
/// path to do it would be a heavier dependency than the job deserves. A missed
/// link costs the user one manual paste of the feed URL.
/// </summary>
public sealed partial class FeedAutodiscovery(HttpClient http)
{
    private const int MaxDiscoveryBytes = 8 * 1024 * 1024;
    private const int ReadBufferSize = 8192;

    private static readonly string[] FeedMediaTypes =
    [
        "application/rss+xml",
        "application/atom+xml",
        "application/rdf+xml",
        "text/xml",
        "application/xml"
    ];

    public async Task<IReadOnlyList<DiscoveredFeed>> DiscoverAsync(
        string inputUrl,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(inputUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Array.Empty<DiscoveredFeed>();

        string body;
        Uri effectiveUri;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", FeedFetcher.UserAgentString);
            request.Headers.TryAddWithoutValidation(
                "Accept",
                "application/atom+xml, application/rss+xml, text/html;q=0.9, */*;q=0.8");

            // ResponseHeadersRead, not the default ResponseContentRead: the
            // latter buffers the whole body before this method gets a chance
            // to look at it, which is the same unbounded-buffering gap
            // ArticleFetcher and FeedFetcher had to close. This class is the
            // most exposed of the three - it fires straight off a string the
            // user pasted, with no prior evidence the target is even
            // feed-shaped - so it needs the same bound.
            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return Array.Empty<DiscoveredFeed>();

            // Redirects (bare domain to www, http upgraded to https, etc.)
            // mean the URL we end up reading is not always the one we asked
            // for. Relative hrefs in the page, and the feed's own URL when
            // the page IS the feed, must resolve against wherever the
            // response actually came from, or a relative link resolves
            // against the wrong host and produces a subscription that 404s.
            // A redirect to a non-http(s) scheme is not re-checked here: the
            // BCL's handler pipeline refuses to follow one before this code
            // ever sees a response, so that protection is implicit rather
            // than something this method needs to duplicate.
            effectiveUri = response.RequestMessage?.RequestUri ?? uri;

            // Cheap early rejection when the server declares its length up
            // front. Most HTML is served chunked, which never sets
            // Content-Length, so this alone is not the real cap - see
            // ReadBoundedAsync for the one that actually is.
            if (response.Content.Headers.ContentLength > MaxDiscoveryBytes)
                return Array.Empty<DiscoveredFeed>();

            var bounded = await ReadBoundedAsync(response.Content, ct);
            if (bounded is null) return Array.Empty<DiscoveredFeed>();
            body = bounded;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Array.Empty<DiscoveredFeed>();
        }

        // Already a feed? Then there is nothing to discover. There is also no
        // page here for SiteMetadataExtractor to read, so the icon is a bare
        // /favicon.ico guess at the feed URL's own host - a URL to record,
        // not a fetch to perform; whether it actually exists is the
        // rendering layer's problem.
        var parser = new FeedParser();
        if (parser.CanParse(body))
        {
            string? title = null;
            try { title = parser.Parse(body, effectiveUri).Title; }
            catch (FeedParseException) { }
            return [new DiscoveredFeed(effectiveUri.ToString(), title, FaviconGuess(effectiveUri))];
        }

        // The page body is already in hand for feed-link discovery, so the
        // same body is read again in memory (no second fetch) for the site
        // icon: FeedAutodiscovery downloads this page anyway, and the
        // favicon link is in the same <head>.
        var siteMetadata = SiteMetadataExtractor.Extract(body, effectiveUri);
        var siteIcon = siteMetadata.IconUrl ?? FaviconGuess(effectiveUri);

        var found = new List<DiscoveredFeed>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in LinkTagPattern().Matches(body))
        {
            var tag = match.Value;

            if (!HasAlternateToken(AttributeValue(tag, "rel")))
                continue;

            var type = AttributeValue(tag, "type");
            if (!FeedMediaTypes.Any(t => type.Contains(t, StringComparison.OrdinalIgnoreCase)))
                continue;

            var href = AttributeValue(tag, "href");
            if (href.Length == 0) continue;
            if (!Uri.TryCreate(effectiveUri, href, out var absolute)) continue;
            if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps) continue;
            if (!seen.Add(absolute.ToString())) continue;

            var linkTitle = AttributeValue(tag, "title");
            found.Add(new DiscoveredFeed(
                absolute.ToString(),
                linkTitle.Length > 0 ? linkTitle : null,
                siteIcon));
        }

        return found;
    }

    private static string FaviconGuess(Uri uri) => $"{uri.Scheme}://{uri.Authority}/favicon.ico";

    /// <summary>
    /// Reads the body a chunk at a time and abandons the read the moment the
    /// total exceeds MaxDiscoveryBytes, rather than calling
    /// HttpContent.ReadAsStringAsync and trusting Content-Length: a chunked
    /// response never sets that header, so the fast-path check above is
    /// skipped entirely and an unbounded read would buffer whatever the
    /// server sends. Mirrors ArticleFetcher.ReadBoundedAsync.
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
            if (buffer.Length > MaxDiscoveryBytes) return null;
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

    /// <summary>
    /// "alternate" must be a whitespace-separated token in rel, not merely a
    /// substring: rel is a space-separated list per the HTML spec (a link can
    /// legitimately be rel="alternate home"), and a substring check would
    /// also false-positive on any token that happens to contain the word.
    /// </summary>
    private static bool HasAlternateToken(string rel) =>
        HtmlAttributeParsing.HasToken(rel, "alternate");

    private static string AttributeValue(string tag, string attribute) =>
        HtmlAttributeParsing.AttributeValue(tag, attribute);

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTagPattern();
}
