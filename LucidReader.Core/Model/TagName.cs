namespace LucidReader.Core.Model;

/// <summary>
/// The rules a tag name has to obey, in one plain class so every one of them
/// can be tested without a database and without a window.
///
/// The rules, and why each one is what it is:
///
/// 1. Leading and trailing whitespace is trimmed, and any internal run of
///    whitespace collapses to one space. " dot   net " and "dot net" are the
///    same tag. Without the collapse two tags could look identical in the
///    sidebar and be different rows in the database, which is the one failure
///    a user cannot diagnose or fix.
///
/// 2. Case is preserved for display but ignored for identity: typing "dotnet"
///    when "DotNet" already exists adds to the existing tag rather than
///    creating a second one, and the sidebar keeps showing "DotNet". The
///    first spelling to reach the database wins, which is what
///    TagRepository's SELECT ... COLLATE NOCASE already did before this class
///    existed.
///
///    <see cref="AreSame"/> deliberately folds ASCII letters only, because
///    that is exactly what SQLite's NOCASE collation does. A Unicode-aware
///    comparison here would disagree with the database: C# would call
///    "STRASSE" and "strasse" the same tag while SQLite's unique index and
///    every COLLATE NOCASE lookup treat them as two, and the app would show
///    one tag while storing two. Matching SQLite is the smaller surprise.
///
/// 3. A comma cannot appear in a tag. The article tag editor and the T
///    shortcut both accept a comma-separated list, so a comma inside a name
///    is not something that can be round-tripped; it is rejected on the way
///    in rather than silently split into two tags. Control characters are
///    rejected for the same reason: they are invisible, so a tag containing
///    one looks identical to one that does not.
///
///    Everything else is allowed. Spaces, dashes, slashes, "#", accents and
///    non-Latin scripts are all ordinary things to call a tag, and a
///    restrictive allowlist would only push people into transliterating.
///
/// 4. <see cref="MaxLength"/> characters after normalisation. The sidebar row
///    is 260px wide and a tag longer than this is ellipsised into something
///    the user cannot tell from its neighbours. Truncating silently would
///    produce exactly that collision, so an over-long name is rejected with
///    the limit named.
///
/// 5. An empty or whitespace-only name is not a tag and is dropped rather
///    than reported: it is what a trailing comma produces, and "a, b," is a
///    perfectly ordinary thing to type.
/// </summary>
public static class TagName
{
    public const int MaxLength = 32;

    /// <summary>
    /// The separator the comma-separated tag inputs use, and therefore the
    /// one character a tag name cannot contain.
    /// </summary>
    public const char Separator = ',';

    /// <summary>
    /// Normalises a name and says whether it is usable. <paramref name="error"/>
    /// is null when the name is fine, and otherwise carries wording meant to be
    /// shown to the user as-is.
    ///
    /// A blank input returns false with a null error, because it is not a
    /// mistake worth reporting: see rule 5 above.
    /// </summary>
    public static bool TryNormalise(string? raw, out string name, out string? error)
    {
        name = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(raw)) return false;

        var collapsed = CollapseWhitespace(raw);
        if (collapsed.Length == 0) return false;

        foreach (var c in collapsed)
        {
            if (c == Separator)
            {
                error = "A tag name cannot contain a comma.";
                return false;
            }

            // Whitespace has already been collapsed to plain spaces above, so
            // anything still classed as a control character here is genuinely
            // invisible junk rather than a tab the user typed.
            if (char.IsControl(c))
            {
                error = "A tag name cannot contain control characters.";
                return false;
            }
        }

        if (collapsed.Length > MaxLength)
        {
            error = $"A tag name can be at most {MaxLength} characters.";
            return false;
        }

        name = collapsed;
        return true;
    }

    /// <summary>
    /// Normalises or throws. For call sites that have already validated, or
    /// that genuinely cannot continue with a bad name (the repository's own
    /// writes).
    /// </summary>
    public static string Normalise(string? raw)
    {
        if (TryNormalise(raw, out var name, out var error)) return name;
        throw new ArgumentException(error ?? "A tag name cannot be blank.", nameof(raw));
    }

    /// <summary>
    /// Whether two names are the same tag. ASCII-only case folding, matching
    /// SQLite's NOCASE collation: see rule 2.
    /// </summary>
    public static bool AreSame(string? a, string? b)
    {
        if (a is null || b is null) return ReferenceEquals(a, b);
        if (a.Length != b.Length) return false;

        for (var i = 0; i < a.Length; i++)
            if (FoldAscii(a[i]) != FoldAscii(b[i]))
                return false;

        return true;
    }

    /// <summary>
    /// An equality comparer over <see cref="AreSame"/>, for the de-duplication
    /// the list parser and the UI both need.
    /// </summary>
    public static IEqualityComparer<string> Comparer { get; } = new AsciiCaseInsensitiveComparer();

    /// <summary>
    /// Parses the comma-separated form both tag inputs accept.
    ///
    /// Names come back normalised, in the order they were typed, with
    /// case-insensitive duplicates collapsed onto the first spelling: typing
    /// "dotnet, DotNet" is one tag, not two, and not an error. Anything
    /// rejected by <see cref="TryNormalise"/> for a stated reason is reported
    /// in <see cref="TagListParse.Errors"/> rather than dropped silently, so
    /// the caller can say what happened; blanks are dropped without comment.
    /// </summary>
    public static TagListParse ParseList(string? raw)
    {
        var names = new List<string>();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(raw)) return new TagListParse(names, errors);

        foreach (var part in raw.Split(Separator))
        {
            if (!TryNormalise(part, out var name, out var error))
            {
                if (error is not null && !errors.Contains(error)) errors.Add(error);
                continue;
            }

            if (names.Any(existing => AreSame(existing, name))) continue;
            names.Add(name);
        }

        return new TagListParse(names, errors);
    }

    private static string CollapseWhitespace(string raw)
    {
        var builder = new System.Text.StringBuilder(raw.Length);
        var pendingSpace = false;

        foreach (var c in raw)
        {
            if (char.IsWhiteSpace(c))
            {
                // Only becomes a space if something non-blank follows, which
                // is what trims both ends without a second pass.
                if (builder.Length > 0) pendingSpace = true;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static char FoldAscii(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;

    private sealed class AsciiCaseInsensitiveComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => AreSame(x, y);

        public int GetHashCode(string obj)
        {
            var hash = new HashCode();
            foreach (var c in obj) hash.Add(FoldAscii(c));
            return hash.ToHashCode();
        }
    }
}

/// <summary>
/// The result of parsing a comma-separated tag list: the usable names, and
/// one message per distinct reason something was refused.
/// </summary>
public sealed record TagListParse(IReadOnlyList<string> Names, IReadOnlyList<string> Errors);
