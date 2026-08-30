namespace LucidReader.Services;

/// <summary>
/// Coordinates background image resolution for one surface (sidebar icons,
/// item thumbnails, or the reading pane's single hero image) so that:
///
/// - a list reload cancels every resolution still in flight for rows it is
///   about to replace, rather than letting a slow favicon fetch land on a
///   row that no longer exists or has since scrolled away, and
/// - a large list does not open dozens of connections at once.
///
/// Mirrors the shape of <see cref="LoadSequenceGuard"/> and
/// <see cref="DwellCoordinator"/> deliberately, rather than inventing a
/// third cancellation mechanism: <see cref="StartBatch"/> cancels whatever
/// batch was previously running - so any resolve still awaiting the fetcher
/// observes cancellation and unwinds instead of assigning a stale row - and
/// hands back a fresh token that every resolution in the new batch shares.
///
/// Plain, no-Avalonia class on purpose, same rationale as its siblings: the
/// decision logic (what gets cancelled, how many run at once) is testable
/// without a Window.
/// </summary>
public sealed class ImageResolutionCoordinator(int maxConcurrency = 4) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(maxConcurrency, maxConcurrency);
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Cancels whatever batch was previously running and returns a token for
    /// a fresh one. Call once, synchronously, before starting the row-level
    /// resolutions for a newly loaded list (or a newly shown article).
    /// </summary>
    public CancellationToken StartBatch()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    /// <summary>Cancels the current batch without starting a new one (window close, e.g.).</summary>
    public void CancelPending()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Runs one row's resolution under the concurrency gate. Swallows
    /// OperationCanceledException so one cancelled batch member does not
    /// surface as an unobserved task exception; any other failure is the
    /// caller's problem (in practice ImageResolver itself never throws for
    /// an ordinary fetch failure, only for a hard cancellation).
    /// </summary>
    public async Task RunAsync(CancellationToken token, Func<CancellationToken, Task> work)
    {
        try
        {
            await _gate.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (token.IsCancellationRequested) return;
            await work(token);
        }
        catch (OperationCanceledException)
        {
            // Expected when StartBatch() supersedes this batch mid-flight.
        }
        finally
        {
            // Guarded because Dispose can run while this resolve is inside
            // work(). Cancellation does not actually stop the fetch - the
            // image cache's revalidate path does not take the token - so a
            // resolve can still be running long after the window closed, and
            // an ObjectDisposedException thrown from here would be caught by
            // neither catch above. These resolves are started as `_ = ...`,
            // so it would surface as an unobserved task exception: lost on
            // most hosts, fatal on one enabling ThrowUnobservedTaskExceptions.
            try { _gate.Release(); }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Cancels the current batch and releases the cancellation source only.
    ///
    /// The semaphore is deliberately left to the GC. Disposing it here raced
    /// every resolve still inside work(): SemaphoreSlim.Dispose does not wait
    /// for waiters or holders, so up to maxConcurrency in-flight resolves
    /// would reach their Release on a disposed semaphore. A SemaphoreSlim
    /// with no one waiting on its AvailableWaitHandle holds no unmanaged
    /// resource worth forcing that race for.
    ///
    /// _cts is nulled rather than left dangling so a StartBatch or
    /// CancelPending arriving after disposal cannot touch a disposed source.
    /// </summary>
    public void Dispose()
    {
        var cts = _cts;
        _cts = null;
        cts?.Cancel();
        cts?.Dispose();
    }
}
