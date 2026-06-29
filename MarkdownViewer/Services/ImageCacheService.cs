using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarkdownViewer.Models;
using Mostlylucid.ImageSharp.Svg;
using SkiaSharp;

namespace MarkdownViewer.Services;

/// <summary>
/// HTTP-aware image cache with proper LRU eviction and per-image metadata
/// sidecars. Honours <c>Cache-Control: max-age</c>, <c>Expires</c>, and
/// conditional <c>If-None-Match</c> / <c>If-Modified-Since</c> requests so
/// reopening a document within the freshness window skips the network
/// entirely, and stale entries revalidate via 304 instead of refetching.
/// SVG content is rendered to PNG via <see cref="SvgImage"/>.
/// </summary>
public class ImageCacheService : IDisposable
{
    private const int MaxConcurrentDownloads = 4;
    private const long MaxRemoteImageBytes = 10L * 1024 * 1024; // 10 MB
    private const int InMemoryLruCapacity = 256;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan DefaultFreshness = TimeSpan.FromHours(1);
    private const long MaxCacheSizeBytes = 500L * 1024 * 1024; // 500 MB

    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;
    private readonly SemaphoreSlim _downloadLimiter = new(MaxConcurrentDownloads, MaxConcurrentDownloads);
    private bool _disposed;

    // ─── In-memory LRU ─────────────────────────────────────────────────
    // LinkedList ordered most-recently-used at the FRONT, least-recently
    // at the BACK. The dictionary indexes nodes by URL for O(1) lookup,
    // and the list is used to evict the back when capacity is exceeded.
    private readonly object _lruLock = new();
    private readonly Dictionary<string, LinkedListNode<ImageCacheEntry>> _index =
        new(StringComparer.Ordinal);
    private readonly LinkedList<ImageCacheEntry> _lru = new();

    public ImageCacheService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent.Value);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        _cacheDir = Path.Combine(Path.GetTempPath(), "lucidview-images");
        Directory.CreateDirectory(_cacheDir);

        // Hydrate the in-memory index from disk sidecars on startup so
        // freshness checks work without a roundtrip on first access.
        Task.Run(() =>
        {
            HydrateFromDisk();
            EvictStaleAndOversizedEntries();
        });
    }

    // ────────────────────────────────────────────────────────────────────
    // Public API
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cache a remote image and return the local file path. SVGs convert
    /// to PNG. Honours HTTP cache headers — if the in-memory or on-disk
    /// entry is fresh, returns immediately without a network call. If
    /// stale, sends a conditional GET (If-None-Match / If-Modified-Since)
    /// and either reuses the cached content (304) or refetches (200).
    /// </summary>
    public async Task<string> CacheRemoteImageAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        if (TryGetEntry(url, out var entry) && File.Exists(entry.LocalPath) && entry.IsFresh)
        {
            Touch(entry);
            return entry.LocalPath;
        }

        await _downloadLimiter.WaitAsync(cancellationToken);
        try
        {
            return await FetchOrRevalidateAsync(url, entry);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            if (entry != null && File.Exists(entry.LocalPath))
                return entry.LocalPath;
            return url;
        }
        finally
        {
            _downloadLimiter.Release();
        }
    }

    /// <summary>Pre-cache all remote images found in markdown content.</summary>
    public async Task PreCacheImagesAsync(IEnumerable<string> imageUrls, CancellationToken cancellationToken = default)
    {
        var tasks = imageUrls
            .Where(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .Select(url => CacheRemoteImageAsync(url, cancellationToken));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Two-phase image cache: fetch the first <paramref name="priorityCount"/>
    /// images and signal once they're cached (so the caller can re-render with
    /// explicit-dim &lt;img&gt; tags for the visible part of the document),
    /// then continue fetching the rest in the background without blocking.
    /// Approximates viewport-priority loading: images at the top of the doc
    /// are almost always what the user sees first, so getting their dims into
    /// the rewrite pass keeps the first paint crisp. Tail images stream in
    /// via LiveMarkdown's per-Image HTTP handler as the layout reaches them.
    /// <paramref name="cancellationToken"/> aborts both the priority await
    /// and the background tail — wire it to the per-document lifetime so a
    /// rapid doc-switch session doesn't leave zombie tail downloads
    /// contending for the shared download semaphore.
    /// </summary>
    public async Task PreCacheImagesPriorityAsync(
        IReadOnlyList<string> imageUrls,
        int priorityCount,
        Func<Task>? onPriorityComplete = null,
        CancellationToken cancellationToken = default)
    {
        var remote = imageUrls
            .Where(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (remote.Count == 0) return;

        var priorityUrls = remote.Take(priorityCount).ToList();
        var tailUrls = remote.Skip(priorityCount).ToList();

        cancellationToken.ThrowIfCancellationRequested();
        await Task.WhenAll(priorityUrls.Select(url => CacheRemoteImageAsync(url, cancellationToken)));

        if (onPriorityComplete is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await onPriorityComplete();
        }

        if (tailUrls.Count == 0 || cancellationToken.IsCancellationRequested) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(tailUrls.Select(url => CacheRemoteImageAsync(url, cancellationToken)));
            }
            catch (OperationCanceledException)
            {
                // Expected on doc-switch cancellation — silent.
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                // Network / disk hiccup on tail images — log so it's
                // diagnosable. LiveMarkdown's per-Image HTTP handler will
                // refetch the URL when its Image source resolves, so the
                // first paint already-emitted (with raw URLs for un-dim'd
                // tail entries) still gets pixels eventually.
                Console.WriteLine($"[image-cache] tail batch hiccup: {ex.GetType().Name}: {ex.Message}");
            }
        }, cancellationToken);
    }

    /// <summary>Get the cached local path for a URL, or null if not cached.</summary>
    public string? GetCachedPath(string url)
    {
        if (TryGetEntry(url, out var entry) && File.Exists(entry.LocalPath))
        {
            Touch(entry);
            return entry.LocalPath;
        }
        return null;
    }

    /// <summary>
    /// Convert a local <c>.svg</c> file to a cached PNG via the AOT-clean
    /// Mostlylucid.ImageSharp.Svg renderer. Synchronous because there's no
    /// network IO involved. Cache key includes the file's mtime so the
    /// rasterized PNG is invalidated automatically when the source SVG
    /// changes on disk.
    /// </summary>
    public string? CacheLocalSvg(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            return null;

        var mtime = File.GetLastWriteTimeUtc(absolutePath).Ticks;
        var cacheKey = $"local-svg|{absolutePath}|{mtime}";

        if (TryGetEntry(cacheKey, out var existing) && File.Exists(existing.LocalPath))
        {
            Touch(existing);
            return existing.LocalPath;
        }

        try
        {
            var svgContent = File.ReadAllText(absolutePath);
            var hash = ComputeUrlHash(cacheKey);
            var pngPath = Path.Combine(_cacheDir, $"{hash}.png");

            var result = SvgImage.LoadAsPng(svgContent, new SvgRenderOptions { Scale = 2f });
            if (result.Bytes.Length == 0 || result.NaturalWidth <= 0 || result.NaturalHeight <= 0)
                return null;

            File.WriteAllBytes(pngPath, result.Bytes);

            // Mtime is baked into the cache key so a source-file edit produces a
            // new key; this entry can stay fresh effectively forever.
            var entry = new ImageCacheEntry
            {
                Url = cacheKey,
                LocalPath = pngPath,
                NaturalWidth = result.NaturalWidth,
                NaturalHeight = result.NaturalHeight,
                FetchedAtUtc = DateTime.UtcNow,
                LastAccessUtc = DateTime.UtcNow,
                FreshUntilUtc = DateTime.UtcNow.AddYears(100),
                ContentType = "image/svg+xml",
            };
            InsertOrUpdate(entry);
            WriteSidecar(entry, hash);

            return pngPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Get the natural display size for a cached image, or null.</summary>
    public (int Width, int Height)? GetCachedDisplaySize(string url)
    {
        if (TryGetEntry(url, out var entry) && entry.NaturalWidth > 0 && entry.NaturalHeight > 0)
            return (entry.NaturalWidth, entry.NaturalHeight);
        return null;
    }

    /// <summary>
    /// Reverse lookup: given a cached local file path, return the natural
    /// display size for the entry that owns that path. Used by the markdown
    /// viewer's post-render visual tree walker which sees the rewritten
    /// local paths, not the original URLs.
    /// </summary>
    public (int Width, int Height)? GetCachedDisplaySizeByLocalPath(string localPath)
    {
        lock (_lruLock)
        {
            foreach (var node in _lru)
            {
                if (string.Equals(node.LocalPath, localPath, StringComparison.OrdinalIgnoreCase)
                    && node.NaturalWidth > 0 && node.NaturalHeight > 0)
                    return (node.NaturalWidth, node.NaturalHeight);
            }
        }
        return null;
    }

    /// <summary>
    /// Mark every in-memory entry stale so the next <see cref="CacheRemoteImageAsync"/>
    /// call sends a conditional GET. The on-disk file remains so 304
    /// responses don't need to refetch the body. Replaces the previous
    /// "drop everything" semantic — we now let HTTP cache headers decide
    /// whether to actually go to the origin.
    /// </summary>
    public void InvalidateInMemoryCache()
    {
        lock (_lruLock)
        {
            foreach (var entry in _lru)
                entry.ForceRevalidate = true;
        }
    }

    /// <summary>Clear all cached images and metadata.</summary>
    public void ClearCache()
    {
        lock (_lruLock)
        {
            _index.Clear();
            _lru.Clear();
        }
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

    // ────────────────────────────────────────────────────────────────────
    // HTTP fetch + revalidation
    // ────────────────────────────────────────────────────────────────────

    private async Task<string> FetchOrRevalidateAsync(string url, ImageCacheEntry? existingEntry)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingEntry != null && File.Exists(existingEntry.LocalPath))
        {
            if (!string.IsNullOrEmpty(existingEntry.ETag))
                request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(existingEntry.ETag));
            if (!string.IsNullOrEmpty(existingEntry.LastModified) &&
                DateTimeOffset.TryParse(existingEntry.LastModified, out var lm))
                request.Headers.IfModifiedSince = lm;
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        // 304 Not Modified — the cached body is still good.
        if (response.StatusCode == HttpStatusCode.NotModified && existingEntry != null && File.Exists(existingEntry.LocalPath))
        {
            existingEntry.ForceRevalidate = false;
            existingEntry.FreshUntilUtc = ComputeFreshUntil(response.Headers, response.Content?.Headers);
            existingEntry.LastAccessUtc = DateTime.UtcNow;
            UpdateValidators(existingEntry, response);
            WriteSidecar(existingEntry, ComputeUrlHash(url));
            return existingEntry.LocalPath;
        }

        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var extension = GetImageExtension(url);
        if (!IsSupportedRemoteImage(contentType, extension))
            return url;

        if (response.Content.Headers.ContentLength is > MaxRemoteImageBytes)
            return url;

        var bytes = await ReadContentWithLimitAsync(response.Content, MaxRemoteImageBytes);
        if (bytes.Length == 0)
            return url;

        var isSvg = extension.Equals(".svg", StringComparison.OrdinalIgnoreCase) ||
                    contentType.Contains("svg", StringComparison.OrdinalIgnoreCase);
        var finalExtension = isSvg ? ".png" : extension;
        var hash = ComputeUrlHash(url);
        var localPath = Path.Combine(_cacheDir, $"{hash}{finalExtension}");

        int naturalWidth, naturalHeight;
        if (isSvg)
        {
            var svgContent = Encoding.UTF8.GetString(bytes);
            var conv = ConvertSvgToPng(svgContent);
            if (conv == null) return url;
            await File.WriteAllBytesAsync(localPath, conv.Value.Bytes);
            naturalWidth = conv.Value.NaturalWidth;
            naturalHeight = conv.Value.NaturalHeight;
        }
        else
        {
            await File.WriteAllBytesAsync(localPath, bytes);
            var dims = TryGetRasterDimensions(bytes);
            naturalWidth = dims?.Width ?? 0;
            naturalHeight = dims?.Height ?? 0;
        }

        var entry = new ImageCacheEntry
        {
            Url = url,
            LocalPath = localPath,
            NaturalWidth = naturalWidth,
            NaturalHeight = naturalHeight,
            FetchedAtUtc = DateTime.UtcNow,
            LastAccessUtc = DateTime.UtcNow,
            FreshUntilUtc = ComputeFreshUntil(response.Headers, response.Content.Headers),
            ContentType = contentType,
        };
        UpdateValidators(entry, response);

        InsertOrUpdate(entry);
        WriteSidecar(entry, hash);
        return localPath;
    }

    private static void UpdateValidators(ImageCacheEntry entry, HttpResponseMessage response)
    {
        if (response.Headers.ETag != null)
            entry.ETag = response.Headers.ETag.Tag;

        if (response.Content?.Headers.LastModified is { } lm)
            entry.LastModified = lm.ToString("R"); // RFC1123
    }

    /// <summary>
    /// Resolve the cache freshness window from <c>Cache-Control: max-age</c>
    /// (highest priority), falling back to <c>Expires</c>, finally to a
    /// 1-hour heuristic. <c>no-cache</c> / <c>no-store</c> short-circuit to
    /// "always stale" so the next access revalidates immediately.
    /// </summary>
    private static DateTime ComputeFreshUntil(HttpResponseHeaders headers, HttpContentHeaders? contentHeaders)
    {
        var cc = headers.CacheControl;
        if (cc != null)
        {
            if (cc.NoStore || cc.NoCache)
                return DateTime.UtcNow; // Always stale.
            if (cc.MaxAge is { } maxAge)
                return DateTime.UtcNow + maxAge;
        }

        if (contentHeaders?.Expires is { } expires)
            return expires.UtcDateTime;

        return DateTime.UtcNow + DefaultFreshness;
    }

    // ────────────────────────────────────────────────────────────────────
    // LRU index management
    // ────────────────────────────────────────────────────────────────────

    private bool TryGetEntry(string url, out ImageCacheEntry entry)
    {
        lock (_lruLock)
        {
            if (_index.TryGetValue(url, out var node))
            {
                entry = node.Value;
                return true;
            }
        }
        entry = null!;
        return false;
    }

    private void Touch(ImageCacheEntry entry)
    {
        lock (_lruLock)
        {
            if (_index.TryGetValue(entry.Url, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
            }
            entry.LastAccessUtc = DateTime.UtcNow;
        }
    }

    private void InsertOrUpdate(ImageCacheEntry entry)
    {
        lock (_lruLock)
        {
            if (_index.TryGetValue(entry.Url, out var existingNode))
            {
                existingNode.Value = entry;
                _lru.Remove(existingNode);
                _lru.AddFirst(existingNode);
            }
            else
            {
                var node = new LinkedListNode<ImageCacheEntry>(entry);
                _lru.AddFirst(node);
                _index[entry.Url] = node;
            }

            // Evict the back of the LRU when over capacity. Removes the
            // index entry but does NOT delete the on-disk file — that
            // happens in the size-based disk eviction pass.
            while (_lru.Count > InMemoryLruCapacity)
            {
                var last = _lru.Last;
                if (last == null) break;
                _lru.RemoveLast();
                _index.Remove(last.Value.Url);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Disk persistence (sidecar files)
    // ────────────────────────────────────────────────────────────────────

    private void WriteSidecar(ImageCacheEntry entry, string hash)
    {
        try
        {
            var path = Path.Combine(_cacheDir, $"{hash}.meta.json");
            var json = JsonSerializer.Serialize(entry, ImageCacheJsonContext.Default.ImageCacheEntry);
            File.WriteAllText(path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void HydrateFromDisk()
    {
        try
        {
            if (!Directory.Exists(_cacheDir)) return;
            var sidecars = Directory.GetFiles(_cacheDir, "*.meta.json");
            // Order by last access so the in-memory LRU mirrors disk order
            // — the most recently used entries become the first in memory.
            Array.Sort(sidecars, (a, b) =>
                File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));

            foreach (var sidecar in sidecars)
            {
                try
                {
                    var json = File.ReadAllText(sidecar);
                    var entry = JsonSerializer.Deserialize(json, ImageCacheJsonContext.Default.ImageCacheEntry);
                    if (entry == null) continue;
                    if (string.IsNullOrEmpty(entry.LocalPath) || !File.Exists(entry.LocalPath))
                        continue;
                    if (string.IsNullOrEmpty(entry.Url)) continue;

                    InsertOrUpdate(entry);
                }
                catch
                {
                    // Corrupt sidecar — skip.
                }

                // Don't bring more than the in-memory cap into RAM. The
                // remaining sidecars stay on disk and will be hydrated on
                // demand if accessed.
                lock (_lruLock)
                {
                    if (_lru.Count >= InMemoryLruCapacity) break;
                }
            }
        }
        catch
        {
            // Hydration failure is non-fatal — start with an empty cache.
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Eviction (disk)
    // ────────────────────────────────────────────────────────────────────

    private void EvictStaleAndOversizedEntries()
    {
        try
        {
            if (!Directory.Exists(_cacheDir))
                return;

            var files = new DirectoryInfo(_cacheDir).GetFiles()
                .Where(f => !f.Name.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.LastAccessTimeUtc)
                .ToList();

            var cutoff = DateTime.UtcNow - MaxAge;

            // Phase 1: Delete files older than MaxAge (and their sidecars).
            for (var i = files.Count - 1; i >= 0; i--)
            {
                if (files[i].LastWriteTimeUtc < cutoff)
                {
                    DeleteEntryFiles(files[i]);
                    files.RemoveAt(i);
                }
            }

            // Phase 2: LRU eviction if total size exceeds limit.
            var totalSize = files.Sum(f => f.Length);
            var idx = 0;
            while (totalSize > MaxCacheSizeBytes && idx < files.Count)
            {
                totalSize -= files[idx].Length;
                DeleteEntryFiles(files[idx]);
                idx++;
            }
        }
        catch
        {
            // Non-critical cleanup
        }
    }

    private static void DeleteEntryFiles(FileInfo imageFile)
    {
        try
        {
            var sidecar = Path.ChangeExtension(imageFile.FullName, ".meta.json");
            if (File.Exists(sidecar)) File.Delete(sidecar);
            imageFile.Delete();
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // SVG / raster helpers
    // ────────────────────────────────────────────────────────────────────

    private static (byte[] Bytes, int NaturalWidth, int NaturalHeight)? ConvertSvgToPng(string svgContent)
    {
        try
        {
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
            if (read == 0) break;
            total += read;
            if (total > maxBytes)
                throw new InvalidOperationException("Remote image exceeds size limit.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static string ComputeUrlHash(string url)
    {
        var bytes = Encoding.UTF8.GetBytes(url);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }

    private static string GetImageExtension(string url)
    {
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
            _ => ".png"
        };
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
