namespace MarkdownViewer.Models;

/// <summary>
///     Metadata extracted from markdown documents (custom tags like categories, date)
/// </summary>
public class DocumentMetadata
{
    /// <summary>
    ///     Categories/tags from &lt;!--category-- tag1, tag2 --&gt;
    /// </summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    ///     Publication date from &lt;datetime class="hidden"&gt;...&lt;/datetime&gt;
    /// </summary>
    public DateTime? PublicationDate { get; set; }

    /// <summary>
    ///     True if any metadata was found
    /// </summary>
    public bool HasMetadata => Categories.Count > 0 || PublicationDate.HasValue;
}