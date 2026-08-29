namespace LucidReader.Core.Feeds;

public interface IFeedParser
{
    /// <summary>
    /// A cheap look at the document to decide whether this parser should try.
    /// Never throws: a false return means "not mine", not "malformed".
    /// </summary>
    bool CanParse(string content);

    /// <summary>
    /// Parses, or throws FeedParseException when the document is unreadable.
    /// sourceUri resolves relative links.
    /// </summary>
    ParsedFeed Parse(string content, Uri sourceUri);
}

public sealed class FeedParseException(string message, Exception? inner = null)
    : Exception(message, inner);
