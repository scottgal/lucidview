using System.Text;

namespace LucidReader.Core.Offline;

/// <summary>
/// Removes the chrome a web page puts above its own article, from the
/// markdown a conversion produced, before that markdown is stored.
///
/// Every publisher wraps the article body in a masthead of some kind, and the
/// extractor cannot always tell that masthead from the first paragraph. What
/// survives conversion is a short run of lines at the very top that the
/// reading pane then shows underneath the headline it has already drawn
/// itself: the document title, the article's own H1 or H2 saying the same
/// thing a second time, and whatever punctuation was holding the byline row
/// together once the row was broken into separate lines.
///
/// The comparison against the item title is deliberately fuzzy. An equality
/// check does not survive contact with real pages, because a document title
/// is almost never just the article title: three articles captured from three
/// publishers gave
///
///   "... multi-widget sites (English)"
///   "... Half-Life 2: Episode 3 assets | The Verge"
///   "... Deep Space Station 23 - NASA Science"
///
/// and each carries a different site suffix behind a different separator.
/// Enumerating separators would be a losing game, so instead both strings are
/// reduced to letters, digits and single spaces and one is accepted as an
/// echo of the other when it is a whole-word prefix of it. The prefix has to
/// be long enough to be a title rather than a word, and the tail short enough
/// to be a site name rather than a sentence, which is what keeps a real
/// heading that happens to start with the title from being eaten.
///
/// The rules are all bounded to the first few blocks. A mid-article heading
/// repeating the title is the author's choice and is left alone; so is a
/// leading heading that is not the title; so is anything at all once a code
/// fence has opened. And if applying the rules would empty the body, nothing
/// is applied: a short item whose entire content is its own title is still
/// better than a blank reading pane.
/// </summary>
public static class ArticleMarkdownTidy
{
    /// <summary>
    /// How far down the document the rules reach. Big enough for the shapes
    /// seen in the wild (NASA's page puts a date and a "Downloads" heading
    /// between the document title and the article's own repeat of it), small
    /// enough that it cannot reach real prose in any article worth reading.
    /// </summary>
    private const int MaxLeadingBlocks = 6;

    /// <summary>
    /// The shorter string in a prefix comparison has to be at least this long
    /// before the prefix rule applies. "Introduction" as a title must not
    /// swallow a heading called "Introduction to sockets".
    /// </summary>
    private const int MinPrefixLength = 20;

    /// <summary>
    /// And the part the longer string adds has to be no longer than this. A
    /// site suffix is a few words; a subtitle that continues the sentence is
    /// not, and is real content.
    /// </summary>
    private const int MaxSuffixLength = 30;

    /// <summary>
    /// Characters a line can be made of and still be a leftover separator
    /// rather than content. A hyphen is in the set because "date // read
    /// time" rows are built out of them just as often as out of slashes; the
    /// horizontal-rule and setext-underline spellings are excluded separately
    /// below, since those mean something.
    /// </summary>
    private const string SeparatorChars = "/|-\u00b7\u2022\u2010\u2013\u2014";

    /// <summary>
    /// Longest a separator-only line can be. Real punctuation art ("*****",
    /// an ASCII divider) is content the author put there on purpose.
    /// </summary>
    private const int MaxSeparatorLength = 4;

    /// <summary>
    /// Returns the markdown with the leading page chrome removed, or the
    /// markdown unchanged when there was none to remove.
    /// </summary>
    public static string Clean(string? markdown, string? itemTitle)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return markdown ?? string.Empty;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var drop = new bool[lines.Length];
        var normalisedTitle = Normalise(itemTitle);

        var blocksSeen = 0;
        var i = 0;
        while (i < lines.Length && blocksSeen < MaxLeadingBlocks)
        {
            if (IsBlank(lines[i])) { i++; continue; }

            // A fence ends the leading region outright. Nothing inside a code
            // block is page chrome, and a line of slashes in a code sample is
            // exactly the sort of thing the separator rule would otherwise
            // find and delete.
            if (IsFence(lines[i])) break;

            var start = i;
            while (i < lines.Length && !IsBlank(lines[i]) && !IsFence(lines[i])) i++;
            blocksSeen++;

            // Only single-line blocks are candidates. A title echo and a
            // stray separator are each one line by construction; anything
            // longer is a paragraph.
            if (i - start != 1) continue;

            var line = lines[start].Trim();
            if (IsSeparatorOnly(line) || IsTitleEcho(line, normalisedTitle))
                drop[start] = true;
        }

        var lastDrop = Array.LastIndexOf(drop, true);
        if (lastDrop < 0) return markdown;

        var kept = Rebuild(lines, drop, lastDrop);

        // Never hand back an empty body. An item whose whole content was its
        // own title reads badly, but it reads.
        return kept.Any(line => !IsBlank(line)) ? string.Join("\n", kept) : markdown;
    }

    /// <summary>
    /// Reassembles the kept lines, closing up the blank lines the removals
    /// left behind. Only the region up to just past the last removal is
    /// touched: collapsing blank runs further down would reach into fenced
    /// code, where a blank line is part of the sample.
    /// </summary>
    private static List<string> Rebuild(string[] lines, bool[] drop, int lastDrop)
    {
        var kept = new List<string>(lines.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            if (drop[i]) continue;

            var blank = IsBlank(lines[i]);
            var inAffectedRegion = i <= lastDrop + 1;
            var wouldDoubleUp = kept.Count == 0 || IsBlank(kept[^1]);

            if (blank && inAffectedRegion && wouldDoubleUp) continue;

            kept.Add(lines[i]);
        }

        return kept;
    }

    /// <summary>
    /// True when the line is a heading or a plain paragraph saying the same
    /// thing as the item title.
    /// </summary>
    private static bool IsTitleEcho(string line, string normalisedTitle)
    {
        if (normalisedTitle.Length == 0) return false;

        var text = StripHeadingMarker(line);
        if (text is null) return false;

        return IsSameTitle(Normalise(text), normalisedTitle);
    }

    /// <summary>
    /// The text of a heading, or of a plain paragraph line, or null when the
    /// line is some other kind of markdown: a list item, a quote, a table
    /// row, an indented or fenced code line. Those carry structure, and
    /// removing one would leave the structure broken even if the words did
    /// match.
    /// </summary>
    private static string? StripHeadingMarker(string line)
    {
        var hashes = 0;
        while (hashes < line.Length && line[hashes] == '#') hashes++;

        if (hashes is > 0 and <= 6)
        {
            var rest = line[hashes..];
            // "#tag" is not a heading; a heading needs space after the hashes.
            return rest.Length > 0 && char.IsWhiteSpace(rest[0]) ? rest.Trim() : null;
        }

        if (hashes > 0) return null;

        return IsPlainParagraph(line) ? line : null;
    }

    private static bool IsPlainParagraph(string line)
    {
        if (line.Length == 0) return false;

        if (line[0] is '>' or '-' or '*' or '+' or '|' or '`' or '~' or '=' or ':') return false;

        // An ordered list item: "1." or "1)".
        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits])) digits++;
        if (digits > 0 && digits < line.Length && line[digits] is '.' or ')') return false;

        return true;
    }

    /// <summary>
    /// Whether two already-normalised titles name the same article. Equal, or
    /// one a whole-word prefix of the other with only a site suffix's worth of
    /// text behind it.
    /// </summary>
    private static bool IsSameTitle(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;

        var (shorter, longer) = a.Length < b.Length ? (a, b) : (b, a);

        if (shorter.Length < MinPrefixLength) return false;
        if (longer.Length - shorter.Length > MaxSuffixLength) return false;
        if (!longer.StartsWith(shorter, StringComparison.Ordinal)) return false;

        // Whole words only, so "deep space station 2" does not match
        // "deep space station 23".
        return longer[shorter.Length] == ' ';
    }

    /// <summary>
    /// Reduces a title to letters, digits and single spaces, lower-cased.
    /// Punctuation becomes a space rather than vanishing, so "Half-Life"
    /// and "Half Life" agree while "in-line" and "inline" are still allowed
    /// to differ from words either side of them.
    /// </summary>
    private static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    private static bool IsSeparatorOnly(string line)
    {
        if (line.Length == 0 || line.Length > MaxSeparatorLength) return false;

        // "---" is a horizontal rule, or the underline of a setext heading.
        // Either way it is markup with a meaning, not a leftover.
        if (line.Length >= 3 && line.All(ch => ch == '-')) return false;

        return line.All(ch => SeparatorChars.Contains(ch));
    }

    private static bool IsFence(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("```", StringComparison.Ordinal)
               || trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }

    private static bool IsBlank(string line) => line.Trim().Length == 0;
}
