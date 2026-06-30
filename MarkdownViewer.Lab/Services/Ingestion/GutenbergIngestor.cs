using System;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownViewer.Lab.Services.Ingestion;

/// <summary>
/// Ingestor stub for Project Gutenberg plain-text files (<c>.txt</c> / Gutenberg URLs).
///
/// <para>
/// <see cref="IngestAsync"/> throws <see cref="NotSupportedException"/> until
/// <c>Mostlylucid.Summarizers.Reader.Gutenberg</c> is published to nuget.org.
/// The typed exception ensures callers fail loudly rather than silently skipping Gutenberg texts.
/// </para>
/// </summary>
public sealed class GutenbergIngestor : IIngestor
{
    public const string NotSupportedMessage =
        "Reader pending upstream publish of `Mostlylucid.Summarizers.Reader.Gutenberg`; " +
        "see Task 10 upstream side-quest in lucidLAB plan.";

    public bool CanHandle(string path) =>
        path.EndsWith(".gutenberg.txt", StringComparison.OrdinalIgnoreCase) ||
        (path.StartsWith("https://www.gutenberg.org", StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith("https://gutenberg.org", StringComparison.OrdinalIgnoreCase));

    public Task<IngestedDocument> IngestAsync(string path, CancellationToken ct) =>
        throw new NotSupportedException(NotSupportedMessage);
}
