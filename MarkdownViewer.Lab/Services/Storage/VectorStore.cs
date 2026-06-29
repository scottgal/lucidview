using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DuckDB.NET.Data;

namespace MarkdownViewer.Lab.Services.Storage;

/// <summary>
/// Persisted vector substrate using DuckDB + VSS (HNSW index).
/// Opens or creates a DuckDB file, installs/loads the vss extension,
/// enables experimental HNSW persistence, and manages a typed FLOAT[N]
/// column with cosine similarity search.
///
/// HNSW persistence note: DuckDB 1.5.0 requires the configuration
/// SET hnsw_enable_experimental_persistence=true before creating an HNSW
/// index on a file-based database. This is set during OpenAsync; the index
/// survives re-open provided the same setting is applied on reconnect.
/// </summary>
public sealed class VectorStore : IAsyncDisposable
{
    private readonly DuckDBConnection _conn;
    private readonly int _dim;

    private VectorStore(DuckDBConnection conn, int dim)
    {
        _conn = conn;
        _dim = dim;
    }

    public static async Task<VectorStore> OpenAsync(string dbPath, int dimensions, CancellationToken ct)
    {
        var conn = new DuckDBConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);

        // Install and load the VSS extension (no-op if already installed).
        await ExecuteNonQueryAsync(conn, "INSTALL vss;", ct);
        await ExecuteNonQueryAsync(conn, "LOAD vss;", ct);

        // Required for HNSW index persistence on file-based databases.
        await ExecuteNonQueryAsync(conn, "SET hnsw_enable_experimental_persistence=true;", ct);

        // DDL: table and HNSW index. FLOAT[N] creates a fixed-size typed array column.
        await ExecuteNonQueryAsync(conn, $@"
            CREATE TABLE IF NOT EXISTS segment_vectors (
                segment_id   BIGINT   PRIMARY KEY,
                content_hash UBIGINT  NOT NULL,
                embedding    FLOAT[{dimensions}]
            );", ct);

        // CREATE INDEX ... USING HNSW requires vss to be loaded and
        // hnsw_enable_experimental_persistence=true for on-disk databases.
        await ExecuteNonQueryAsync(conn, $@"
            CREATE INDEX IF NOT EXISTS idx_seg_vec_hnsw
                ON segment_vectors USING HNSW (embedding)
                WITH (metric = 'cosine');", ct);

        return new VectorStore(conn, dimensions);
    }

    public async Task UpsertAsync(
        long segmentId,
        ContentHash hash,
        ReadOnlyMemory<float> vector,
        CancellationToken ct)
    {
        if (vector.Length != _dim)
            throw new ArgumentException(
                $"Vector dimension {vector.Length} does not match store dimension {_dim}.",
                nameof(vector));

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO segment_vectors (segment_id, content_hash, embedding)
            VALUES ($id, $h, $v)
            ON CONFLICT (segment_id) DO UPDATE SET
                content_hash = excluded.content_hash,
                embedding    = excluded.embedding;";
        cmd.Parameters.Add(new DuckDBParameter("id", segmentId));
        cmd.Parameters.Add(new DuckDBParameter("h", (ulong)hash.Value));
        cmd.Parameters.Add(new DuckDBParameter("v", vector.ToArray()));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<(long SegmentId, ContentHash Hash, float Score)>>
        SearchAsync(ReadOnlyMemory<float> queryVector, int k, CancellationToken ct)
    {
        if (queryVector.Length != _dim)
            throw new ArgumentException(
                $"Query vector dimension {queryVector.Length} does not match store dimension {_dim}.",
                nameof(queryVector));

        using var cmd = _conn.CreateCommand();
        // The cast $q::FLOAT[N] must use a literal dimension — DuckDB's
        // array_cosine_similarity requires both sides to have matching fixed sizes.
        cmd.CommandText = $@"
            SELECT segment_id, content_hash,
                   array_cosine_similarity(embedding, $q::FLOAT[{_dim}]) AS score
            FROM   segment_vectors
            ORDER  BY score DESC
            LIMIT  $k;";
        cmd.Parameters.Add(new DuckDBParameter("q", queryVector.ToArray()));
        cmd.Parameters.Add(new DuckDBParameter("k", k));

        var results = new List<(long, ContentHash, float)>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var segId = reader.GetInt64(0);
            var hash  = new ContentHash(reader.GetFieldValue<ulong>(1));
            var score = reader.GetFloat(2);
            results.Add((segId, hash, score));
        }
        return results;
    }

    public async Task DeleteAsync(long segmentId, CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM segment_vectors WHERE segment_id = $id;";
        cmd.Parameters.Add(new DuckDBParameter("id", segmentId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _conn.CloseAsync();
        await _conn.DisposeAsync();
    }

    private static async Task ExecuteNonQueryAsync(
        DuckDBConnection conn, string sql, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
