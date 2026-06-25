namespace MarkdownViewer.Services;

// Placeholder for Task 4. Delegates to lean's HtmlToMarkdownService verbatim so
// behaviour is identical until the StyloExtract Core pipeline lands.
public sealed class HtmlToMarkdownServiceFull : IHtmlToMarkdownService
{
    private readonly HtmlToMarkdownService _inner = new();

    public Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default)
        => _inner.ConvertAsync(html, sourceUri, ct);
}
