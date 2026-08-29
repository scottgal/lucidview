namespace LucidReader.Services;

/// <summary>
/// Owns the single outstanding "mark read after a dwell" timer. There is at
/// most one pending dwell at a time: starting a new one cancels whatever was
/// pending, and every event that invalidates the current dwell (the item
/// list reloading, the user manually toggling read state, the window
/// closing) must call <see cref="CancelPending"/> rather than leaving it to
/// incidental selection-clearing behaviour.
///
/// Plain, no-Avalonia class on purpose: it is the extracted decision logic
/// behind the dwell rule, so cancellation semantics can be unit tested
/// directly instead of only through the UI harness and a manual stopwatch.
/// </summary>
public sealed class DwellCoordinator : IDisposable
{
    private CancellationTokenSource? _cts;

    /// <summary>True while a dwell timer is pending.</summary>
    public bool IsPending => _cts is not null;

    /// <summary>
    /// Cancels any pending dwell and starts a new one, returning its token.
    /// </summary>
    public CancellationToken StartNew()
    {
        CancelPending();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    /// <summary>
    /// Cancels the pending dwell, if any. Idempotent: safe to call when
    /// nothing is pending.
    /// </summary>
    public void CancelPending()
    {
        if (_cts is null) return;
        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
    }

    public void Dispose() => CancelPending();
}
