using System.Text.RegularExpressions;

namespace LucidReader.Models;

/// <summary>
/// Turns an article body into the short plain-text preview the item list shows
/// under each title, the way Mail previews a message.
///
/// This is a display heuristic, not a parser. Stored content is markdown, but a
/// feed summary is often raw HTML, so both get stripped. Getting an edge case
/// wrong costs a slightly odd preview line, nothing more.
/// </summary>
public static partial class Snippet
{
    public static string FromMarkdown(string? markdown, string? summary, int maxLength = 180)
    {
        var source = !string.IsNullOrWhiteSpace(markdown) ? markdown : summary;
        if (string.IsNullOrWhiteSpace(source)) return string.Empty;

        var text = source;

        // Fenced code contributes nothing readable to a preview.
        text = CodeFencePattern().Replace(text, " ");
        // Images first, so their alt text does not survive as link text.
        text = ImagePattern().Replace(text, " ");
        // Links keep their label and lose their target.
        text = LinkPattern().Replace(text, "$1");
        text = HtmlTagPattern().Replace(text, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = MarkupNoisePattern().Replace(text, " ");
        text = WhitespacePattern().Replace(text, " ").Trim();

        return Truncate(text, maxLength);
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;

        // Leave room for the ellipsis itself, so the truncated text plus
        // "..." does not overshoot maxLength (the naive "cut at maxLength,
        // then append '...'" approach can land 3 characters over).
        var limit = Math.Max(0, maxLength - 3);

        // Cut on a word boundary so the preview does not end mid-word.
        var cut = text.LastIndexOf(' ', Math.Min(limit, text.Length - 1));
        if (cut <= 0) cut = limit;

        return text[..cut].TrimEnd() + "...";
    }

    [GeneratedRegex(@"```.*?```|~~~.*?~~~", RegexOptions.Singleline)]
    private static partial Regex CodeFencePattern();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagPattern();

    /// <summary>
    /// Heading hashes, emphasis markers, blockquote markers, list bullets and
    /// inline code ticks. Deliberately not a markdown parser.
    /// </summary>
    [GeneratedRegex(@"^\s{0,3}#{1,6}\s*|^\s{0,3}>\s?|^\s{0,3}[-*+]\s+|\*{1,3}|_{1,3}|`", RegexOptions.Multiline)]
    private static partial Regex MarkupNoisePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
