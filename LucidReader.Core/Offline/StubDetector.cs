using System.Text.RegularExpressions;

namespace LucidReader.Core.Offline;

/// <summary>
/// Decides whether feed-supplied content is the whole article or a teaser.
/// A heuristic, and wrong sometimes: being wrong costs one unnecessary page
/// fetch, or one article read as a summary with a retry button. Neither is
/// worth a heavier mechanism.
/// </summary>
public static partial class StubDetector
{
    /// <summary>
    /// Visible characters at or above which content is treated as a full
    /// article regardless of how it ends.
    /// </summary>
    public const int FullArticleThreshold = 1500;

    /// <summary>
    /// Below this, content is a stub whatever else it looks like.
    /// </summary>
    private const int ObviousStubThreshold = 400;

    public static bool IsStub(string? contentHtml)
    {
        if (string.IsNullOrWhiteSpace(contentHtml)) return true;

        var text = VisibleText(contentHtml);
        if (text.Length < ObviousStubThreshold) return true;
        if (text.Length >= FullArticleThreshold) return false;

        // In the middle band, the deciding factor is how it ends. A teaser
        // finishes by pointing somewhere else.
        var tail = text[^Math.Min(120, text.Length)..];
        return ReadMorePattern().IsMatch(tail);
    }

    private static string VisibleText(string html)
    {
        // Script and style content is never visible text in any rendering
        // context, so it is removed (element and content together) before
        // the general tag strip runs. Without this, a short teaser padded
        // with an embedded widget's inline script or stylesheet would be
        // long enough in raw characters to read as a full article.
        var withoutScriptsAndStyles = ScriptOrStylePattern().Replace(html, " ");
        var withoutTags = TagPattern().Replace(withoutScriptsAndStyles, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        return WhitespacePattern().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStylePattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    // Known gaps, accepted as part of this heuristic's error budget: an
    // article that legitimately ends mid-sentence with an ellipsis will
    // trip the "..." branch and read as a stub, and a teaser that ends with
    // something like "Continue at our site" or a bare arrow/glyph will not
    // match anything here and will read as a full article. Both are wrong
    // sometimes by design; see the class doc comment.
    [GeneratedRegex(
        @"(read\s+(the\s+)?(more|full|rest)|continue\s+reading|view\s+(the\s+)?(full\s+)?(article|post)|\[\s*\.\.\.\s*\]|\.\.\.\s*$)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ReadMorePattern();
}
