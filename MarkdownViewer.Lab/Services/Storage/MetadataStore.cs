using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace MarkdownViewer.Lab.Services.Storage;

public sealed class MetadataStore : IAsyncDisposable
{
    private readonly SqliteConnection _conn;

    private MetadataStore(SqliteConnection conn) => _conn = conn;

    public static async Task<MetadataStore> OpenAsync(string dbPath, CancellationToken ct)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = MetadataSchema.CreateTablesSql;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        return new MetadataStore(conn);
    }

    public async Task<long> UpsertDocumentAsync(DocumentRow row, CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO documents (path, mime_type, title, ingested_utc, source, state)
            VALUES ($p, $m, $t, $i, $s, $st)
            ON CONFLICT(path, source) DO UPDATE
                SET mime_type    = excluded.mime_type,
                    title        = excluded.title,
                    ingested_utc = excluded.ingested_utc,
                    state        = excluded.state
            RETURNING id;
        ";
        cmd.Parameters.AddWithValue("$p", row.Path);
        cmd.Parameters.AddWithValue("$m", row.MimeType);
        cmd.Parameters.AddWithValue("$t", row.Title);
        cmd.Parameters.AddWithValue("$i", row.IngestedUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$s", row.Source);
        cmd.Parameters.AddWithValue("$st", row.State.ToString());
        var id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return id;
    }

    public async Task<long> UpsertSegmentAsync(SegmentRow row, CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO segments (document_id, ordinal, content_hash, salience, created_utc, source)
            VALUES ($d, $o, $h, $sa, $c, $src)
            ON CONFLICT(content_hash) DO UPDATE
                SET salience = excluded.salience
            RETURNING id;
        ";
        cmd.Parameters.AddWithValue("$d", row.DocumentId);
        cmd.Parameters.AddWithValue("$o", row.Ordinal);
        cmd.Parameters.AddWithValue("$h", (long)row.ContentHash.Value);
        cmd.Parameters.AddWithValue("$sa", row.Salience);
        cmd.Parameters.AddWithValue("$c", row.CreatedUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$src", row.Source);
        var id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return id;
    }

    public async Task<SegmentRow?> GetSegmentByContentHashAsync(ContentHash h, CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, document_id, ordinal, content_hash, salience, created_utc, source
            FROM segments WHERE content_hash = $h LIMIT 1;
        ";
        cmd.Parameters.AddWithValue("$h", (long)h.Value);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadSegment(reader);
    }

    public async IAsyncEnumerable<SegmentRow> QuerySegmentsAsync(
        SegmentQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        if (q.IncludePersonal)
        {
            cmd.CommandText = @"
                SELECT id, document_id, ordinal, content_hash, salience, created_utc, source
                FROM segments;
            ";
        }
        else
        {
            cmd.CommandText = @"
                SELECT id, document_id, ordinal, content_hash, salience, created_utc, source
                FROM segments WHERE source NOT LIKE 'personal:%';
            ";
        }
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            yield return ReadSegment(reader);
    }

    private static SegmentRow ReadSegment(SqliteDataReader r) =>
        new(r.GetInt64(0), r.GetInt64(1), r.GetInt32(2),
            new ContentHash((ulong)r.GetInt64(3)),
            r.GetDouble(4),
            DateTimeOffset.Parse(r.GetString(5), CultureInfo.InvariantCulture),
            r.GetString(6));

    public async ValueTask DisposeAsync()
    {
        await _conn.CloseAsync();
        await _conn.DisposeAsync();
    }
}
