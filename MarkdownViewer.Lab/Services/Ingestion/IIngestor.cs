using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownViewer.Lab.Services.Ingestion;

/// <summary>
/// Reads a source file or URL into a list of raw text segments.
/// Each format has its own implementation; dispatch is by file extension.
/// </summary>
public interface IIngestor
{
    /// <summary>Returns true if this ingestor can handle <paramref name="path"/>.</summary>
    bool CanHandle(string path);

    /// <summary>
    /// Reads the file at <paramref name="path"/> and returns a structured document
    /// with raw text segments ready for embedding and indexing.
    /// </summary>
    Task<IngestedDocument> IngestAsync(string path, CancellationToken ct);
}

/// <summary>
/// A document produced by an <see cref="IIngestor"/> ready for downstream processing.
/// </summary>
public sealed record IngestedDocument(
    string Path,
    string MimeType,
    string Title,
    IReadOnlyList<RawSegment> Segments);

/// <summary>
/// A single text chunk within an <see cref="IngestedDocument"/>.
/// </summary>
public sealed record RawSegment(int Ordinal, string Text);
