namespace LucidReader.Models;

/// <summary>
/// The plain, Avalonia-free half of the add-feed and OPML flows: what the
/// pasted address turns into before autodiscovery sees it, and the wording of
/// every message the user reads afterwards. A Window cannot be constructed in
/// a unit test in this repo, so anything worth asserting on lives here rather
/// than in AddFeedDialog or MainWindow.Subscriptions (see AddFeedTests).
/// </summary>
public static class AddFeedInput
{
    /// <summary>
    /// Trims the pasted address and gives it an https scheme when, and only
    /// when, it declares no scheme at all.
    ///
    /// FeedAutodiscovery only follows http and https and returns an empty list
    /// for anything else, so a bare "xkcd.com" would otherwise come back as
    /// "no feeds found" when the real problem was a missing scheme. An address
    /// that already declares a scheme is left exactly as typed, including a
    /// deliberate plain-http one and including an unsupported one: prepending
    /// https to "ftp://example.com" produced "https://ftp://example.com",
    /// which fails to parse and is then reported as "no feeds found", which is
    /// wrong about the cause. Leaving it alone lets
    /// <see cref="DescribeAddressProblem"/> say what is actually wrong.
    ///
    /// A colon alone does not mean a scheme: "example.com:8080/feed" is a host
    /// and a port, so a colon followed only by digits is not treated as one.
    /// </summary>
    public static string Normalise(string? raw)
    {
        var input = (raw ?? string.Empty).Trim();
        if (input.Length == 0) return string.Empty;

        return HasScheme(input) ? input : "https://" + input;
    }

    private static bool HasScheme(string input)
    {
        var colon = input.IndexOf(':');
        if (colon <= 0) return false;

        // RFC 3986: scheme = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )
        if (!char.IsAsciiLetter(input[0])) return false;
        for (var i = 1; i < colon; i++)
        {
            var c = input[i];
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.')) return false;
        }

        // host:port, not scheme:path.
        var rest = input[(colon + 1)..];
        var digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits])) digits++;
        if (digits > 0 && (digits == rest.Length || rest[digits] is '/' or '?' or '#')) return false;

        return true;
    }

    /// <summary>
    /// Says what is wrong with an already-normalised address, or null when
    /// there is nothing wrong with it.
    ///
    /// Both refusals here match the policy SafeLinkOpener states for links in
    /// feed content, so the app applies one rule to addresses rather than a
    /// different one per entry point. Embedded credentials matter most: the
    /// address is stored in feeds.feed_url, replayed on every scheduler tick
    /// and shown in the dialog, so "https://user:token@internal.example/" is
    /// refused before any of that can happen.
    /// </summary>
    public static string? DescribeAddressProblem(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
            return "That does not look like a web address.";

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return UnsupportedSchemeMessage;

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return CredentialsMessage;

        return null;
    }

    public const string EmptyAddressMessage = "Enter the address of a feed or a website.";

    public const string UnsupportedSchemeMessage =
        "mylo can only read feeds over http or https.";

    public const string CredentialsMessage =
        "Remove the username and password from that address; mylo will not store credentials in a subscription.";

    public const string DiscoveryTimedOutMessage = "Timed out looking up that address.";

    public static string DescribeDiscovery(int count) => count switch
    {
        <= 0 => "No feeds found at that address.",
        1 => "Found one feed.",
        _ => $"Found {count} feeds. Choose the ones you want."
    };

    /// <summary>
    /// The result of an add. Skipped means the URL was already subscribed,
    /// which is a normal outcome and deliberately not an error; failed means
    /// the write itself did not go through, which is not, so the two are
    /// counted and reported separately rather than lumped together.
    ///
    /// firstProblem is the message from the first write that failed. A count
    /// on its own cannot tell a routine constraint violation from a genuine
    /// bug, which is the whole reason the failure reason is kept, so it is
    /// carried into the status line rather than discarded.
    /// </summary>
    public static string DescribeAdded(int added, int skipped, int failed = 0, string? firstProblem = null)
    {
        var message = $"Added {added} {(added == 1 ? "feed" : "feeds")}.";

        if (skipped > 0)
            message = message.TrimEnd('.') +
                      $", skipped {skipped} already subscribed.";

        if (failed > 0)
        {
            message += $" {failed} could not be added.";
            if (!string.IsNullOrWhiteSpace(firstProblem))
                message += $" First problem: {firstProblem.Trim()}";
        }

        return message;
    }

    /// <summary>
    /// failedUrls names the feeds that did not import. A bare count leaves the
    /// user with nothing to act on, so the first few addresses are listed and
    /// the rest are summarised, which keeps the line short on a big import.
    /// </summary>
    public static string DescribeImport(
        int foldersCreated,
        int feedsAdded,
        int feedsSkipped,
        int feedsFailed,
        IReadOnlyList<string>? failedUrls = null)
    {
        var message =
            $"Imported {feedsAdded} {(feedsAdded == 1 ? "feed" : "feeds")} " +
            $"into {foldersCreated} new {(foldersCreated == 1 ? "folder" : "folders")}.";

        if (feedsSkipped > 0)
            message = message.TrimEnd('.') + $", skipped {feedsSkipped} already subscribed.";

        if (feedsFailed > 0)
        {
            message += $" {feedsFailed} could not be imported.";

            if (failedUrls is { Count: > 0 })
            {
                const int shown = 3;
                var listed = string.Join(", ", failedUrls.Take(shown));
                message += $" Not imported: {listed}";
                message += failedUrls.Count > shown
                    ? $" and {failedUrls.Count - shown} more."
                    : ".";
            }
        }

        return message;
    }

    /// <summary>
    /// The refresh queue is bounded and coalescing, so a request to fetch
    /// straight away can be refused. Saying how many actually went in beats a
    /// promise of an immediate fetch that quietly did not happen for most of
    /// a large import.
    /// </summary>
    public static string DescribeQueued(int queued, int requested)
    {
        if (requested <= 0) return string.Empty;

        return queued == requested
            ? $" Queued {queued} for an immediate fetch."
            : $" Queued {queued} of {requested} for an immediate fetch.";
    }

    /// <summary>
    /// A subscription list is a few hundred kilobytes of XML at the outside.
    /// Reading a picked file straight into a string with no cap turns a file
    /// the user did not write into an OutOfMemoryException, so the size is
    /// checked first, the same bounded-read habit FeedAutodiscovery applies to
    /// a fetched page.
    /// </summary>
    public const long MaxOpmlBytes = 8 * 1024 * 1024;

    public static string OpmlTooLargeMessage(long bytes) =>
        $"That file is {bytes / (1024 * 1024)} MB. A subscription list is never that big, so it was not read.";
}
