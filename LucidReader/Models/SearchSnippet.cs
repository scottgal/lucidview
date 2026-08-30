using LucidReader.Core.Storage;

namespace LucidReader.Models;

/// <summary>One word of a search snippet, and whether it is part of the match.</summary>
public sealed record SnippetWord(string Text, bool IsMatch);

/// <summary>
/// Turns the delimited snippet FTS5 produced (see <see cref="SearchHit"/>)
/// into something the item list can render: a plain string with the
/// delimiters removed, and a per-word list that says which words matched.
///
/// Word by word rather than run by run, because the list row wraps. A run
/// ("the compositor pipeline, end to") is one long piece of text that a
/// WrapPanel cannot break, so a two-line preview would clip mid-run; splitting
/// on whitespace lets the row wrap wherever it needs to and still mark the
/// matched words. The cost is that the space between two words is a layout
/// gap rather than a character, which is invisible at this size.
///
/// Plain class, no Avalonia types, so the parsing is unit tested directly.
/// </summary>
public static class SearchSnippet
{
    /// <summary>
    /// Splits the snippet into words, each flagged with whether it fell inside
    /// a match. A word straddling a delimiter (FTS5 marks whole tokens, but
    /// punctuation can attach to one) counts as a match, since it is the word
    /// the user is looking for.
    ///
    /// Unbalanced or absent delimiters are not an error: a snippet with no
    /// markers at all comes back as plain words, which is exactly what should
    /// happen if FTS5 ever returns an unmarked passage.
    /// </summary>
    public static IReadOnlyList<SnippetWord> ToWords(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet)) return Array.Empty<SnippetWord>();

        var words = new List<SnippetWord>();
        var current = new System.Text.StringBuilder();
        var inMatch = false;
        var wordHasMatch = false;

        void Flush()
        {
            if (current.Length > 0) words.Add(new SnippetWord(current.ToString(), wordHasMatch));
            current.Clear();
            wordHasMatch = false;
        }

        foreach (var c in snippet)
        {
            switch (c)
            {
                case SearchHit.MatchStart:
                    inMatch = true;
                    continue;
                case SearchHit.MatchEnd:
                    inMatch = false;
                    continue;
            }

            if (char.IsWhiteSpace(c)) { Flush(); continue; }

            current.Append(c);
            if (inMatch) wordHasMatch = true;
        }

        Flush();
        return words;
    }
}
