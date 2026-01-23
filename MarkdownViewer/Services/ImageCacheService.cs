using System.Security.Cryptography;
using System.Text;
using SkiaSharp;
using Svg.Skia;

namespace MarkdownViewer.Services;

/// <summary>
/// Service for downloading and caching remote images.
/// Supports PNG, JPG, GIF, WebP, and SVG (converted to PNG).
/// </summary>
public class ImageCacheService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;
    private readonly Dictionary<string, string> _urlToLocalPath = new();
    private bool _disposed;

    public ImageCacheService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("lucidVIEW/1.0 (Markdown Viewer)");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        _cacheDir = Path.Combine(Path.GetTempPath(), "lucidview-images");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// Cache a remote image and return the local file path.
    /// For SVG images, converts to PNG for better compatibility.
    /// </summary>
    /// <param name="url">The remote image URL</param>
    /// <returns>Local file path to the cached image, or original URL if caching fails</returns>
    public async Task<string> CacheRemoteImageAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        // Check if already cached this session
        if (_urlToLocalPath.TryGetValue(url, out var cachedPath) && File.Exists(cachedPath))
            return cachedPath;

        try
        {
            // Generate cache filename from URL hash
            var hash = ComputeUrlHash(url);
            var extension = GetImageExtension(url);
            var isSvg = extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);

            // SVGs get converted to PNG
            var finalExtension = isSvg ? ".png" : extension;
            var localPath = Path.Combine(_cacheDir, $"{hash}{finalExtension}");

            // Download image
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var bytes = await response.Content.ReadAsByteArrayAsync();

            if (bytes.Length == 0)
                return url;

            // Handle SVG conversion
            if (isSvg || contentType.Contains("svg", StringComparison.OrdinalIgnoreCase))
            {
                var svgContent = Encoding.UTF8.GetString(bytes);
                var pngBytes = ConvertSvgToPng(svgContent);
                if (pngBytes != null)
                {
                    await File.WriteAllBytesAsync(localPath, pngBytes);
                    _urlToLocalPath[url] = localPath;
                    return localPath;
                }
                return url; // Fallback to original URL if conversion fails
            }

            // Save other image formats directly
            await File.WriteAllBytesAsync(localPath, bytes);
            _urlToLocalPath[url] = localPath;
            return localPath;
        }
        catch
        {
            // On any error, return original URL so browser/renderer can try
            return url;
        }
    }

    /// <summary>
    /// Convert SVG content to PNG bytes using SkiaSharp
    /// </summary>
    private static byte[]? ConvertSvgToPng(string svgContent)
    {
        try
        {
            using var svg = new SKSvg();
            svg.FromSvg(svgContent);

            if (svg.Picture == null)
                return null;

            var bounds = svg.Picture.CullRect;
            var scale = 2f; // 2x for crisp rendering
            var width = (int)(bounds.Width * scale);
            var height = (int)(bounds.Height * scale);

            if (width <= 0 || height <= 0)
                return null;

            // Limit maximum size to prevent memory issues
            if (width > 4096 || height > 4096)
            {
                var maxDim = Math.Max(width, height);
                scale = 4096f / maxDim * scale;
                width = (int)(bounds.Width * scale);
                height = (int)(bounds.Height * scale);
            }

            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.DrawPicture(svg.Picture);

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
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
    /// Clear all cached images
    /// </summary>
    public void ClearCache()
    {
        _urlToLocalPath.Clear();
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

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
