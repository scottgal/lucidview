using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace MarkdownViewer.Services;

/// <summary>
/// Disk-backed cache for rendered mermaid diagram PNGs.
/// Key = SHA256(mermaidCode + theme + isDark). Evicts entries older than 7 days.
/// </summary>
public class MermaidCacheService
{
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, string> _index = new(StringComparer.Ordinal);

    // Include Naiad assembly write time in cache keys so layout/rendering changes
    // automatically invalidate stale cached PNGs on rebuild.
    private static readonly string NaiadBuildStamp = GetNaiadBuildStamp();

    private static string GetNaiadBuildStamp()
    {
        try
        {
            var asm = typeof(MermaidSharp.Mermaid).Assembly;
            var location = asm.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
                return File.GetLastWriteTimeUtc(location).Ticks.ToString();
            return asm.GetName().Version?.ToString() ?? "0";
        }
        catch
        {
            return "0";
        }
    }

    public MermaidCacheService()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "lucidview-mermaid-cache");
        Directory.CreateDirectory(_cacheDir);
        LoadIndex();
        EvictStaleEntries(TimeSpan.FromDays(7));
    }

    /// <summary>
    /// Compute a stable cache key for a mermaid diagram + rendering context.
    /// Includes Naiad assembly version so layout changes invalidate cached PNGs.
    /// </summary>
    public static string ComputeKey(string mermaidCode, bool isDark, string textColor, string bgColor)
    {
        var raw = string.Concat(mermaidCode, "|", isDark ? "d" : "l", "|", textColor, "|", bgColor, "|v", NaiadBuildStamp);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Try to get a cached PNG path. Returns null on miss.
    /// </summary>
    public string? TryGet(string key)
    {
        if (_index.TryGetValue(key, out var path) && File.Exists(path))
            return path;

        _index.TryRemove(key, out _);
        return null;
    }

    /// <summary>
    /// Store a rendered PNG in the cache. Writes to disk.
    /// </summary>
    public string Put(string key, byte[] pngData)
    {
        var path = Path.Combine(_cacheDir, key + ".png");
        File.WriteAllBytes(path, pngData);
        _index[key] = path;
        return path;
    }

    /// <summary>
    /// Invalidate all cached entries (e.g. on theme change).
    /// Does NOT delete files — they'll be evicted on next startup.
    /// </summary>
    public void InvalidateAll()
    {
        _index.Clear();
    }

    private void LoadIndex()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_cacheDir, "*.png"))
            {
                var key = Path.GetFileNameWithoutExtension(file);
                _index[key] = file;
            }
        }
        catch
        {
            // Cache dir might not exist yet — that's fine
        }
    }

    private void EvictStaleEntries(TimeSpan maxAge)
    {
        try
        {
            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var file in Directory.EnumerateFiles(_cacheDir, "*.png"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    var key = Path.GetFileNameWithoutExtension(file);
                    _index.TryRemove(key, out _);
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch
        {
            // Non-critical cleanup
        }
    }
}
