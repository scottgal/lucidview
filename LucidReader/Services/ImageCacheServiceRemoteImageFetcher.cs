using Mostlylucid.LucidView.Markdown.Services;

namespace LucidReader.Services;

/// <summary>
/// Production <see cref="IRemoteImageFetcher"/> backed by lucidVIEW's shared
/// <see cref="ImageCacheService"/>.
///
/// <see cref="ImageCacheService.CacheRemoteImageAsync"/> never throws for an
/// ordinary network/IO/timeout failure: it swallows those internally and
/// returns either a stale local path or the original URL unchanged. The only
/// way to tell "fetched" from "gave up" from the return value alone is to
/// compare it against the URL that was passed in, which is what this class
/// does. Genuinely unexpected exceptions are still caught here so a single
/// bad image can never take down the whole rewrite pass.
/// </summary>
public sealed class ImageCacheServiceRemoteImageFetcher(ImageCacheService cache) : IRemoteImageFetcher
{
    public async Task<CachedImage?> FetchAsync(string absoluteUrl, CancellationToken ct)
    {
        try
        {
            var local = await cache.CacheRemoteImageAsync(absoluteUrl, ct);
            if (string.IsNullOrEmpty(local) || string.Equals(local, absoluteUrl, StringComparison.Ordinal))
                return null;

            if (!File.Exists(local)) return null;

            return new CachedImage(local, new FileInfo(local).Length);
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
}
