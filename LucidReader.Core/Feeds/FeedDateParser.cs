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
        // RFC 822 as RSS specifies it, and the common variants that omit the
        // day name or use a two-digit year.
        "ddd, dd MMM yyyy HH:mm:ss zzz",
        "ddd, dd MMM yyyy HH:mm:ss K",
        "ddd, dd MMM yyyy HH:mm zzz",
        "ddd, dd MMM yyyy HH:mm K",
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

    public static DateTimeOffset? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();

        // "GMT" is not a zone designator DateTimeOffset understands, but it is
        // what most RSS feeds emit.
        var normalised = trimmed.EndsWith(" GMT", StringComparison.OrdinalIgnoreCase)
            ? string.Concat(trimmed.AsSpan(0, trimmed.Length - 4), " +0000")
            : trimmed;

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
}
