using System.Text.RegularExpressions;

namespace MarkdownViewer.Controls.Editor;

/// <summary>
/// Parses markdown text into a list of typed <see cref="MarkdownBlock"/> descriptors.
/// Handles fenced code blocks (including mermaid), headings, images, blockquotes,
/// lists, horizontal rules, and catch-all paragraphs.
/// </summary>
public static partial class MarkdownBlockParser
{
    /// <summary>
    /// Split raw markdown into typed block descriptors. Each block carries its
    /// type and the raw markdown text that produced it.
    /// </summary>
    public static List<MarkdownBlock> Parse(string markdown)
    {
        var blocks = new List<MarkdownBlock>();
        if (string.IsNullOrEmpty(markdown))
        {
            blocks.Add(new MarkdownBlock(EditorBlockType.Paragraph, string.Empty));
            return blocks;
        }

        // Normalize line endings
        var text = markdown.Replace("\r\n", "\n").Replace("\r", "\n");

        var lines = text.Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            // Skip leading blank lines
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // Fenced code block (``` ... ```)
            if (line.TrimStart().StartsWith("```"))
            {
                var fenceResult = ConsumeFencedBlock(lines, i);
                blocks.Add(fenceResult.Block);
                i = fenceResult.NextIndex;
                continue;
            }

            // Heading (# ... ###### ...)
            var headingMatch = HeadingRegex.Match(line);
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                var content = headingMatch.Groups[2].Value.Trim();
                blocks.Add(new MarkdownBlock(EditorBlockType.Heading, line.TrimEnd(), content, level));
                i++;
                // Consume trailing blank lines
                while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
                continue;
            }

            // Horizontal rule (---, ***, ___ on its own line)
            if (HorizontalRuleRegex.IsMatch(line))
            {
                blocks.Add(new MarkdownBlock(EditorBlockType.HorizontalRule, line.TrimEnd()));
                i++;
                continue;
            }

            // Image-only line (![alt](url))
            if (ImageOnlyRegex.IsMatch(line.Trim()))
            {
                var imgMatch = ImageRegex.Match(line.Trim());
                var alt = imgMatch.Groups[1].Value;
                var url = imgMatch.Groups[2].Value;
                blocks.Add(new MarkdownBlock(EditorBlockType.Image, line.TrimEnd(), alt, url: url));
                i++;
                continue;
            }

            // Blockquote (> ...)
            if (line.TrimStart().StartsWith('>'))
            {
                var (quoteLines, nextIdx) = ConsumeBlockquote(lines, i);
                blocks.Add(new MarkdownBlock(EditorBlockType.Blockquote, string.Join("\n", quoteLines)));
                i = nextIdx;
                continue;
            }

            // Unordered list item (- or *)
            if (UnorderedListItemRegex.IsMatch(line))
            {
                var (listLines, nextIdx, ordered) = ConsumeList(lines, i);
                blocks.Add(new MarkdownBlock(
                    ordered ? EditorBlockType.OrderedList : EditorBlockType.UnorderedList,
                    string.Join("\n", listLines)));
                i = nextIdx;
                continue;
            }

            // Ordered list item (1. )
            if (OrderedListItemRegex.IsMatch(line))
            {
                var (listLines, nextIdx, _) = ConsumeList(lines, i);
                blocks.Add(new MarkdownBlock(EditorBlockType.OrderedList, string.Join("\n", listLines)));
                i = nextIdx;
                continue;
            }

            // Default: paragraph — consume until blank line or next block-level token
            var (paraLines, paraNext) = ConsumeParagraph(lines, i);
            var paraText = string.Join("\n", paraLines).Trim();
            if (!string.IsNullOrEmpty(paraText))
                blocks.Add(new MarkdownBlock(EditorBlockType.Paragraph, paraText));
            i = paraNext;
        }

        if (blocks.Count == 0)
            blocks.Add(new MarkdownBlock(EditorBlockType.Paragraph, string.Empty));

        return blocks;
    }

    /// <summary>
    /// Serialize a list of blocks back to canonical markdown text.
    /// </summary>
    public static string ToMarkdown(IReadOnlyList<MarkdownBlock> blocks)
    {
        var parts = new List<string>();
        foreach (var block in blocks)
        {
            var md = block.RawMarkdown;
            if (string.IsNullOrWhiteSpace(md) && block.Type != EditorBlockType.Paragraph)
                continue;
            parts.Add(md);
        }
        return string.Join("\n\n", parts).Trim();
    }

    // ─── Block consumers ───

    private static (MarkdownBlock Block, int NextIndex) ConsumeFencedBlock(string[] lines, int start)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(lines[start]);
        var fenceMarker = lines[start].TrimStart();
        var language = fenceMarker.Length > 3 ? fenceMarker[3..].Trim() : string.Empty;

        var i = start + 1;
        while (i < lines.Length)
        {
            sb.AppendLine(lines[i]);
            if (lines[i].TrimStart().StartsWith("```"))
            {
                i++;
                break;
            }
            i++;
        }

        var raw = sb.ToString().TrimEnd();

        // Mermaid blocks
        if (language.Equals("mermaid", StringComparison.OrdinalIgnoreCase))
        {
            var code = ExtractFenceContent(raw);
            return (new MarkdownBlock(EditorBlockType.Mermaid, raw, code, language: language), i);
        }

        // Regular code block
        var codeContent = ExtractFenceContent(raw);
        return (new MarkdownBlock(EditorBlockType.Code, raw, codeContent, language: language), i);
    }

    private static string ExtractFenceContent(string fenceBlock)
    {
        // Remove opening and closing ```
        var lines = fenceBlock.Split('\n');
        if (lines.Length < 2) return string.Empty;
        var inner = lines[1..]; // skip opening ```
        // Remove trailing ```
        if (inner.Length > 0 && inner[^1].TrimStart().StartsWith("```"))
            inner = inner[..^1];
        return string.Join("\n", inner).TrimEnd();
    }

    private static (List<string> Lines, int NextIndex) ConsumeBlockquote(string[] lines, int start)
    {
        var result = new List<string>();
        var i = start;
        while (i < lines.Length)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith('>'))
            {
                result.Add(lines[i]);
                i++;
            }
            else if (string.IsNullOrWhiteSpace(lines[i]))
            {
                // Blank line — peek ahead: if next line is also a quote, include the blank
                if (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith('>'))
                {
                    result.Add(lines[i]);
                    i++;
                }
                else
                {
                    i++;
                    break;
                }
            }
            else
            {
                break;
            }
        }
        // Consume trailing blank lines
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        return (result, i);
    }

    private static (List<string> Lines, int NextIndex, bool Ordered) ConsumeList(string[] lines, int start)
    {
        var result = new List<string>();
        var firstLine = lines[start].TrimStart();
        var ordered = OrderedListItemRegex.IsMatch(firstLine);
        var i = start;

        while (i < lines.Length)
        {
            var trimmed = lines[i].TrimStart();
            if (ordered && OrderedListItemRegex.IsMatch(trimmed))
            {
                result.Add(lines[i]);
                i++;
            }
            else if (!ordered && UnorderedListItemRegex.IsMatch(trimmed))
            {
                result.Add(lines[i]);
                i++;
            }
            else if (string.IsNullOrWhiteSpace(lines[i]))
            {
                // Blank line — peek ahead for more list items
                if (i + 1 < lines.Length)
                {
                    var nextTrimmed = lines[i + 1].TrimStart();
                    if ((ordered && OrderedListItemRegex.IsMatch(nextTrimmed)) ||
                        (!ordered && UnorderedListItemRegex.IsMatch(nextTrimmed)))
                    {
                        result.Add(lines[i]);
                        i++;
                        continue;
                    }
                }
                i++;
                break;
            }
            else
            {
                break;
            }
        }
        // Consume trailing blank lines
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        return (result, i, ordered);
    }

    private static (List<string> Lines, int NextIndex) ConsumeParagraph(string[] lines, int start)
    {
        var result = new List<string>();
        var i = start;
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Stop at block-level tokens
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                break;
            }
            if (trimmed.StartsWith("```")) break;
            if (trimmed.StartsWith('>')) break;
            if (HeadingRegex.IsMatch(line)) break;
            if (HorizontalRuleRegex.IsMatch(trimmed)) break;
            if (UnorderedListItemRegex.IsMatch(trimmed)) break;
            if (OrderedListItemRegex.IsMatch(trimmed)) break;
            if (ImageOnlyRegex.IsMatch(trimmed)) break;

            result.Add(line);
            i++;
        }
        // Consume trailing blank lines
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        return (result, i);
    }

    // ─── Regex patterns ───

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled)]
    private static partial Regex HeadingRegex { get; }

    [GeneratedRegex(@"^[-*_]{3,}\s*$", RegexOptions.Compiled)]
    private static partial Regex HorizontalRuleRegex { get; }

    [GeneratedRegex(@"^!\[([^\]]*)\]\(([^)]+)\)\s*$", RegexOptions.Compiled)]
    private static partial Regex ImageOnlyRegex { get; }

    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex ImageRegex { get; }

    [GeneratedRegex(@"^[\-\*]\s+", RegexOptions.Compiled)]
    private static partial Regex UnorderedListItemRegex { get; }

    [GeneratedRegex(@"^\d+\.\s+", RegexOptions.Compiled)]
    private static partial Regex OrderedListItemRegex { get; }
}

/// <summary>
/// Describes a single parsed markdown block — its type and the raw text.
/// </summary>
public class MarkdownBlock
{
    public EditorBlockType Type { get; }
    public string RawMarkdown { get; set; }

    /// <summary>Inner content (without markers). For headings: text after #. For code: body text.</summary>
    public string? Content { get; set; }

    /// <summary>Heading level (1-6). Only valid for Heading blocks.</summary>
    public int HeadingLevel { get; set; }

    /// <summary>Image URL. Only valid for Image blocks.</summary>
    public string? Url { get; set; }

    /// <summary>Code fence language tag. Only valid for Code/Mermaid blocks.</summary>
    public string? Language { get; set; }

    public MarkdownBlock(EditorBlockType type, string rawMarkdown, string? content = null, int headingLevel = 1, string? url = null, string? language = null)
    {
        Type = type;
        RawMarkdown = rawMarkdown;
        Content = content;
        HeadingLevel = headingLevel;
        Url = url;
        Language = language;
    }
}
