namespace LucidReader.Core.Feeds;

/// <summary>
/// Icon, social-card image and description read out of a page's &lt;head&gt;.
/// Every field is a URL/text taken straight from the page and validated by
/// SiteMetadataExtractor; nothing here has been fetched.
/// </summary>
public readonly record struct SiteMetadata(string? IconUrl, string? ImageUrl, string? Description);
