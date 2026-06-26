namespace MarkdownViewer.Services;

/// <summary>
/// Decides whether the first-pass static extraction should be retried via
/// Playwright. A retry is warranted when the HTML is a JavaScript SPA shell
/// or when the extraction returned too little usable text.
/// </summary>
internal static class RenderedFetchPolicy
{
    // Minimum markdown length we consider "extraction found enough content".
    // 200 was too lenient — any single substantive block on a JS-rendered news
    // aggregator (BBC News, NYT, theGuardian) passed it, hiding the carousel
    // content from extraction. 5000 chars (~750 words) is a reasonable floor
    // for "a real article or aggregator page worth of content".
    // Internal-settable so unit tests with small fixtures can lower it.
    internal static int MinMarkdownLength { get; set; } = 5000;

    // Aggregator pages tend to have lots of short blocks; if the static fetch
    // produced too few blocks we assume the headlines are JS-rendered and
    // retry under Playwright. Internal-settable for tests.
    internal static int MinBlockCount { get; set; } = 10;

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

    public static bool ShouldRetry(string firstPassHtml, string firstPassMarkdown, int firstPassBlockCount)
    {
        if (ShouldRetry(firstPassHtml, firstPassMarkdown))
            return true;
        // Sparse-blocks signal: many real article/aggregator pages have ≥10
        // content blocks once JS finishes; static fetch returning fewer is a
        // strong tell the JS-rendered headlines are missing.
        if (firstPassBlockCount < MinBlockCount)
            return true;
        return false;
    }
}
