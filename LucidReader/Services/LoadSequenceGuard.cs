namespace LucidReader.Services;

/// <summary>
/// Guards against a slow, earlier async load landing after a faster, later
/// one and overwriting its result. Call <see cref="Begin"/> synchronously at
/// the start of the operation to capture a ticket, then re-check
/// <see cref="IsCurrent"/> immediately before acting on the result: if a
/// newer <see cref="Begin"/> has happened in the meantime, the ticket is
/// stale and the result must be discarded rather than applied.
///
/// Plain, no-Avalonia class on purpose: it is the extracted decision logic
/// behind MainWindow.LoadItemsAsync's staleness guard, so it can be unit
/// tested directly instead of only through the UI harness.
/// </summary>
public sealed class LoadSequenceGuard
{
    private long _current;

    /// <summary>Starts a new operation and returns its ticket.</summary>
    public long Begin() => Interlocked.Increment(ref _current);

    /// <summary>True if no newer operation has started since this ticket was issued.</summary>
    public bool IsCurrent(long ticket) => Interlocked.Read(ref _current) == ticket;
}
