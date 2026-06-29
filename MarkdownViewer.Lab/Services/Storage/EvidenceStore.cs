using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Mostlylucid.Ephemeral.Sqlite;

namespace MarkdownViewer.Lab.Services.Storage;

public sealed class EvidenceNotFoundException(ContentHash hash)
    : KeyNotFoundException($"Evidence hash {hash.ToHex()} not found in SQLite.")
{
    public ContentHash Hash { get; } = hash;
}

/// <param name="CacheSizeLimit">Forwarded to SqliteSingleWriterOptions.CacheSizeLimit.</param>
/// <param name="CacheDuration">Forwarded to SqliteSingleWriterOptions.DefaultCacheDuration; null uses the package default (5 min).</param>
public sealed record EvidenceStoreOptions(long CacheSizeLimit = 4096, TimeSpan? CacheDuration = null);

/// <param name="Hits">Cache hits since this store instance opened, from SqliteSingleWriter cache.hit:* signals.</param>
/// <param name="Misses">Cache misses since this store instance opened, from SqliteSingleWriter cache.miss:* signals.</param>
/// <param name="InFlightWrites">Count of writes the coordinator has accepted but not yet flushed, from GetWriteSnapshot().</param>
/// <param name="Evictions">Always 0 for this version: EphemeralLruCache evictions are not surfaced as signals by SqliteSingleWriter 2.6.4.</param>
public sealed record EvidenceStoreStats(long Hits, long Misses, int InFlightWrites, long Evictions);

public sealed class EvidenceStore : IAsyncDisposable
{
    private readonly SqliteSingleWriter _writer;

    private EvidenceStore(SqliteSingleWriter writer) { _writer = writer; }

    /// <summary>
    /// Opens (or re-opens) an EvidenceStore backed by a SQLite file at <paramref name="dbPath"/>.
    /// Schema is created idempotently via the single writer. WAL is enabled by default.
    /// </summary>
    public static async Task<EvidenceStore> OpenAsync(string dbPath, EvidenceStoreOptions opts,
        CancellationToken ct)
    {
        var connString = $"Data Source={dbPath};Mode=ReadWriteCreate";
        var writerOpts = new SqliteSingleWriterOptions
        {
            CacheSizeLimit = opts.CacheSizeLimit,
            EnableWriteAheadLogging = true,
        };
        if (opts.CacheDuration is { } d)
            writerOpts.DefaultCacheDuration = d;

        var writer = SqliteSingleWriter.GetOrCreate(connString, writerOpts);

        // Idempotent schema init. No parameters needed; pass empty dictionary for the AOT-safe overload.
        await writer.WriteAsync(
            """
            CREATE TABLE IF NOT EXISTS evidence (
                content_hash INTEGER PRIMARY KEY,
                text         TEXT NOT NULL
            );
            """,
            new Dictionary<string, object?>(),
            ct);

        return new EvidenceStore(writer);
    }

    /// <summary>
    /// Returns the stored text for <paramref name="hash"/>, or null if not found.
    /// Result is served from the LFU cache on subsequent calls.
    /// </summary>
    public Task<string?> GetAsync(ContentHash hash, CancellationToken ct) =>
        _writer.ReadAsync<string?>(
            cacheKey: CacheKey(hash),
            reader: async conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT text FROM evidence WHERE content_hash = @h LIMIT 1;";
                cmd.Parameters.AddWithValue("@h", (long)hash.Value);
                return await cmd.ExecuteScalarAsync(ct) as string;
            },
            ct: ct);

    /// <summary>
    /// Stores <paramref name="text"/> keyed by <paramref name="hash"/>, upsert semantics.
    /// Invalidates the LFU cache entry for this hash so the next GetAsync hits the DB.
    /// </summary>
    public Task PutAsync(ContentHash hash, string text, CancellationToken ct) =>
        _writer.WriteAndInvalidateAsync(
            "INSERT INTO evidence (content_hash, text) VALUES (@h, @t) " +
            "ON CONFLICT(content_hash) DO UPDATE SET text = excluded.text;",
            new Dictionary<string, object?> { ["h"] = (long)hash.Value, ["t"] = text },
            cacheKeysToInvalidate: new[] { CacheKey(hash) },
            ct);

    /// <summary>
    /// Drains the in-flight write queue, waiting at most <paramref name="deadline"/>.
    /// Swallows the deadline-only cancellation; propagates caller cancellation.
    /// </summary>
    public async Task DrainAsync(TimeSpan deadline, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(deadline);
        try
        {
            await _writer.FlushWritesAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Deadline fired; caller's token is still live.
        }
    }

    /// <summary>
    /// Returns observable stats. Hits/Misses come from SqliteSingleWriter's signal sink
    /// (signals named "cache.hit:{cacheKey}" / "cache.miss:{cacheKey}"). Evictions are
    /// not surfaced by the package; Evictions is always 0.
    /// </summary>
    public EvidenceStoreStats GetStats()
    {
        long hits = _writer.GetSignals("cache.hit:*").Count;
        long misses = _writer.GetSignals("cache.miss:*").Count;
        var snapshot = _writer.GetWriteSnapshot();
        return new EvidenceStoreStats(hits, misses, snapshot.Count, 0L);
    }

    private static string CacheKey(ContentHash hash) => "evidence:" + hash.ToHex();

    public async ValueTask DisposeAsync()
    {
        await DrainAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await _writer.DisposeAsync();
    }
}
