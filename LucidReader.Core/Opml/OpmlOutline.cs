namespace LucidReader.Core.Opml;

/// <summary>
/// One outline node from an OPML document. An outline with a FeedUrl is a
/// subscription; one without is a folder. Real exporters produce both, and
/// occasionally something that is neither, which is preserved rather than
/// discarded so import can report honestly on what it saw.
/// </summary>
public sealed record OpmlOutline(
    string Title,
    string? FeedUrl,
    string? SiteUrl,
    IReadOnlyList<OpmlOutline> Children);

public sealed class OpmlParseException(string message, Exception? inner = null)
    : Exception(message, inner);
