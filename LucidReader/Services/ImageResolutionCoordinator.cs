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
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _gate.Dispose();
    }
}
