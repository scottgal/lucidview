using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MarkdownViewer.Lab.Services.Ingestion;

/// <summary>
/// Statistics snapshot for a running <see cref="FolderWatcher"/>.
/// </summary>
/// <param name="Depth">Current number of paths queued in the bounded channel.</param>
/// <param name="Dropped">Total paths dropped because the channel was full.</param>
/// <param name="Processed">Total paths successfully processed by the consumer.</param>
public sealed record IngestionChannelStats(int Depth, long Dropped, long Processed);

/// <summary>
/// Watches a folder for new or changed files and routes matching paths to
/// <see cref="WorkspaceIngestor"/> via a bounded
/// <see cref="Channel{T}"/> with drop-newest backpressure.
///
/// <para>
/// Backpressure contract: when the channel is full, the newly-arrived event
/// path is dropped and <see cref="GetStats"/><c>.Dropped</c> increments.  A
/// trace-level log entry names the dropped path.  The watcher's OS thread is
/// never blocked.
/// </para>
///
/// <para>
/// OS-level <see cref="InternalBufferOverflowException"/> events (when the
/// kernel's file-event queue overflows) are caught and logged; ingestion
/// continues.
/// </para>
/// </summary>
public sealed class FolderWatcher : IAsyncDisposable
{
    private readonly WorkspaceIngestor _ingestor;
    private readonly string _folder;
    private readonly string _includeGlob;
    private readonly string _excludeGlob;
    private readonly Channel<string> _channel;
    private readonly int _channelCapacity;
    private readonly ILogger<FolderWatcher> _log;

    private readonly CancellationTokenSource _stopCts = new();
    private FileSystemWatcher? _fsw;
    private Task? _consumer;

    private long _dropped;
    private long _processed;

    // Debounce map: path → last-seen tick.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _lastSeen = new();
    private const int DebounceMs = 250;

    public FolderWatcher(
        WorkspaceIngestor ingestor,
        string folder,
        string includeGlob,
        string excludeGlob,
        int channelCapacity,
        ILogger<FolderWatcher> log)
    {
        _ingestor        = ingestor;
        _folder          = folder;
        _includeGlob     = includeGlob;
        _excludeGlob     = excludeGlob;
        _channelCapacity = channelCapacity;
        _log             = log;

        // DropWrite: when the channel is full, the newly-arriving event is
        // dropped (not an already-queued item).  TryWrite returns false so we
        // can increment _dropped and emit a trace.  This is the "bounded
        // ingestion channel, drop with trace, not silent" contract from the spec.
        // The spec calls this "drop-newest" by convention (drop the new arrival)
        // which maps to DropWrite in System.Threading.Channels nomenclature.
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode       = BoundedChannelFullMode.DropWrite,
            SingleWriter   = false,
            SingleReader   = true,
        });
    }

    /// <summary>Returns a live snapshot of channel depth, drop count, and processed count.</summary>
    public IngestionChannelStats GetStats() =>
        new(_channel.Reader.Count,
            Interlocked.Read(ref _dropped),
            Interlocked.Read(ref _processed));

    /// <summary>
    /// Starts the <see cref="FileSystemWatcher"/> and the consumer task.
    /// Can only be called once.
    /// </summary>
    public Task StartAsync(CancellationToken ct)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopCts.Token);

        // Set up the FileSystemWatcher.
        _fsw = new FileSystemWatcher(_folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };

        // Wire events.
        _fsw.Created += OnFileEvent;
        _fsw.Changed += OnFileEvent;

        // Handle OS buffer overflow gracefully.
        _fsw.Error += (_, e) =>
        {
            var ex = e.GetException();
            if (ex is InternalBufferOverflowException)
                _log.LogWarning(ex, "FileSystemWatcher internal buffer overflow on {Folder}", _folder);
            else
                _log.LogError(ex, "FileSystemWatcher error on {Folder}", _folder);
        };

        _fsw.EnableRaisingEvents = true;

        // Start consumer task.
        _consumer = Task.Run(() => ConsumeAsync(linked.Token), linked.Token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Directly attempts to enqueue <paramref name="path"/> into the channel.
    /// Exposed for unit tests that need to simulate file events without relying on
    /// <see cref="FileSystemWatcher"/> event timing.
    /// </summary>
    internal bool TryEnqueue(string path)
    {
        return TryEnqueueInternal(path);
    }

    public async ValueTask DisposeAsync()
    {
        _stopCts.Cancel();
        _fsw?.Dispose();
        _channel.Writer.TryComplete();

        if (_consumer is not null)
        {
            try { await _consumer.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _stopCts.Dispose();
    }

    // ── Private ───────────────────────────────────────────────────────────

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        var path = e.FullPath;

        // Include-glob filter.
        if (!MatchesGlob(path, _includeGlob)) return;

        // Exclude-glob filter.
        if (!string.IsNullOrEmpty(_excludeGlob) && MatchesGlob(path, _excludeGlob))
        {
            _log.LogTrace("FolderWatcher: excluded {Path}", path);
            return;
        }

        // Debounce: skip if same path fired within DebounceMs.
        var now = Environment.TickCount64;
        var last = _lastSeen.GetOrAdd(path, 0L);
        if (now - last < DebounceMs)
        {
            _log.LogTrace("FolderWatcher: debounced {Path}", path);
            return;
        }
        _lastSeen[path] = now;

        TryEnqueueInternal(path);
    }

    /// <summary>
    /// Writes <paramref name="path"/> to the bounded channel if capacity is
    /// available; otherwise increments <see cref="_dropped"/> and traces.
    /// </summary>
    /// <remarks>
    /// All <see cref="BoundedChannelFullMode"/> variants return <c>true</c>
    /// from <c>TryWrite</c> (they silently drop inside the channel). To
    /// faithfully count drops we check <see cref="Channel{T}.Reader.Count"/>
    /// before writing using the channel capacity stored at construction time.
    /// </remarks>
    private bool TryEnqueueInternal(string path)
    {
        // Fast path: if the reader count is below capacity, write is definitely safe.
        // If at or above, we have lost the race — count the write as a drop.
        // Note: BoundedChannelFullMode.DropWrite still returns true from TryWrite,
        // so we must gate on the count ourselves.
        if (_channel.Reader.Count >= _channelCapacity)
        {
            Interlocked.Increment(ref _dropped);
            _log.LogTrace("FolderWatcher: channel full ({Count}/{Cap}), dropped {Path}",
                _channel.Reader.Count, _channelCapacity, path);
            return false;
        }

        _channel.Writer.TryWrite(path);
        return true;
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var path in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                _log.LogDebug("FolderWatcher: ingesting {Path}", path);
                await _ingestor.IngestAsync(path, source: "folder", ct).ConfigureAwait(false);
                Interlocked.Increment(ref _processed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log but keep the consumer alive — a single bad file should
                // not kill the watcher for the entire session.
                _log.LogError(ex, "FolderWatcher: ingestion failed for {Path}", path);
            }
        }
    }

    /// <summary>
    /// Converts a shell-style glob pattern to a <see cref="Regex"/> and tests
    /// the file name (not the full path) against it.
    /// </summary>
    private static bool MatchesGlob(string fullPath, string glob)
    {
        if (string.IsNullOrEmpty(glob)) return false;
        var name = Path.GetFileName(fullPath);
        var pattern = "^" + Regex.Escape(glob)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";
        return Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase);
    }
}
