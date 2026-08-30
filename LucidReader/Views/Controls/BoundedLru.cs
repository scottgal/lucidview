namespace LucidReader.Views.Controls;

/// <summary>
/// A fixed-capacity least-recently-used map from a path to a decoded value,
/// with the file's last-write time carried alongside so a stale entry can be
/// told from a live one.
///
/// Extracted out of <see cref="PathToBitmapConverter"/> rather than invented:
/// the bookkeeping was already exactly this, but it was tangled up with
/// Avalonia's Bitmap, which cannot be constructed without a platform. That
/// made the one property that actually matters - the cap holds, so the map
/// cannot grow without bound over a long session - impossible to assert
/// anywhere. It is asserted now, over plain objects, in
/// LucidReader.Core.Tests/Ui/BoundedLruTests.cs, along with the property that
/// follows from it and is the whole reason the cap exists: an evicted value
/// is genuinely unreachable afterwards, so the garbage collector can take it.
///
/// Eviction deliberately does NOT dispose anything, and this class holds no
/// disposal hook by which it could. See PathToBitmapConverter for why: a
/// disposed Bitmap still assigned as a live Image.Source is touched on the
/// next render pass, and the sidebar realises every feed row at once, so the
/// forty-ninth favicon disposed one that was still on screen. Dropping the
/// reference and letting the collector reclaim the native memory on its own
/// schedule is correct for every surface, which deterministic disposal was
/// not.
///
/// Thread ownership: none of this is synchronised. The one caller evaluates
/// bindings on the UI thread and nowhere else.
/// </summary>
public sealed class BoundedLru<TValue>(int capacity) where TValue : class
{
    private readonly Dictionary<string, LinkedListNode<Entry>> _byKey = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _order = new();
    private readonly int _capacity = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), "A bounded cache needs a positive capacity.");

    private readonly record struct Entry(string Key, TValue Value, DateTime StampUtc);

    public int Count => _order.Count;

    public int Capacity => _capacity;

    /// <summary>
    /// Returns the cached value when there is one whose stamp still matches.
    /// A stamp mismatch drops the entry and reports a miss, so the caller
    /// decodes afresh rather than resurrecting bytes that have since been
    /// overwritten at the same path.
    /// </summary>
    public bool TryGet(string key, DateTime stampUtc, out TValue? value)
    {
        value = null;
        if (!_byKey.TryGetValue(key, out var node)) return false;

        if (node.Value.StampUtc != stampUtc)
        {
            Evict(node);
            return false;
        }

        _order.Remove(node);
        _order.AddLast(node);
        value = node.Value.Value;
        return true;
    }

    /// <summary>
    /// Stores a value as the most recently used, evicting the least recently
    /// used ones until the capacity holds. Replacing an existing key drops
    /// the previous entry first, so one key can never occupy two slots.
    /// </summary>
    public void Add(string key, TValue value, DateTime stampUtc)
    {
        if (_byKey.TryGetValue(key, out var existing)) Evict(existing);

        _byKey[key] = _order.AddLast(new Entry(key, value, stampUtc));

        while (_order.Count > _capacity)
            Evict(_order.First!);
    }

    public void Remove(string key)
    {
        if (_byKey.TryGetValue(key, out var node)) Evict(node);
    }

    private void Evict(LinkedListNode<Entry> node)
    {
        _order.Remove(node);
        _byKey.Remove(node.Value.Key);
    }
}
