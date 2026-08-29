using System.Text.RegularExpressions;
using LucidReader.Core.Model;
using MarkdownViewer.Services;
using Mostlylucid.LucidView.Markdown.Services;

namespace LucidReader.Services;

/// <summary>
/// Article image caching on top of lucidVIEW's ImageCacheService.
///
/// Rewrites markdown image references to local file paths so an article read
/// offline still shows its pictures. Any image that cannot be fetched keeps
/// its original remote URL, which renders fine when online and degrades to a
/// missing image when not, rather than breaking the article.
/// </summary>
public sealed partial class AvaloniaArticleImageCache(
    ImageCacheService cache,
    Func<ReaderSettings> settings) : IArticleImageCache
{
    public async Task<string> RewriteAsync(
        string markdown,
        Uri? baseUri,
        CancellationToken ct = default)
    {
        if (!settings().CacheImages) return markdown;

        var matches = MarkdownImagePattern().Matches(markdown);
        if (matches.Count == 0) return markdown;

        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in matches)
        {
            ct.ThrowIfCancellationRequested();

            var url = match.Groups["url"].Value.Trim();
            if (url.Length == 0 || replacements.ContainsKey(url)) continue;

            // Skip anything already local, and refuse any scheme other than
            // http and https: a feed is attacker-controlled input.
            if (!Uri.TryCreate(baseUri, url, out var absolute)) continue;
            if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps) continue;

            try
            {
                var local = await cache.CacheRemoteImageAsync(absolute.ToString(), ct);
                if (!string.IsNullOrEmpty(local)) replacements[url] = local;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Leave this image remote and carry on with the rest.
            }
        }

        if (replacements.Count == 0) return markdown;

        return MarkdownImagePattern().Replace(markdown, match =>
        {
            var url = match.Groups["url"].Value.Trim();
            return replacements.TryGetValue(url, out var local)
                ? $"![{match.Groups["alt"].Value}]({local})"
                : match.Value;
        });
    }

    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex MarkdownImagePattern();
}
