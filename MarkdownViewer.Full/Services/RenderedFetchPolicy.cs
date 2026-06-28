namespace MarkdownViewer.Services;

/// <summary>
/// Decides whether the first-pass static extraction should be retried via
/// Playwright.
///
/// Old policy: fire Playwright on any of (empty markdown, thin markdown,
/// few blocks, SPA framework markers). The "thin markdown" and "few blocks"
/// triggers backfire on server-rendered sites where the static HTML carries
/// the real content but the heuristic happened to pick a smaller subtree:
/// Playwright's rendered DOM exposes hidden metadata / admin / debug
/// sub-elements that beat the actual article body on re-extraction
/// (MS Learn's YAML frontmatter, Notion's __PROPS__, every framework's
/// hydration scratch data). For server-rendered pages, "thin extraction"
/// means our extractor needs to do better — NOT that we should ask JS to
/// give us more DOM to sift through.
///
/// New policy: fire Playwright only when the static HTML is recognisably a
/// JavaScript framework shell (Next/Nuxt/Angular/React-SSR/etc.) AND the
/// static extraction produced essentially nothing. If the HTML is server-
/// rendered with substantial body bytes, keep what the extractor produced
/// and let the LLM repair path (if any) refine the template. Playwright is
/// for "the page has no content without JS" — never for "the page has
/// content but our extractor didn't find enough of it."
/// </summary>
internal static class RenderedFetchPolicy
{
    // Floor below which an extraction is treated as essentially-empty. Only
    // applies in combination with the SPA-marker check below.
    internal static int EmptyMarkdownThreshold { get; set; } = 200;

    public static bool ShouldRetry(string firstPassHtml, string firstPassMarkdown)
    {
        // Playwright fires only when BOTH:
        //  - the HTML carries a known SPA framework marker (the static body
        //    is a hydration shell, not the real content); AND
        //  - the static extraction produced essentially no text (so there's
        //    nothing to lose by re-fetching).
        // Sites without SPA markers are server-rendered: we keep the static
        // extraction and don't risk a Playwright DOM corrupting the result
        // with hidden metadata.
        if (!SpaDetection.LooksLikeSpa(firstPassHtml))
            return false;

        if (string.IsNullOrWhiteSpace(firstPassMarkdown))
            return true;
        if (firstPassMarkdown.Length < EmptyMarkdownThreshold)
            return true;
        return false;
    }

    public static bool ShouldRetry(string firstPassHtml, string firstPassMarkdown, int firstPassBlockCount)
        => ShouldRetry(firstPassHtml, firstPassMarkdown);
}
