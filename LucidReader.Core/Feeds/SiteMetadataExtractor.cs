using System.Text.RegularExpressions;

namespace LucidReader.Core.Feeds;

/// <summary>
/// Pulls a site icon, social-card image and description out of HTML this
/// caller has ALREADY downloaded for some other reason - FeedAutodiscovery's
/// site page, or the article page OfflineDownloader fetched for full-text
/// extraction. This type performs no I/O of its own: adding a call here must
/// never add a second fetch of a page already on hand.
///
/// Uses the same GeneratedRegex-over-tags approach as FeedAutodiscovery,
/// sharing its attribute-extraction helper, rather than pulling in an HTML
/// parser: the only thing being read is link/meta elements, and a missed tag
/// costs one missing icon or image, not a functional failure.
/// </summary>
public static partial class SiteMetadataExtractor
{
    /// <summary>
    /// Icon precedence, highest first: an explicit rel="icon", then the
    /// legacy rel="shortcut icon", then rel="apple-touch-icon". A
    /// /favicon.ico guess at the site root is NOT produced here - a page that
    /// declares no icon yields a null IconUrl; callers that want the guessed
    /// fallback (FeedAutodiscovery) add it themselves, since that guess is a
    /// URL to record, not something this pure-parsing type should assume.
    /// </summary>
    public static SiteMetadata Extract(string html, Uri baseUri)
    {
        string? icon = null;
        var bestIconRank = int.MaxValue;

        foreach (Match match in LinkTagPattern().Matches(html))
        {
            var tag = match.Value;
            var rel = HtmlAttributeParsing.AttributeValue(tag, "rel");
            var rank = ClassifyIconRel(rel);
            if (rank is null || rank >= bestIconRank) continue;

            var resolved = ResolveSafe(baseUri, HtmlAttributeParsing.AttributeValue(tag, "href"));
            if (resolved is null) continue;

            icon = resolved;
            bestIconRank = rank.Value;
        }

        string? ogImage = null;
        string? twitterImage = null;
        string? ogDescription = null;
        string? plainDescription = null;

        foreach (Match match in MetaTagPattern().Matches(html))
        {
            var tag = match.Value;
            var content = HtmlAttributeParsing.AttributeValue(tag, "content");
            if (content.Length == 0) continue;

            var property = HtmlAttributeParsing.AttributeValue(tag, "property");
            var name = HtmlAttributeParsing.AttributeValue(tag, "name");

            if (ogImage is null && property.Equals("og:image", StringComparison.OrdinalIgnoreCase))
                ogImage = ResolveSafe(baseUri, content);
            else if (twitterImage is null && name.Equals("twitter:image", StringComparison.OrdinalIgnoreCase))
                twitterImage = ResolveSafe(baseUri, content);

            if (ogDescription is null && property.Equals("og:description", StringComparison.OrdinalIgnoreCase))
                ogDescription = content;
            else if (plainDescription is null && name.Equals("description", StringComparison.OrdinalIgnoreCase))
                plainDescription = content;
        }

        return new SiteMetadata(
            icon,
            ogImage ?? twitterImage,
            ogDescription ?? plainDescription);
    }

    /// <summary>
    /// Lower is higher precedence: 0 = rel="icon" exactly, 1 = the two-token
    /// legacy rel="shortcut icon", 2 = rel="apple-touch-icon". Null means the
    /// tag is not an icon link at all.
    /// </summary>
    private static int? ClassifyIconRel(string rel)
    {
        if (rel.Length == 0) return null;

        var tokens = HtmlAttributeParsing.Tokens(rel);
        if (tokens.Any(t => t.Equals("apple-touch-icon", StringComparison.OrdinalIgnoreCase)))
            return 2;
        if (tokens.Any(t => t.Equals("icon", StringComparison.OrdinalIgnoreCase)))
            return tokens.Length == 1 ? 0 : 1;

        return null;
    }

    /// <summary>
    /// Resolves a possibly-relative URL against the page's base URI and
    /// refuses anything that is not http/https once resolved. These URLs
    /// come from remote pages and will later be fetched and cached by other
    /// code, so the same allowlist discipline as everywhere else in this
    /// codebase (FeedAutodiscovery, ParsedItem.Link) applies here, at the
    /// point of extraction, rather than trusting a downstream fetch to
    /// reject a javascript:/data:/file: URL.
    /// </summary>
    private static string? ResolveSafe(Uri baseUri, string value)
    {
        if (value.Length == 0) return null;
        if (!Uri.TryCreate(baseUri, value, out var absolute)) return null;
        if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps) return null;
        return absolute.ToString();
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTagPattern();

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTagPattern();
}
