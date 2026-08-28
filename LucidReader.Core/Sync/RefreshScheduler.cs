using LucidReader.Core.Storage;

namespace LucidReader.Core.Sync;

/// <summary>
/// A plain timer over one SQL query. Ephemeral's ScheduledTasks atom is not
/// used here on purpose: the whole scheduling rule is "next_due_utc has
/// passed", which the database answers better than a scheduler would.
/// </summary>
public sealed class RefreshScheduler(
    FeedRepository feeds,
    FeedRefreshService refresh,
    TimeProvider timeProvider,
    TimeSpan? tickInterval = null) : IAsyncDisposable
{
    private const int MaxFeedsPerTick = 200;

    private readonly TimeSpan _interval = tickInterval ?? TimeSpan.FromMinutes(1);
    private readonly CancellationTokenSource _stopping = new();
    private ITimer? _timer;

    public bool IsRunning => _timer is not null;

    public void Start()
    {
        if (_timer is not null) return;

        _timer = timeProvider.CreateTimer(
            _ => _ = TickSafelyAsync(),
            null,
            _interval,
            _interval);
    }

    public async Task StopAsync()
    {
        await _stopping.CancelAsync();
        if (_timer is not null)
        {
            await _timer.DisposeAsync();
            _timer = null;
        }
    }

    /// <summary>
    /// Queues every feed whose next_due_utc has passed. Returns how many were
    /// actually queued, which is fewer than were due when some are already in
    /// flight from a manual refresh.
    /// </summary>
    public async Task<int> TickAsync(CancellationToken ct = default)
    {
        var due = await feeds.GetDueAsync(timeProvider.GetUtcNow(), MaxFeedsPerTick, ct);

        var queued = 0;
        foreach (var feed in due)
            if (refresh.TryQueue(feed.Id))
                queued++;

        return queued;
    }

    private async Task TickSafelyAsync()
    {
        try
        {
            await TickAsync(_stopping.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception)
        {
            // A tick that throws must not kill the timer, or refreshing stops
            // silently for the rest of the session.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stopping.Dispose();
    }
}
