using System.Globalization;

namespace LucidReader.Core.Feeds;

/// <summary>
/// Feed dates are unreliable. A date we cannot read is null, never an
/// exception: an unparseable timestamp costs the item its sort position, and
/// nothing more. The item itself is still worth showing.
/// </summary>
public static class FeedDateParser
{
    private static readonly string[] Formats =
    [
        // The day name, when present, is stripped by TryParse before we get
        // here (see StripLeadingWeekday), so none of these formats need a
        // "ddd" specifier: we never trust the weekday, only the date.
        "dd MMM yyyy HH:mm:ss zzz",
        "dd MMM yyyy HH:mm:ss K",
        "dd MMM yyyy HH:mm zzz",
        // ISO 8601, which plenty of RSS feeds use in a pubDate regardless of
        // what the specification says.
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.fffK",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd"
    ];

    private static readonly string[] WeekdayNames =
    [
        "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun",
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"
    ];

    public static DateTimeOffset? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();

        // "GMT" is not a zone designator DateTimeOffset understands, but it is
        // what most RSS feeds emit.
        var normalised = trimmed.EndsWith(" GMT", StringComparison.OrdinalIgnoreCase)
            ? string.Concat(trimmed.AsSpan(0, trimmed.Length - 4), " +0000")
            : trimmed;

        normalised = StripLeadingWeekday(normalised);

        if (DateTimeOffset.TryParseExact(
                normalised, Formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var exact))
            return exact;

        if (DateTimeOffset.TryParse(
                normalised, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var loose))
            return loose;

        return null;
    }

    /// <summary>
    /// Removes a leading "Weekday, " token, whether or not it names the
    /// correct day. The weekday is redundant with the date that follows it,
    /// and feed generators get it wrong routinely (usually through
    /// timezone-naive weekday arithmetic), so we never validate it, only
    /// the date underneath it.
    ///
    /// This is deliberately narrow: it only strips a token that is one of
    /// the recognised English weekday names immediately followed by a
    /// comma. Anything else (a comma-terminated token that is not a
    /// weekday, or no comma at all) is left untouched, so garbage input
    /// still falls through to TryParseExact/TryParse and returns null
    /// rather than being silently reinterpreted.
    /// </summary>
    private static string StripLeadingWeekday(string value)
    {
        var commaIndex = value.IndexOf(',');
        if (commaIndex < 0) return value;

        var candidate = value.AsSpan(0, commaIndex).Trim();
        foreach (var day in WeekdayNames)
        {
            if (candidate.Equals(day, StringComparison.OrdinalIgnoreCase))
                return value[(commaIndex + 1)..].TrimStart();
        }

        return value;
    }
}
