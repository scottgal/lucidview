using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownViewer.Lab.Services.Ingestion;

/// <summary>
/// Ingestor for Markdown files (<c>.md</c> / <c>.markdown</c>).
///
/// <para>
/// Reads the file as UTF-8 text and delegates chunking to <see cref="SimpleSegmentSelector"/>.
/// No Markdig AST traversal is performed — raw text chunking is sufficient for the
/// embedding pipeline and avoids a dependency on the upstream reader package.
/// Replace with <c>Mostlylucid.Summarizers.Reader.Markdown.MarkdownReader</c> once that
/// package publishes to nuget.org.
/// </para>
/// </summary>
public sealed class MarkdownIngestor : IIngestor
{
    public bool CanHandle(string path) =>
        path.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".markdown", System.StringComparison.OrdinalIgnoreCase);

    public async Task<IngestedDocument> IngestAsync(string path, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(path, System.Text.Encoding.UTF8, ct);
        var segments = SimpleSegmentSelector.Chunk(text);
        var title = Path.GetFileNameWithoutExtension(path);
        return new IngestedDocument(path, "text/markdown", title, segments);
    }
}
