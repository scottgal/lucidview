namespace LucidReader.Core.Opml;

/// <summary>
/// One outline node from an OPML document. An outline with a FeedUrl is a
/// subscription; one without is a folder. Real exporters produce both, and
/// occasionally something that is neither, which is preserved rather than
/// discarded so import can report honestly on what it saw.
/// </summary>
/// <param name="TitleOverride">
/// The name the user gave this feed, when it is theirs rather than the
/// publisher's. Carried in its own lucidTitleOverride attribute so a round
/// trip through OPML keeps a rename: the standard text attribute has to hold
/// the displayed name for other readers' sake, and importing that into
/// feeds.title would put a user's name in the publisher-owned column, where
/// the first successful refresh overwrites it. Null on anything exported by
/// another reader, which is exactly right - there is no override to restore.
/// </param>
public sealed record OpmlOutline(
    string Title,
    string? FeedUrl,
    string? SiteUrl,
    IReadOnlyList<OpmlOutline> Children,
    string? TitleOverride = null);

public sealed class OpmlParseException(string message, Exception? inner = null)
    : Exception(message, inner);
