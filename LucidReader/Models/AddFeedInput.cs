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
    /// Trims the pasted address and gives it an https scheme when it has none.
    ///
    /// FeedAutodiscovery only follows http and https and returns an empty list
    /// for anything else, so a bare "xkcd.com" would otherwise come back as
    /// "no feeds found" when the real problem was a missing scheme. An address
    /// that already declares http or https is left exactly as typed, including
    /// a deliberate plain-http one.
    /// </summary>
    public static string Normalise(string? raw)
    {
        var input = (raw ?? string.Empty).Trim();
        if (input.Length == 0) return string.Empty;

        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return input;

        return "https://" + input;
    }

    public const string EmptyAddressMessage = "Enter the address of a feed or a website.";

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
    /// </summary>
    public static string DescribeAdded(int added, int skipped, int failed = 0)
    {
        var message = $"Added {added} {(added == 1 ? "feed" : "feeds")}.";

        if (skipped > 0)
            message = message.TrimEnd('.') +
                      $", skipped {skipped} already subscribed.";

        if (failed > 0)
            message += $" {failed} could not be added.";

        return message;
    }

    public static string DescribeImport(int foldersCreated, int feedsAdded, int feedsSkipped, int feedsFailed)
    {
        var message =
            $"Imported {feedsAdded} {(feedsAdded == 1 ? "feed" : "feeds")} " +
            $"into {foldersCreated} new {(foldersCreated == 1 ? "folder" : "folders")}.";

        if (feedsSkipped > 0)
            message = message.TrimEnd('.') + $", skipped {feedsSkipped} already subscribed.";

        if (feedsFailed > 0)
            message += $" {feedsFailed} could not be imported.";

        return message;
    }
}
