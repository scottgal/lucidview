namespace MermaidSharp.Rendering;

public class SvgFilter
{
    public required string Id { get; init; }
    public required string Content { get; init; }

    public string ToXml() => $"<filter id=\"{System.Net.WebUtility.HtmlEncode(Id)}\">{Content}</filter>";
}
