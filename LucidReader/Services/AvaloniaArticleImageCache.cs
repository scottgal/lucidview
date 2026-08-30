using System.Text.RegularExpressions;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using MarkdownViewer.Services;

namespace LucidReader.Services;

/// <summary>
/// Article image caching.
///
/// Rewrites markdown image references (and any raw HTML &lt;img&gt; tags
/// embedded in the markdown - the HTML-to-markdown converter falls back to
/// emitting raw HTML for complex tables, and RSS content:encoded commonly
/// uses layout tables with images inside them) to local file paths, so an
/// article read offline still shows its pictures. Any image that cannot be
/// fetched, or that fetches larger than <see cref="ReaderSettings.MaxImageBytes"/>,
/// keeps its original remote URL, which renders fine when online and
/// degrades to a missing image when not, rather than breaking the article.
///
/// The actual fetch is delegated to <see cref="IRemoteImageFetcher"/> so this
/// class - the regex matching, the scheme allowlist, the CacheImages gate and
/// the size limit, all of which run over attacker-controlled feed content -
/// can be unit tested without Avalonia and without real network or disk IO.
///
/// Known limitations of the regexes below, left as-is because they are cheap
/// to reason about and self-consistent between match and replace:
/// - Not fence-aware: an image reference appearing as literal text inside a
///   fenced code sample would still be rewritten. Unlikely in RSS content and
///   harmless when it happens (worst case, a code sample that quotes markdown
///   syntax gets a local path substituted into it).
/// - A URL containing an unescaped closing parenthesis, or alt text containing
///   nested square brackets, can truncate the captured group early. Both are
///   self-consistent between the match and the replace (the same truncated
///   text is looked up and substituted), so the cost is at most one image
///   that is not recognised and stays remote, never a corrupted document.
/// </summary>
public sealed partial class AvaloniaArticleImageCache(
    IRemoteImageFetcher fetcher,
    Func<ReaderSettings> settings) : IArticleImageCache
{
    /// <summary>
    /// How many image URLs one article may cost in fetches. The markdown is
    /// publisher-controlled and this runs unattended during offline download,
    /// so without a cap a single item advertising ten thousand images is ten
    /// thousand outbound requests. Far above what any real article carries.
    /// </summary>
    private const int MaxImagesPerArticle = 100;


    public async Task<string> RewriteAsync(
        string markdown,
        Uri? baseUri,
        CancellationToken ct = default)
    {
        if (!settings().CacheImages) return markdown;

        var markdownMatches = MarkdownImagePattern().Matches(markdown);
        var htmlMatches = HtmlImageSrcPattern().Matches(markdown);
        if (markdownMatches.Count == 0 && htmlMatches.Count == 0) return markdown;

        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        var attempts = 0;

        foreach (Match match in markdownMatches)
        {
            if (attempts++ >= MaxImagesPerArticle) break;
            await ResolveAsync(match.Groups["url"].Value, baseUri, replacements, ct);
        }

        foreach (Match match in htmlMatches)
        {
            if (attempts++ >= MaxImagesPerArticle) break;
            await ResolveAsync(match.Groups["url"].Value, baseUri, replacements, ct);
        }

        if (replacements.Count == 0) return markdown;

        var rewritten = MarkdownImagePattern().Replace(markdown, match =>
        {
            var url = match.Groups["url"].Value.Trim();
            return replacements.TryGetValue(url, out var local)
                ? $"![{match.Groups["alt"].Value}]({local})"
                : match.Value;
        });

        rewritten = HtmlImageSrcPattern().Replace(rewritten, match =>
        {
            var urlGroup = match.Groups["url"];
            var url = urlGroup.Value.Trim();
            if (!replacements.TryGetValue(url, out var local)) return match.Value;

            var relativeStart = urlGroup.Index - match.Index;
            return match.Value[..relativeStart] + local + match.Value[(relativeStart + urlGroup.Length)..];
        });

        return rewritten;
    }

    /// <summary>
    /// Resolves one candidate URL against <paramref name="baseUri"/>, applies
    /// the scheme allowlist and the size limit, and records a replacement if
    /// the image was fetched and is small enough to keep. Anything that fails
    /// any of these checks is simply skipped: the caller leaves the original
    /// text untouched for URLs with no entry in <paramref name="replacements"/>.
    /// </summary>
    private async Task ResolveAsync(
        string rawUrl,
        Uri? baseUri,
        Dictionary<string, string> replacements,
        CancellationToken ct)
    {
        var url = rawUrl.Trim();
        if (url.Length == 0 || replacements.ContainsKey(url)) return;

        // A feed is attacker-controlled input, so this runs the same gate
        // every other document-supplied address in the app runs: the scheme
        // allowlist keeps `file:` and `data:` away from the fetcher, and the
        // host rules keep an <img src="http://192.168.1.1/setup.cgi?reboot=1">
        // from turning automatic offline download into a request against the
        // user's own network.
        if (!Uri.TryCreate(baseUri, url, out var absolute)) return;
        if (!FeedUrlPolicy.IsAllowed(absolute.ToString())) return;

        ct.ThrowIfCancellationRequested();

        var fetched = await fetcher.FetchAsync(absolute.ToString(), ct);
        if (fetched is null) return;

        // An oversized image keeps its remote URL rather than pointing at a
        // local copy the reader decided not to keep. This is the only place
        // ReaderSettings.MaxImageBytes is enforced: ImageCacheService itself
        // has its own separate, larger, hardcoded ceiling that is not ours to
        // retune since that class is shared with lucidVIEW.
        if (fetched.Value.SizeBytes > settings().MaxImageBytes) return;

        replacements[url] = fetched.Value.LocalPath;
    }

    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex MarkdownImagePattern();

    [GeneratedRegex(@"<img\b[^>]*?\bsrc\s*=\s*(?<q>[""'])(?<url>[^""']*)\k<q>[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImageSrcPattern();
}
