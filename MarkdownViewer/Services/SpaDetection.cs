namespace MarkdownViewer.Services;

// Heuristic detector for client-side-rendered (SPA / SSR-with-hydration)
// pages. We can't run the JS that produces the real content, so when the
// markdown extraction comes back essentially empty AND the source HTML
// carries a known framework footprint, render a friendly stub instead of
// a blank page.
internal static class SpaDetection
{
    private static readonly string[] FrameworkMarkers =
    [
        "__NEXT_DATA__",            // Next.js
        "__NUXT__",                 // Nuxt
        "__INITIAL_STATE__",        // generic
        "__APOLLO_STATE__",         // Apollo Client
        "__REDUX_STATE__",          // Redux SSR
        "__REMIX_DATA__",           // Remix
        "data-reactroot",           // React 16 SSR
        "data-next-page",           // Next.js client hint
        "ng-version=",              // Angular
        "id=\"__sapper\"",           // Sapper
        "id=\"svelte\"",             // SvelteKit
    ];

    public static bool LooksLikeSpa(string html)
    {
        if (string.IsNullOrEmpty(html)) return false;
        foreach (var marker in FrameworkMarkers)
            if (html.Contains(marker, StringComparison.Ordinal))
                return true;
        return false;
    }

    public static string DetectFramework(string html)
    {
        if (html.Contains("__NEXT_DATA__", StringComparison.Ordinal)
            || html.Contains("data-next-page", StringComparison.Ordinal))
            return "Next.js";
        if (html.Contains("__NUXT__", StringComparison.Ordinal))
            return "Nuxt";
        if (html.Contains("__APOLLO_STATE__", StringComparison.Ordinal))
            return "Apollo Client (React)";
        if (html.Contains("ng-version=", StringComparison.Ordinal))
            return "Angular";
        if (html.Contains("__REMIX_DATA__", StringComparison.Ordinal))
            return "Remix";
        if (html.Contains("id=\"svelte\"", StringComparison.Ordinal))
            return "SvelteKit";
        if (html.Contains("data-reactroot", StringComparison.Ordinal))
            return "React (SSR)";
        return "a JavaScript framework";
    }

    public static string BuildStubMarkdown(string url, string framework, string? title = null)
    {
        var displayTitle = title ?? new Uri(url).Host;
        return $"""
            # {displayTitle}

            ## This page uses client-side rendering

            **lucidVIEW** is a markdown browser — it does not run a JavaScript
            engine by design (no Chromium, no V8). The page at:

            <{url}>

            ships its visible content via **{framework}**, which generates the
            DOM in the browser *after* the page loads. The raw HTML response
            contains the framework shell but not the article body, so there's
            nothing for our extractor to convert.

            ### What you can do

            - Open this page in your default browser to view it with JavaScript:
              <{url}>
            - Look for a print / text / lite version of the site (some news
              sites offer e.g. `text.npr.org`, `lite.cnn.com`).
            - If you're the site operator: a static SSR fallback (no hydration)
              or a server-side `Accept: text/markdown` response would render
              perfectly here.

            ### Want better extraction?

            The conversion engine is **StyloExtract** — a separate open-source
            project. Improvements happen there, not here:
            <https://github.com/scottgal/styloextract>

            """;
    }
}
