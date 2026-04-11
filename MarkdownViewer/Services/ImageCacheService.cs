using System.Security.Cryptography;
using System.Text;
using Mostlylucid.ImageSharp.Svg;
using SkiaSharp;

namespace MarkdownViewer.Services;

/// <summary>
/// Service for downloading and caching remote images.
/// Supports PNG, JPG, GIF, WebP, and SVG (converted to PNG).
/// </summary>
public class ImageCacheService : IDisposable
{
    private const int MaxConcurrentDownloads = 4;
    private const long MaxRemoteImageBytes = 10L * 1024 * 1024; // 10 MB
    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;
    private readonly Dictionary<string, string> _urlToLocalPath = new();
    /// <summary>
    /// Natural display dimensions for cached images. For SVG-converted PNGs
    /// this is the SVG's intrinsic 1x size (the cached PNG is 2× for hi-DPI
    /// crispness, so the markdown rewriter emits explicit width/height to
    /// downscale at composite time). For raster originals it is the PNG's
    /// pixel size, so authors can rely on natural rendering.
    /// </summary>
    private readonly Dictionary<string, (int Width, int Height)> _urlToDisplaySize = new();
    private readonly SemaphoreSlim _downloadLimiter = new(MaxConcurrentDownloads, MaxConcurrentDownloads);
    private bool _disposed;

    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private const long MaxCacheSizeBytes = 500L * 1024 * 1024; // 500 MB

    public ImageCacheService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("lucidVIEW/1.0 (Markdown Viewer)");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        _cacheDir = Path.Combine(Path.GetTempPath(), "lucidview-images");
        Directory.CreateDirectory(_cacheDir);

        // Run eviction on startup (non-blocking)
        Task.Run(EvictStaleAndOversizedEntries);
    }

    /// <summary>
    /// Cache a remote image and return the local file path.
    /// For SVG images, converts to PNG for better compatibility.
    /// </summary>
    /// <param name="url">The remote image URL</param>
    /// <returns>Local file path to the cached image, or original URL if caching fails</returns>
    public async Task<string> CacheRemoteImageAsync(string url)
    {
        var debug = Environment.GetEnvironmentVariable("LUCIDVIEW_IMG_DEBUG") == "1";
        if (string.IsNullOrWhiteSpace(url))
            return url;

        // In-session cache hit (cleared on every document open via
        // InvalidateInMemoryCache so we always re-download per document).
        if (_urlToLocalPath.TryGetValue(url, out var cachedPath) && File.Exists(cachedPath))
        {
            if (debug) Console.WriteLine($"[cache] HIT {url}");
            return cachedPath;
        }
        if (debug) Console.WriteLine($"[cache] FETCH {url}");

        await _downloadLimiter.WaitAsync();
        try
        {
            // Generate cache filename from URL hash
            var hash = ComputeUrlHash(url);
            var extension = GetImageExtension(url);
            var isSvg = extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);

            // SVGs get converted to PNG
            var finalExtension = isSvg ? ".png" : extension;
            var localPath = Path.Combine(_cacheDir, $"{hash}{finalExtension}");

            // Download image with bounded buffering so a single document cannot spike memory.
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!IsSupportedRemoteImage(contentType, extension))
                return url;

            if (response.Content.Headers.ContentLength is > MaxRemoteImageBytes)
                return url;

            var bytes = await ReadContentWithLimitAsync(response.Content, MaxRemoteImageBytes);

            if (bytes.Length == 0)
                return url;

            // Handle SVG conversion
            if (isSvg || contentType.Contains("svg", StringComparison.OrdinalIgnoreCase))
            {
                var svgContent = Encoding.UTF8.GetString(bytes);
                var conv = ConvertSvgToPng(svgContent);
                if (debug) Console.WriteLine($"[cache] svg→png {url} bytes={conv?.Bytes.Length ?? -1} natural={conv?.NaturalWidth}x{conv?.NaturalHeight}");
                if (conv != null)
                {
                    await File.WriteAllBytesAsync(localPath, conv.Value.Bytes);
                    _urlToLocalPath[url] = localPath;
                    _urlToDisplaySize[url] = (conv.Value.NaturalWidth, conv.Value.NaturalHeight);
                    return localPath;
                }
                return url; // Fallback to original URL if conversion fails
            }

            // Save other image formats directly
            await File.WriteAllBytesAsync(localPath, bytes);
            _urlToLocalPath[url] = localPath;
            // Record raster dimensions so markdown can render at natural size.
            var dims = TryGetRasterDimensions(bytes);
            if (dims.HasValue)
                _urlToDisplaySize[url] = dims.Value;
            return localPath;
        }
        catch
        {
            // On any error, return original URL so browser/renderer can try
            return url;
        }
        finally
        {
            _downloadLimiter.Release();
        }
    }

    /// <summary>
    /// Convert SVG content to PNG bytes via the AOT-clean
    /// Mostlylucid.ImageSharp.Svg renderer. Returns the encoded PNG along
    /// with the SVG's intrinsic 1x dimensions so the markdown rewriter can
    /// emit display-size constraints.
    /// </summary>
    private static (byte[] Bytes, int NaturalWidth, int NaturalHeight)? ConvertSvgToPng(string svgContent)
    {
        try
        {
            // Render at 2x for hi-DPI crispness — the natural-size constraint
            // applied by the markdown viewer's post-render walker downscales
            // back to intended DIPs at composite time.
            var result = SvgImage.LoadAsPng(svgContent, new SvgRenderOptions { Scale = 2f });
            if (result.Bytes.Length == 0 || result.NaturalWidth <= 0 || result.NaturalHeight <= 0)
                return null;
            return (result.Bytes, result.NaturalWidth, result.NaturalHeight);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Read the pixel dimensions of a raster image header without decoding
    /// the full bitmap. Returns null if the format is not recognised by
    /// SkiaSharp's codec layer.
    /// </summary>
    private static (int Width, int Height)? TryGetRasterDimensions(byte[] bytes)
    {
        try
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec == null) return null;
            return (codec.Info.Width, codec.Info.Height);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Pre-cache all remote images found in markdown content
    /// </summary>
    public async Task PreCacheImagesAsync(IEnumerable<string> imageUrls)
    {
        var tasks = imageUrls
            .Where(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .Select(CacheRemoteImageAsync);

        await Task.WhenAll(tasks);
    }

    private static bool IsSupportedRemoteImage(string contentType, string extension)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (contentType.Contains("svg", StringComparison.OrdinalIgnoreCase))
            return true;

        return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".ico";
    }

    private static async Task<byte[]> ReadContentWithLimitAsync(HttpContent content, long maxBytes)
    {
        await using var stream = await content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        long total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk, 0, chunk.Length);
            if (read == 0)
                break;

            total += read;
            if (total > maxBytes)
                throw new InvalidOperationException("Remote image exceeds size limit.");

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Get the cached local path for a URL, or null if not cached
    /// </summary>
    public string? GetCachedPath(string url)
    {
        if (_urlToLocalPath.TryGetValue(url, out var path) && File.Exists(path))
            return path;
        return null;
    }

    /// <summary>
    /// Convert a local <c>.svg</c> file to a cached PNG via the AOT-clean
    /// Mostlylucid.ImageSharp.Svg renderer. Synchronous because there's no
    /// network IO involved — local SVGs are typically small (icons,
    /// diagrams) and rendering takes milliseconds.
    /// </summary>
    /// <param name="absolutePath">Absolute filesystem path to the .svg file.</param>
    /// <returns>Cached PNG path on success, or null if the file is missing
    /// or fails to render. Re-renders when the source file's mtime changes.</returns>
    public string? CacheLocalSvg(string absolutePath)
    {
        var debug = Environment.GetEnvironmentVariable("LUCIDVIEW_IMG_DEBUG") == "1";
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            return null;

        // Cache key includes the file's last-write-time so re-rendering
        // happens automatically when the source SVG is edited on disk.
        var mtime = File.GetLastWriteTimeUtc(absolutePath).Ticks;
        var cacheKey = $"local-svg|{absolutePath}|{mtime}";
        if (_urlToLocalPath.TryGetValue(cacheKey, out var existing) && File.Exists(existing))
        {
            if (debug) Console.WriteLine($"[cache] LOCAL-HIT {absolutePath}");
            return existing;
        }

        try
        {
            var svgContent = File.ReadAllText(absolutePath);
            var hash = ComputeUrlHash(cacheKey);
            var pngPath = Path.Combine(_cacheDir, $"{hash}.png");

            var result = Mostlylucid.ImageSharp.Svg.SvgImage.LoadAsPng(
                svgContent,
                new Mostlylucid.ImageSharp.Svg.SvgRenderOptions { Scale = 2f });
            if (result.Bytes.Length == 0 || result.NaturalWidth <= 0 || result.NaturalHeight <= 0)
                return null;

            File.WriteAllBytes(pngPath, result.Bytes);
            _urlToLocalPath[cacheKey] = pngPath;
            _urlToDisplaySize[cacheKey] = (result.NaturalWidth, result.NaturalHeight);
            if (debug) Console.WriteLine($"[cache] LOCAL-SVG→PNG {absolutePath} → {pngPath} ({result.NaturalWidth}x{result.NaturalHeight})");
            return pngPath;
        }
        catch (Exception ex)
        {
            if (debug) Console.WriteLine($"[cache] LOCAL-SVG ERROR {absolutePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get the natural display size (DIPs) for a cached image, or null if
    /// either the image is not cached or its dimensions could not be
    /// determined. SVG-converted PNGs return their 1x intrinsic size; raster
    /// originals return their pixel dimensions.
    /// </summary>
    public (int Width, int Height)? GetCachedDisplaySize(string url)
    {
        return _urlToDisplaySize.TryGetValue(url, out var size) ? size : null;
    }

    /// <summary>
    /// Reverse lookup: given a cached local file path (the value side of the
    /// url→path map), return the natural display size that should be applied
    /// when rendering. Used by the markdown viewer's post-render visual tree
    /// walker which sees the rewritten local paths, not the original URLs.
    /// </summary>
    public (int Width, int Height)? GetCachedDisplaySizeByLocalPath(string localPath)
    {
        foreach (var (url, path) in _urlToLocalPath)
        {
            if (string.Equals(path, localPath, StringComparison.OrdinalIgnoreCase)
                && _urlToDisplaySize.TryGetValue(url, out var size))
                return size;
        }
        return null;
    }

    /// <summary>
    /// Forget the in-memory URL→path mapping so the next
    /// <see cref="CacheRemoteImageAsync"/> for any URL re-downloads from the
    /// origin. The on-disk PNG file remains as a fallback if the network
    /// fetch fails. Call this on every document open so dynamic images
    /// (shields.io build status, latest-version badges) reflect the current
    /// state instead of whatever was cached on a previous open.
    /// </summary>
    public void InvalidateInMemoryCache()
    {
        _urlToLocalPath.Clear();
        _urlToDisplaySize.Clear();
    }

    /// <summary>
    /// Clear all cached images
    /// </summary>
    public void ClearCache()
    {
        _urlToLocalPath.Clear();
        _urlToDisplaySize.Clear();
        try
        {
            if (Directory.Exists(_cacheDir))
            {
                foreach (var file in Directory.GetFiles(_cacheDir))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Compute a hash of the URL for use as filename (AOT-safe)
    /// </summary>
    private static string ComputeUrlHash(string url)
    {
        var bytes = Encoding.UTF8.GetBytes(url);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Get the image extension from URL or content type
    /// </summary>
    private static string GetImageExtension(string url)
    {
        // Remove query string
        var path = url.Split('?')[0].Split('#')[0];
        var ext = Path.GetExtension(path).ToLowerInvariant();

        return ext switch
        {
            ".png" => ".png",
            ".jpg" or ".jpeg" => ".jpg",
            ".gif" => ".gif",
            ".webp" => ".webp",
            ".svg" => ".svg",
            ".ico" => ".ico",
            _ => ".png" // Default to PNG
        };
    }

    private void EvictStaleAndOversizedEntries()
    {
        try
        {
            if (!Directory.Exists(_cacheDir))
                return;

            var files = new DirectoryInfo(_cacheDir).GetFiles()
                .OrderBy(f => f.LastAccessTimeUtc)
                .ToList();

            var cutoff = DateTime.UtcNow - MaxAge;

            // Phase 1: Delete files older than MaxAge
            for (var i = files.Count - 1; i >= 0; i--)
            {
                if (files[i].LastWriteTimeUtc < cutoff)
                {
                    try { files[i].Delete(); files.RemoveAt(i); } catch { }
                }
            }

            // Phase 2: LRU eviction if total size exceeds limit
            var totalSize = files.Sum(f => f.Length);
            var idx = 0;
            while (totalSize > MaxCacheSizeBytes && idx < files.Count)
            {
                totalSize -= files[idx].Length;
                try { files[idx].Delete(); } catch { }
                idx++;
            }
        }
        catch
        {
            // Non-critical cleanup
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _downloadLimiter.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
