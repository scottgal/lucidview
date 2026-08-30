namespace LucidReader.Core.Feeds;

/// <summary>
/// Turns an article's link into the identity two copies of the same article
/// share, whichever feed they arrived under.
///
/// Why this exists: a site that publishes both an RSS and an Atom feed is
/// publishing the same articles twice. The two documents give the same
/// article different guids (Atom's &lt;id&gt; and RSS's &lt;guid&gt; are not
/// required to agree, and plenty of feeds emit neither), so the
/// (feed_id, guid) key items are stored under cannot tell that the two rows
/// are one article. The link can: both formats point at the same page.
///
/// The normalisation is deliberately conservative. It only removes things
/// that provably do not change which page is being addressed:
///
///   - the scheme and host are lower-cased, because both are
///     case-insensitive per RFC 3986. The path is NOT: plenty of servers
///     serve /About and /about as different pages.
///   - a single trailing slash is dropped, so "/posts/x/" and "/posts/x"
///     agree. The root path "/" keeps its slash, since dropping it would
///     leave a bare authority.
///   - the fragment goes. It is never sent to the server and never selects a
///     different document.
///   - well-known tracking parameters go (see TrackingParameters). Those are
///     added by whatever produced the link, not by the publisher deciding
///     which page this is, and the same article routinely carries different
///     ones in different feeds.
///
/// Everything else is left exactly as it was, including the rest of the query
/// string, which on plenty of sites is the only thing naming the article
/// (?p=1234, ?story_fbid=..., a paging parameter). Stripping more would make
/// two genuinely different articles collide, which is a far worse failure than
/// showing one article twice.
///
/// Returns null when there is no usable link. A null identity means "this row
/// stands alone": callers must never treat two nulls as equal.
/// </summary>
public static class CanonicalArticleId
{
    /// <summary>
    /// Query parameters removed before comparison. Anything beginning "utm_"
    /// is removed by prefix; the rest are matched whole. All matched
    /// case-insensitively, since a link in a feed is written by hand as often
    /// as it is generated.
    /// </summary>
    private static readonly string[] TrackingParameters =
    [
        "fbclid",
        "gclid",
        "ref"
    ];

    private const string TrackingPrefix = "utm_";

    public static string? FromLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        if (host.Length == 0) return null;

        // IsDefaultPort covers http on 80 and https on 443, which is the only
        // case where two spellings of one address differ by the port alone.
        var authority = uri.IsDefaultPort ? host : $"{host}:{uri.Port}";

        var path = TrimTrailingSlash(uri.AbsolutePath);
        var query = KeepMeaningfulQuery(uri.Query);

        return query.Length == 0
            ? $"{scheme}://{authority}{path}"
            : $"{scheme}://{authority}{path}?{query}";
    }

    /// <summary>
    /// Only one trailing slash, and never the one that is the whole path: "/"
    /// is the root document, and reducing it to an empty string would make
    /// "https://example.com/" normalise to a bare authority that no longer
    /// looks like a URL.
    /// </summary>
    private static string TrimTrailingSlash(string path)
    {
        if (path.Length <= 1) return path;
        return path[^1] == '/' ? path[..^1] : path;
    }

    /// <summary>
    /// Rebuilds the query from the parameters that survive, in the order the
    /// link had them. Order is preserved rather than sorted on purpose: two
    /// spellings of one link that differ only in parameter order are rare,
    /// and reordering somebody else's query string is a change this class has
    /// no evidence is safe.
    /// </summary>
    private static string KeepMeaningfulQuery(string rawQuery)
    {
        if (rawQuery.Length <= 1) return string.Empty;

        var kept = new List<string>();
        foreach (var pair in rawQuery[1..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var name = equals >= 0 ? pair[..equals] : pair;
            if (IsTracking(name)) continue;
            kept.Add(pair);
        }

        return string.Join('&', kept);
    }

    private static bool IsTracking(string name) =>
        name.StartsWith(TrackingPrefix, StringComparison.OrdinalIgnoreCase)
        || TrackingParameters.Contains(name, StringComparer.OrdinalIgnoreCase);
}
