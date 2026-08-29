using LucidReader.Core.Model;

namespace LucidReader.Services;

/// <summary>
/// Resolves a single remote image URL (a feed's favicon, an article's
/// OpenGraph image) to a local cached path, for the sidebar, item list and
/// reading pane thumbnails/hero images.
///
/// Deliberately thin: an http/https allowlist, the same CacheImages gate
/// that governs article images, and a delegation to
/// <see cref="IRemoteImageFetcher"/> - the seam Task 5 introduced, which
/// already owns fetching, on-disk caching, MaxImageBytes enforcement, and
/// swallowing an ordinary fetch failure to null rather than throwing. This
/// class adds no second fetch path and no second cache; it exists only so
/// callers that just want "give me a local path for this URL, or null" do
/// not each have to reimplement the allowlist and the settings gate.
/// </summary>
public sealed class ImageResolver(IRemoteImageFetcher fetcher, Func<ReaderSettings> settings)
{
    public async Task<string?> ResolveAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        // The user's single CacheImages switch governs favicons and
        // thumbnails exactly as it governs article images - one setting,
        // not three. Checked before touching the network at all.
        if (!settings().CacheImages) return null;

        // A favicon or OpenGraph URL comes from remote, attacker-controlled
        // HTML (Task 8b's SiteMetadataExtractor), so it gets the same
        // scheme allowlist as article images: refuse anything other than
        // http/https before it ever reaches the fetcher.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute)) return null;
        if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps) return null;

        var fetched = await fetcher.FetchAsync(absolute.ToString(), ct);
        return fetched?.LocalPath;
    }
}
