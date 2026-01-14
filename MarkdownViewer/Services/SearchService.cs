using System.Text.RegularExpressions;

namespace MarkdownViewer.Services;

public class SearchService
{
    public List<SearchResult> Search(string content, string query, bool caseSensitive = false, bool wholeWord = false)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(content))
            return [];

        var results = new List<SearchResult>();
        var lines = content.Split('\n');

        var pattern = wholeWord ? $@"\b{Regex.Escape(query)}\b" : Regex.Escape(query);
        var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        var regex = new Regex(pattern, options);

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var matches = regex.Matches(line);

            foreach (Match match in matches)
                results.Add(new SearchResult
                {
                    Line = lineIndex,
                    Column = match.Index,
                    Length = match.Length,
                    Context = GetContext(line, match.Index, 40),
                    MatchText = match.Value
                });
        }

        return results;
    }

    private static string GetContext(string line, int matchIndex, int contextLength)
    {
        var start = Math.Max(0, matchIndex - contextLength);
        var end = Math.Min(line.Length, matchIndex + contextLength);

        var prefix = start > 0 ? "..." : "";
        var suffix = end < line.Length ? "..." : "";

        return prefix + line[start..end].Trim() + suffix;
    }
}

public class SearchResult
{
    public int Line { get; set; }
    public int Column { get; set; }
    public int Length { get; set; }
    public string Context { get; set; } = "";
    public string MatchText { get; set; } = "";
}