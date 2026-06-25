namespace MarkdownViewer.Services;

/// <summary>
/// Decides whether the first-pass static extraction should be retried via
/// Playwright. A retry is warranted when the HTML is a JavaScript SPA shell
/// or when the extraction returned too little usable text.
/// </summary>
internal static class RenderedFetchPolicy
{
    // Minimum markdown length we consider "extraction found real content".
    // Below this we assume the first-pass extraction failed.
    private const int MinMarkdownLength = 200;

    public static bool ShouldRetry(string firstPassHtml, string firstPassMarkdown)
    {
        if (string.IsNullOrWhiteSpace(firstPassMarkdown))
            return true;
        if (firstPassMarkdown.Length < MinMarkdownLength)
            return true;
        if (SpaDetection.LooksLikeSpa(firstPassHtml))
            return true;
        return false;
    }
}
