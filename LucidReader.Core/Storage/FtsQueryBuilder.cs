using System.Text;

namespace LucidReader.Core.Storage;

/// <summary>
/// Turns whatever the user typed into an FTS5 MATCH expression.
///
/// Two properties matter here, and they pull against each other.
///
/// Safety: FTS5 has its own query syntax, and an unbalanced quote, a stray
/// parenthesis, a bare AND/OR/NOT/NEAR or a column filter (^, :) is a syntax
/// error that comes back as an exception, not as "no results". Search runs on
/// every keystroke, so half-typed junk reaches this method constantly. Every
/// term is therefore reduced to letters, digits and underscores - every other
/// character is treated as a term separator - and then wrapped in double
/// quotes as a phrase literal. After that reduction no FTS5 metacharacter can
/// survive inside a term, and a bareword keyword like OR is quoted, so it is
/// searched for rather than parsed.
///
/// As-you-type: the last term gets the FTS5 prefix operator, so "compos"
/// matches "compositor" while the user is still typing. Only the last term,
/// and only when the query does not end in a separator: a trailing space (or
/// full stop) means the user finished that word, so it is matched exactly.
/// That is the difference between "compos" (still typing, prefix) and
/// "compos " (finished, no prefix), and it stops a completed query silently
/// matching more than it says.
/// </summary>
public static class FtsQueryBuilder
{
    /// <summary>
    /// Returns an FTS5 MATCH expression, or null when the input holds no
    /// usable term at all (empty, whitespace, or pure punctuation). Null means
    /// "do not run a query", never "match everything".
    /// </summary>
    public static string? Build(string? query, bool prefixLastTerm = true)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var terms = Tokenize(query);
        if (terms.Count == 0) return null;

        // A query ending in a separator (space, comma, full stop) is a
        // finished word, so it is not extended into a prefix search.
        var lastCharIsSeparator = IsSeparator(query[^1]);

        var builder = new StringBuilder();
        for (var i = 0; i < terms.Count; i++)
        {
            if (i > 0) builder.Append(' ');
            builder.Append('"').Append(terms[i]).Append('"');

            var isLast = i == terms.Count - 1;
            if (isLast && prefixLastTerm && !lastCharIsSeparator) builder.Append('*');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits on anything that is not a letter, a digit or an underscore.
    /// Unicode letters (accented, Greek, CJK) count as letters, so this is a
    /// separator rule rather than an ASCII allow-list. "don't" becomes the two
    /// terms don and t, which is also how unicode61 tokenizes the indexed
    /// text, so the pair still matches the stored word.
    /// </summary>
    private static List<string> Tokenize(string query)
    {
        var terms = new List<string>();
        var current = new StringBuilder();

        foreach (var c in query)
        {
            if (IsSeparator(c))
            {
                if (current.Length > 0) { terms.Add(current.ToString()); current.Clear(); }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) terms.Add(current.ToString());
        return terms;
    }

    private static bool IsSeparator(char c) => !char.IsLetterOrDigit(c) && c != '_';
}
