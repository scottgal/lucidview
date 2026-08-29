namespace MarkdownViewer.Services;

public interface IHtmlToMarkdownService
{
    Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default);
}
