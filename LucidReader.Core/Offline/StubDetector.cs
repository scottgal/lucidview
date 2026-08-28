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
        var withoutTags = TagPattern().Replace(html, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        return WhitespacePattern().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(
        @"(read\s+(the\s+)?(more|full|rest)|continue\s+reading|view\s+(the\s+)?(full\s+)?(article|post)|\[\s*\.\.\.\s*\]|\.\.\.\s*$)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ReadMorePattern();
}
