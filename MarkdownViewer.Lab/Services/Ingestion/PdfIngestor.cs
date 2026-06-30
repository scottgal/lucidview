using System;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownViewer.Lab.Services.Ingestion;

/// <summary>
/// Ingestor stub for PDF files (<c>.pdf</c>).
///
/// <para>
/// <see cref="IngestAsync"/> throws <see cref="NotSupportedException"/> until
/// <c>Mostlylucid.Summarizers.Reader.Pdf</c> is published to nuget.org.
/// The typed exception ensures callers fail loudly rather than silently skipping PDFs.
/// </para>
/// </summary>
public sealed class PdfIngestor : IIngestor
{
    public const string NotSupportedMessage =
        "Reader pending upstream publish of `Mostlylucid.Summarizers.Reader.Pdf`; " +
        "see Task 10 upstream side-quest in lucidLAB plan.";

    public bool CanHandle(string path) =>
        path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<IngestedDocument> IngestAsync(string path, CancellationToken ct) =>
        throw new NotSupportedException(NotSupportedMessage);
}
