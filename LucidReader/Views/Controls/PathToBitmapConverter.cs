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

    /// <summary>
    /// The bookkeeping, which used to live inline here as a Dictionary and a
    /// LinkedList. It moved into <see cref="BoundedLru{TValue}"/> so the one
    /// property that matters over a long session - the cap holds, and an
    /// evicted bitmap is genuinely unreachable so the collector can take it -
    /// could be asserted in a test. Neither could be, while the structures
    /// were welded to a type that cannot be constructed without a rendering
    /// platform. Nothing about the caching behaviour changed in the move; see
    /// BoundedLru for why eviction still deliberately does not dispose.
    /// </summary>
    private readonly BoundedLru<Bitmap> _cache = new(MaxCacheEntries);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;

        if (!File.Exists(path))
        {
            _cache.Remove(path);
            return null;
        }

        DateTime writeTimeUtc;
        try
        {
            writeTimeUtc = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception)
        {
            _cache.Remove(path);
            return null;
        }

        // A hit is only honoured when the file has not been rewritten since
        // it was decoded; a mismatch drops the entry inside TryGet and falls
        // through to a fresh decode here.
        if (_cache.TryGet(path, writeTimeUtc, out var cached)) return cached;

        try
        {
            var bitmap = new Bitmap(path);
            _cache.Add(path, bitmap, writeTimeUtc);
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
