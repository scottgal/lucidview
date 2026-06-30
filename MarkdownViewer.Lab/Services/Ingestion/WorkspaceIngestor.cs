using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarkdownViewer.Lab.Services.Ml;
using MarkdownViewer.Lab.Services.Storage;
using Microsoft.Extensions.Logging;

namespace MarkdownViewer.Lab.Services.Ingestion;

/// <summary>
/// Orchestrates document ingestion into a <see cref="Workspace"/>.
///
/// <para>
/// Flow per document:
/// 1. Dispatch to the matching <see cref="IIngestor"/> by file extension.
/// 2. Upsert a <c>DocumentRow</c> (state=Pending).
/// 3. For each <see cref="RawSegment"/>: compute <see cref="ContentHash"/>,
///    skip if already present (idempotency), embed via <see cref="IOnnxEmbedder"/>,
///    extract entities via <see cref="INerExtractor"/>, then fan-out writes to all
///    four substrates (Metadata, Evidence, Vectors, Lexical).
/// 4. Flush Lexical + Evidence, then mark the document Indexed.
/// </para>
///
/// <para>
/// Any substrate write failure propagates immediately — no silent fallback.
/// </para>
/// </summary>
public sealed class WorkspaceIngestor
{
    private readonly Workspace _ws;
    private readonly IOnnxEmbedder _embedder;
    private readonly INerExtractor _ner;
    private readonly IReadOnlyList<IIngestor> _ingestors;
    private readonly ILogger<WorkspaceIngestor> _log;

    public WorkspaceIngestor(
        Workspace ws,
        IOnnxEmbedder embedder,
        INerExtractor ner,
        IEnumerable<IIngestor> ingestors,
        ILogger<WorkspaceIngestor> log)
    {
        _ws        = ws;
        _embedder  = embedder;
        _ner       = ner;
        _ingestors = ingestors.ToArray();
        _log       = log;
    }

    /// <summary>
    /// Ingests the file at <paramref name="pathOrUrl"/> into the workspace,
    /// tagging every segment with <paramref name="source"/>.
    ///
    /// <para>
    /// Segments with <paramref name="source"/> prefixed with <c>"personal:"</c> are
    /// filtered out of default retrieval by <see cref="MetadataStore.QuerySegmentsAsync"/>
    /// (Task 4 contract); the ingestor simply writes the source tag as given.
    /// </para>
    /// </summary>
    public async Task IngestAsync(string pathOrUrl, string source, CancellationToken ct)
    {
        var ingestor = _ingestors.FirstOrDefault(i => i.CanHandle(pathOrUrl))
            ?? throw new InvalidOperationException(
                $"No ingestor registered for '{pathOrUrl}'. " +
                $"Registered: [{string.Join(", ", _ingestors.Select(i => i.GetType().Name))}].");

        _log.LogInformation("Ingesting '{Path}' via {Ingestor}", pathOrUrl, ingestor.GetType().Name);

        var doc = await ingestor.IngestAsync(pathOrUrl, ct);

        // Upsert document record as Pending.
        var docId = await _ws.Metadata.UpsertDocumentAsync(
            new DocumentRow(0, doc.Path, doc.MimeType, doc.Title,
                DateTimeOffset.UtcNow, source, IngestionState.Pending),
            ct);

        int written = 0;
        int skipped = 0;

        for (int i = 0; i < doc.Segments.Count; i++)
        {
            var raw  = doc.Segments[i];
            var hash = ContentHash.Compute(raw.Text);

            // Idempotency: skip segments whose content hash is already indexed.
            var existing = await _ws.Metadata.GetSegmentByContentHashAsync(hash, ct);
            if (existing is not null)
            {
                skipped++;
                continue;
            }

            // Embed and extract entities.
            var vector   = _embedder.Embed(raw.Text);
            var entities = _ner.Extract(raw.Text);

            // Fan-out writes: any throw propagates — no silent fallback.
            var segId = await _ws.Metadata.UpsertSegmentAsync(
                new SegmentRow(0, docId, raw.Ordinal, hash,
                    Salience: 0.5, DateTimeOffset.UtcNow, source),
                ct);

            await _ws.Evidence.PutAsync(hash, raw.Text, ct);
            await _ws.Vectors.UpsertAsync(segId, hash, vector, ct);
            await _ws.Lexical.IndexAsync(segId, hash, raw.Text, ct);

            written++;
        }

        // Flush the write-behind stores.
        await _ws.Lexical.CommitAsync(ct);
        await _ws.Evidence.DrainAsync(TimeSpan.FromSeconds(5), ct);

        // Mark document Indexed.
        await _ws.Metadata.UpsertDocumentAsync(
            new DocumentRow(docId, doc.Path, doc.MimeType, doc.Title,
                DateTimeOffset.UtcNow, source, IngestionState.Indexed),
            ct);

        _log.LogInformation(
            "Ingested '{Path}': {Written} segments written, {Skipped} skipped (idempotent).",
            pathOrUrl, written, skipped);
    }
}
