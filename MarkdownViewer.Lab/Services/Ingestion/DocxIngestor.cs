using System;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownViewer.Lab.Services.Ingestion;

/// <summary>
/// Ingestor stub for DOCX files (<c>.docx</c>).
///
/// <para>
/// <see cref="IngestAsync"/> throws <see cref="NotSupportedException"/> until
/// <c>Mostlylucid.Summarizers.Reader.Docx</c> is published to nuget.org.
/// The typed exception ensures callers fail loudly rather than silently skipping DOCX files.
/// </para>
/// </summary>
public sealed class DocxIngestor : IIngestor
{
    public const string NotSupportedMessage =
        "Reader pending upstream publish of `Mostlylucid.Summarizers.Reader.Docx`; " +
        "see Task 10 upstream side-quest in lucidLAB plan.";

    public bool CanHandle(string path) =>
        path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    public Task<IngestedDocument> IngestAsync(string path, CancellationToken ct) =>
        throw new NotSupportedException(NotSupportedMessage);
}
