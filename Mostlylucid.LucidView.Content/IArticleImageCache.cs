namespace MarkdownViewer.Services;

/// <summary>
/// Downloads the images a converted article references and rewrites the
/// markdown to point at local copies, so an article read offline shows its
/// pictures.
///
/// This interface exists in Content rather than in the reader engine because
/// the implementation needs Avalonia, and the engine must not.
///
/// An implementation should be best effort: an image it cannot fetch should
/// be left as a remote URL, not turned into a broken link or an exception.
/// </summary>
public interface IArticleImageCache
{
    Task<string> RewriteAsync(string markdown, Uri? baseUri, CancellationToken ct = default);
}
