using System.Text.RegularExpressions;

namespace LucidReader.Core.Feeds;

public readonly record struct DiscoveredFeed(string FeedUrl, string? Title);

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
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", FeedFetcher.UserAgentString);
            request.Headers.TryAddWithoutValidation(
                "Accept",
                "application/atom+xml, application/rss+xml, text/html;q=0.9, */*;q=0.8");

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return Array.Empty<DiscoveredFeed>();

            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Array.Empty<DiscoveredFeed>();
        }

        // Already a feed? Then there is nothing to discover.
        var parser = new FeedParser();
        if (parser.CanParse(body))
        {
            string? title = null;
            try { title = parser.Parse(body, uri).Title; }
            catch (FeedParseException) { }
            return [new DiscoveredFeed(uri.ToString(), title)];
        }

        var found = new List<DiscoveredFeed>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in LinkTagPattern().Matches(body))
        {
            var tag = match.Value;

            if (!AttributeValue(tag, "rel").Contains("alternate", StringComparison.OrdinalIgnoreCase))
                continue;

            var type = AttributeValue(tag, "type");
            if (!FeedMediaTypes.Any(t => type.Contains(t, StringComparison.OrdinalIgnoreCase)))
                continue;

            var href = AttributeValue(tag, "href");
            if (href.Length == 0) continue;
            if (!Uri.TryCreate(uri, href, out var absolute)) continue;
            if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps) continue;
            if (!seen.Add(absolute.ToString())) continue;

            var linkTitle = AttributeValue(tag, "title");
            found.Add(new DiscoveredFeed(
                absolute.ToString(),
                linkTitle.Length > 0 ? linkTitle : null));
        }

        return found;
    }

    private static string AttributeValue(string tag, string attribute)
    {
        var match = Regex.Match(
            tag,
            attribute + @"\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["v"].Value.Trim() : string.Empty;
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTagPattern();
}
