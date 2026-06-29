using System;

namespace MarkdownViewer.Lab.Services.Storage;

public sealed record DocumentRow(
    long Id, string Path, string MimeType, string Title,
    DateTimeOffset IngestedUtc, string Source, IngestionState State);

public sealed record SegmentRow(
    long Id, long DocumentId, int Ordinal, ContentHash ContentHash,
    double Salience, DateTimeOffset CreatedUtc, string Source);

public enum IngestionState { Pending, Indexed, Error }

public sealed record SegmentQuery(bool IncludePersonal = false);
