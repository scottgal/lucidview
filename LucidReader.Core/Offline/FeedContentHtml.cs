namespace LucidReader.Core.Offline;

/// <summary>
/// Prepares feed-supplied HTML for the markdown converter.
///
/// The converter is a page reader. It parses a document, cleans it, cuts it
/// into blocks and then CLASSIFIES those blocks, keeping the ones that look
/// like an article and dropping the ones that look like page furniture. That
/// is exactly right for the article page OfflineDownloader fetches, and it is
/// wrong for what a feed hands over, which is a bare fragment with no document
/// around it: with nothing marking where the article is, a fragment of any
/// shape can read as furniture, and a fragment that is mostly a list of links
/// reads as navigation every time.
///
/// Measured against alvinashcraft.com's content:encoded, which is a heading and
/// a long list of linked items: converted bare it produced ONE character.
/// Wrapped in a document with an article element around it, the same input
/// produced 23,915 characters of correct markdown. The wrapping is not a nicety
/// - without it, storing feed-supplied content means storing an empty article.
///
/// This applies to the path that was already there ("the feed gave us the whole
/// thing, convert what we have") as much as to the content:encoded body V9
/// added. That path was rare enough - it needs a summary over
/// StubDetector.FullArticleThreshold - that nobody had hit it with a
/// link-shaped body, and its test used a recording converter, so the fragment
/// problem stayed hidden.
/// </summary>
internal static class FeedContentHtml
{
    /// <summary>
    /// The HTML as a document the converter can classify. Already-complete
    /// documents are handed back untouched: a feed is free to put a whole page
    /// in content:encoded, and wrapping one in an article element would nest a
    /// document inside a body and leave the parser to sort out something it was
    /// never given a reason to sort out.
    ///
    /// The article element is what does the work. It is the strongest signal
    /// there is for "the content is in here", so a body wrapped in it is
    /// classified as an article rather than weighed against page furniture that
    /// is not present.
    /// </summary>
    public static string AsDocument(string html) =>
        LooksLikeDocument(html)
            ? html
            : "<!doctype html><html><body><article>" + html + "</article></body></html>";

    private static bool LooksLikeDocument(string html)
    {
        var start = html.AsSpan().TrimStart();
        return start.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
               || start.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }
}
