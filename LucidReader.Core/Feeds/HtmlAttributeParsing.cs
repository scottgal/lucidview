using System.Net;
using System.Text.RegularExpressions;

namespace LucidReader.Core.Feeds;

/// <summary>
/// Regex-based attribute extraction shared by FeedAutodiscovery and
/// SiteMetadataExtractor. Both read a single already-downloaded HTML page
/// looking for values inside &lt;link&gt; and &lt;meta&gt; tags, and both need
/// the same entity-decoding and whitespace-token handling: a second copy of
/// this logic would inevitably drift from this one (e.g. one place learning
/// to handle single-quoted attributes and the other not).
/// </summary>
internal static class HtmlAttributeParsing
{
    /// <summary>
    /// Reads one attribute's value out of a single tag's raw text (e.g. the
    /// full "&lt;link rel=... href=...&gt;" match), decoding HTML entities.
    /// Sites (WordPress especially) routinely emit entity-encoded attribute
    /// values, e.g. href="/feed?a=1&amp;b=2" - left undecoded, that "&amp;"
    /// would be baked literally into a resolved URL's query string.
    /// </summary>
    public static string AttributeValue(string tag, string attribute)
    {
        var match = AttributePattern(attribute).Match(tag);
        if (!match.Success) return string.Empty;
        return WebUtility.HtmlDecode(match.Groups["v"].Value.Trim());
    }

    /// <summary>
    /// Splits a whitespace-separated attribute value (e.g. a "rel" list) into
    /// its individual tokens.
    /// </summary>
    public static string[] Tokens(string valueList) =>
        valueList.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// True when one of the whitespace-separated tokens in valueList equals
    /// token exactly. A substring check would false-positive on any token
    /// that happens to contain the word (rel is a space-separated list per
    /// the HTML spec, e.g. rel="alternate home").
    /// </summary>
    public static bool HasToken(string valueList, string token) =>
        Tokens(valueList).Any(t => t.Equals(token, StringComparison.OrdinalIgnoreCase));

    private static Regex AttributePattern(string attribute) => new(
        attribute + @"\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))",
        RegexOptions.IgnoreCase);
}
