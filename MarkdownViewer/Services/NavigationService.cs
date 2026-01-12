using System.Text.RegularExpressions;

namespace MarkdownViewer.Services;

public partial class NavigationService
{
    public List<HeadingItem> ExtractHeadings(string markdown)
    {
        var headings = new List<HeadingItem>();
        var lines = markdown.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();

            // ATX-style headings: # Heading
            var match = AtxHeadingRegex().Match(line);
            if (match.Success)
            {
                var level = match.Groups[1].Value.Length;
                var text = match.Groups[2].Value.Trim();

                // Remove inline formatting
                text = CleanHeadingText(text);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    headings.Add(new HeadingItem
                    {
                        Level = level,
                        Text = text,
                        Line = i,
                        Slug = GenerateSlug(text)
                    });
                }
            }
        }

        return BuildHierarchy(headings);
    }

    private static string CleanHeadingText(string text)
    {
        // Remove markdown formatting
        text = BoldItalicRegex().Replace(text, "$1");
        text = LinkRegex().Replace(text, "$1");
        text = CodeRegex().Replace(text, "$1");
        text = ImageRegex().Replace(text, "$1");
        return text.Trim();
    }

    private static string GenerateSlug(string text)
    {
        // GitHub-style slug generation
        var slug = text.ToLowerInvariant();
        slug = SlugInvalidCharsRegex().Replace(slug, "");
        slug = SlugSpacesRegex().Replace(slug, "-");
        slug = slug.Trim('-');
        return slug;
    }

    private static List<HeadingItem> BuildHierarchy(List<HeadingItem> flat)
    {
        var root = new List<HeadingItem>();
        var stack = new Stack<HeadingItem>();

        foreach (var heading in flat)
        {
            while (stack.Count > 0 && stack.Peek().Level >= heading.Level)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                root.Add(heading);
            }
            else
            {
                stack.Peek().Children.Add(heading);
            }

            stack.Push(heading);
        }

        return root;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex AtxHeadingRegex();

    [GeneratedRegex(@"\*\*\*(.+?)\*\*\*|\*\*(.+?)\*\*|\*(.+?)\*|___(.+?)___|__(.+?)__|_(.+?)_")]
    private static partial Regex BoldItalicRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]+\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"[^\w\s-]")]
    private static partial Regex SlugInvalidCharsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SlugSpacesRegex();
}

public class HeadingItem
{
    public int Level { get; set; }
    public string Text { get; set; } = "";
    public int Line { get; set; }
    public string Slug { get; set; } = "";
    public List<HeadingItem> Children { get; set; } = [];

    public string DisplayText => new string(' ', (Level - 1) * 2) + Text;
    public double IndentMargin => (Level - 1) * 16;
}
