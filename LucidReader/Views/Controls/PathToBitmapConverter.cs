using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace LucidReader.Views.Controls;

/// <summary>
/// Binds a local file path string (FeedTreeNode.IconPath, ItemRow.ThumbnailPath,
/// MainWindow.HeroImagePath - all Task 8c) to an Image control's Source, which
/// is typed IImage, not string. Avalonia has no built-in string-to-Bitmap
/// conversion, so this is the one converter all three image surfaces share.
///
/// Deliberately fails soft: a missing or unreadable file (deleted from the
/// cache after the row already resolved, corrupt download, race with an
/// eviction) yields null - the Image renders nothing - rather than a binding
/// exception that would take the row down with it.
///
/// Holds a small bounded LRU of already-decoded bitmaps keyed by path, so a
/// reader (a scroll-heavy app: containers recycle onto the same rows
/// repeatedly as a virtualised list scrolls) does not re-read and re-decode
/// the same file from disk on every binding evaluation that revisits it.
///
/// Thread ownership: Avalonia evaluates bindings on the UI thread, and this
/// converter is only ever invoked from a binding evaluation, so the cache
/// below is touched exclusively from the UI thread and needs no locking.
/// If that ever changes (e.g. a future async binding pathway), this class
/// would need to add one.
///
/// Staleness: a cache hit is only honoured if the file at that path has the
/// same last-write time as when it was decoded. This guards against
/// resurrecting a stale image if ImageCacheService's own LRU evicts and a
/// later write reuses the same path for different bytes (its cache paths
/// are content-hash-derived per URL, so same-path reuse with different
/// content is unlikely in practice, but cheap to guard against regardless).
/// If the file at a cached path has since been deleted outright (evicted
/// with nothing written back), the stale entry is dropped and this returns
/// null rather than continuing to show the now-orphaned in-memory bitmap -
/// consistent with the "fails soft to nothing" behaviour above, not a
/// resurrection of old content.
/// </summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    public static readonly PathToBitmapConverter Instance = new();

    // Virtualisation already bounds how many rows are live at once, so this
    // only needs to cover the working set a scroll pass revisits, not the
    // whole list.
    private const int MaxCacheEntries = 48;

    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _byPath = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _lruOrder = new();

    private readonly record struct CacheEntry(string Path, Bitmap Bitmap, DateTime LastWriteTimeUtc);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;

        if (!File.Exists(path))
        {
            EvictIfPresent(path);
            return null;
        }

        DateTime writeTimeUtc;
        try
        {
            writeTimeUtc = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception)
        {
            EvictIfPresent(path);
            return null;
        }

        if (_byPath.TryGetValue(path, out var node))
        {
            if (node.Value.LastWriteTimeUtc == writeTimeUtc)
            {
                // Cache hit: move to the most-recently-used end and reuse
                // the already-decoded bitmap instead of touching disk again.
                _lruOrder.Remove(node);
                _lruOrder.AddLast(node);
                return node.Value.Bitmap;
            }

            // The file at this path changed since it was decoded - do not
            // resurrect the old bytes. Drop the stale entry and fall
            // through to a fresh decode below.
            EvictNode(node);
        }

        try
        {
            var bitmap = new Bitmap(path);
            var entry = new CacheEntry(path, bitmap, writeTimeUtc);
            var newNode = _lruOrder.AddLast(entry);
            _byPath[path] = newNode;

            while (_lruOrder.Count > MaxCacheEntries)
                EvictNode(_lruOrder.First!);

            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private void EvictIfPresent(string path)
    {
        if (_byPath.TryGetValue(path, out var node)) EvictNode(node);
    }

    /// <summary>
    /// Removes an entry from both structures. Deliberately does NOT dispose
    /// the bitmap.
    ///
    /// It used to. That was a rendering hazard, not a memory optimisation: a
    /// disposed Bitmap still assigned as a live Image.Source is touched on
    /// the next render pass. The item list and the reading pane's hero are
    /// virtualised, so their bindings are reassigned before an entry can be
    /// evicted, but the sidebar is a plain ItemsControl with every feed row
    /// realised at once. With 49 or more feeds carrying favicons - an
    /// ordinary subscription list - the 49th decode disposed the bitmap row
    /// one was still showing.
    ///
    /// Dropping the reference and letting the GC reclaim the native memory on
    /// its own schedule costs a little latency in returning it and is correct
    /// for every surface, which deterministic disposal was not.
    /// </summary>
    private void EvictNode(LinkedListNode<CacheEntry> node)
    {
        _lruOrder.Remove(node);
        _byPath.Remove(node.Value.Path);
    }
}
