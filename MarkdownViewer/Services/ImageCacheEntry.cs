using System.Text.Json.Serialization;

namespace MarkdownViewer.Services;

/// <summary>
/// Per-cached-image metadata: the local file path, the HTTP cache headers
/// the origin returned (so we can revalidate later with conditional GETs),
/// the freshness window, and the LRU access time. Persisted alongside each
/// cached image as a <c>{hash}.meta.json</c> sidecar so cache state survives
/// app restarts.
/// </summary>
public sealed class ImageCacheEntry
{
    /// <summary>The original remote URL (or local-svg cache key).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Absolute path to the cached file on disk.</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>
    /// Natural display dimensions in DIPs. For SVG-converted PNGs this is
    /// the SVG's intrinsic 1x size; for raster originals it's the pixel
    /// size of the file.
    /// </summary>
    public int NaturalWidth { get; set; }
    public int NaturalHeight { get; set; }

    /// <summary>UTC timestamp when the image was downloaded from the origin.</summary>
    public DateTime FetchedAtUtc { get; set; }

    /// <summary>UTC timestamp the cache entry was last accessed (used for LRU).</summary>
    public DateTime LastAccessUtc { get; set; }

    /// <summary>HTTP <c>ETag</c> response header (raw, including quotes).</summary>
    public string? ETag { get; set; }

    /// <summary>HTTP <c>Last-Modified</c> response header (raw RFC1123 string).</summary>
    public string? LastModified { get; set; }

    /// <summary>
    /// Effective freshness window. Computed at fetch time from
    /// <c>Cache-Control: max-age</c>, falling back to <c>Expires</c>, and
    /// finally a heuristic default. While <c>UtcNow &lt; FreshUntilUtc</c>
    /// the cache entry is served WITHOUT a network call.
    /// </summary>
    public DateTime FreshUntilUtc { get; set; }

    /// <summary>Origin's reported MIME type.</summary>
    public string? ContentType { get; set; }

    /// <summary>True when the entry has been marked stale by the app and
    /// must revalidate (conditional GET) on next access.</summary>
    public bool ForceRevalidate { get; set; }

    [JsonIgnore]
    public bool IsFresh => !ForceRevalidate && DateTime.UtcNow < FreshUntilUtc;
}
