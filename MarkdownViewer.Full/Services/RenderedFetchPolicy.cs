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

    // SPA marker strings that indicate client-side rendering — Next.js, React,
    // Angular, Vue, and generic single-page-app roots.
    private static readonly string[] SpaMarkers =
    [
        "id=\"__next\"",
        "id='__next'",
        "window.__NEXT_DATA__",
        "__NEXT_DATA__",
        "data-reactroot",
        "ng-version=",
        "id=\"app\"",
        "id='app'",
        "<div id=\"root\">",
        "<div id='root'>",
    ];

    public static bool ShouldRetry(string firstPassHtml, string firstPassMarkdown)
    {
        if (string.IsNullOrWhiteSpace(firstPassMarkdown))
            return true;
        if (firstPassMarkdown.Length < MinMarkdownLength)
            return true;
        if (LooksLikeSpa(firstPassHtml))
            return true;
        return false;
    }

    private static bool LooksLikeSpa(string html)
    {
        if (string.IsNullOrEmpty(html))
            return false;

        foreach (var marker in SpaMarkers)
        {
            if (html.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
