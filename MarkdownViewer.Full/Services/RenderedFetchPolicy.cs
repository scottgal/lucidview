namespace MarkdownViewer.Services;

/// <summary>
/// Decides whether the first-pass static extraction should be retried via
/// Playwright. A retry is warranted when the HTML is a JavaScript SPA shell
/// or when the extraction returned too little usable text.
/// </summary>
internal static class RenderedFetchPolicy
{
    // Minimum markdown length we consider "extraction found enough content"
    // to skip Playwright retry. Set conservatively — 500 chars (~75 words)
    // catches genuinely-empty extractions without forcing Playwright on every
    // article-size page. Bumping further hurts server-rendered sites where
    // the heuristic does fine work (mostlylucid.net, blogs, docs) — the
    // Playwright DOM has JS-executed bloat that confuses the inducer.
    // Internal-settable so unit tests with small fixtures can lower it.
    internal static int MinMarkdownLength { get; set; } = 500;

    // Block-count signal: many real article/aggregator pages have ≥3 blocks
    // (heading + a paragraph + at least one figure or list). Anything below
    // suggests the static fetch returned nav-only chrome and needs JS render.
    // Internal-settable for tests.
    internal static int MinBlockCount { get; set; } = 3;

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
