namespace LucidReader.Services;

/// <summary>
/// The one piece of <see cref="AvaloniaArticleImageCache"/> that actually
/// touches the network and the disk. Pulled out as a seam so the rewrite
/// logic (regex matching, scheme allowlist, the CacheImages gate, the size
/// limit) can be unit tested with a fake, without Avalonia and without real
/// HTTP or file IO.
/// </summary>
public interface IRemoteImageFetcher
{
    /// <summary>
    /// Fetches (or returns an already-cached copy of) the image at
    /// <paramref name="absoluteUrl"/>. Returns null if the image could not be
    /// fetched at all: callers should leave the original remote URL in place
    /// rather than pointing at nothing.
    /// </summary>
    Task<CachedImage?> FetchAsync(string absoluteUrl, CancellationToken ct);
}

/// <summary>A successfully cached image: where it landed, and how big it is.</summary>
public readonly record struct CachedImage(string LocalPath, long SizeBytes);
