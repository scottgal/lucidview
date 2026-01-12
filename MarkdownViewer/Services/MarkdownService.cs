using System.Text.RegularExpressions;
using MarkdownViewer.Models;
using Naiad;

namespace MarkdownViewer.Services;

public partial class MarkdownService
{
    private string? _basePath;
    private string? _baseUrl;
    private readonly string _tempDir;

    public MarkdownService()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lucidview-mermaid");
        Directory.CreateDirectory(_tempDir);
    }

    public void SetBasePath(string? path)
    {
        _basePath = path;
        _baseUrl = null;
    }

    public void SetBaseUrl(string? url)
    {
        _baseUrl = url?.TrimEnd('/');
        _basePath = null;
    }

    /// <summary>
    /// Extract metadata from markdown content (categories, publication date)
    /// </summary>
    public DocumentMetadata ExtractMetadata(string content)
    {
        var metadata = new DocumentMetadata();

        // Extract categories: <!--category-- ASP.NET, PostgreSQL, Search -->
        var categoryMatch = CategoryRegex().Match(content);
        if (categoryMatch.Success)
        {
            var categoriesStr = categoryMatch.Groups[1].Value;
            metadata.Categories = categoriesStr
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        // Extract publication date: <datetime class="hidden">2026-01-14T12:00</datetime>
        var dateMatch = DatetimeRegex().Match(content);
        if (dateMatch.Success && DateTime.TryParse(dateMatch.Groups[1].Value, out var pubDate))
        {
            metadata.PublicationDate = pubDate;
        }

        return metadata;
    }

    public string ProcessMarkdown(string content)
    {
        // Remove metadata tags from rendered content (they'll be shown separately)
        content = CategoryRegex().Replace(content, "");
        content = DatetimeRegex().Replace(content, "");

        // Fix bold links: **[text](url)** -> [**text**](url)
        // Some markdown parsers don't handle bold wrapping links well
        content = BoldLinkRegex().Replace(content, "[**$1**]($2)");

        // Process relative image paths
        content = ProcessImagePaths(content);

        // Process mermaid code blocks (placeholder for now)
        content = ProcessMermaidBlocks(content);

        return content.Trim();
    }

    private string ProcessImagePaths(string content)
    {
        var imageRegex = ImageRegex();

        return imageRegex.Replace(content, match =>
        {
            var alt = match.Groups[1].Value;
            var path = match.Groups[2].Value;

            // Already absolute URL - leave as-is
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            // Already absolute file path - leave as-is
            if (Path.IsPathRooted(path))
            {
                return match.Value;
            }

            // Resolve relative path
            string resolvedPath;

            if (!string.IsNullOrEmpty(_baseUrl))
            {
                // URL-based resolution
                var cleanPath = path.TrimStart('.', '/');
                resolvedPath = $"{_baseUrl}/{cleanPath}";
            }
            else if (!string.IsNullOrEmpty(_basePath))
            {
                // File-based resolution
                resolvedPath = Path.GetFullPath(Path.Combine(_basePath, path));
                // Convert to file URI for Avalonia
                resolvedPath = new Uri(resolvedPath).AbsoluteUri;
            }
            else
            {
                return match.Value;
            }

            return $"![{alt}]({resolvedPath})";
        });
    }

    private int _mermaidCounter;

    private string ProcessMermaidBlocks(string content)
    {
        _mermaidCounter = 0;
        var mermaidRegex = MermaidBlockRegex();
        var matches = mermaidRegex.Matches(content);

        foreach (Match match in matches)
        {
            var mermaidCode = match.Groups[1].Value.Trim();
            string replacement;

            try
            {
                // Render mermaid to SVG using Naiad
                var svg = Mermaid.Render(mermaidCode);

                // Save SVG to temp file
                var svgPath = Path.Combine(_tempDir, $"diagram_{_mermaidCounter++}.svg");
                File.WriteAllText(svgPath, svg);

                // Return as image reference
                var fileUri = new Uri(svgPath).AbsoluteUri;
                replacement = $"![Mermaid Diagram]({fileUri})";
            }
            catch (Exception ex)
            {
                // Detect unsupported diagram types
                var diagramType = DetectMermaidDiagramType(mermaidCode);
                var errorMessage = ex.Message.Contains("Unsupported") || ex.Message.Contains("parse")
                    ? $"Naiad cannot yet render '{diagramType}' diagrams"
                    : ex.Message;

                // On error, show syntax-highlighted mermaid code with warning
                replacement = $"""
                    > ⚠️ **Naiad cannot yet render '{diagramType}' diagrams**

                    ```mermaid
                    {mermaidCode}
                    ```
                    """;
            }

            content = content.Replace(match.Value, replacement);
        }

        return content;
    }

    private static string DetectMermaidDiagramType(string code)
    {
        var firstLine = code.Split('\n').FirstOrDefault()?.Trim().ToLowerInvariant() ?? "";
        return firstLine switch
        {
            var s when s.StartsWith("flowchart") => "flowchart",
            var s when s.StartsWith("graph") => "graph",
            var s when s.StartsWith("sequencediagram") => "sequence diagram",
            var s when s.StartsWith("classDiagram") => "class diagram",
            var s when s.StartsWith("statediagram") => "state diagram",
            var s when s.StartsWith("erdiagram") => "ER diagram",
            var s when s.StartsWith("journey") => "journey",
            var s when s.StartsWith("gantt") => "gantt",
            var s when s.StartsWith("pie") => "pie chart",
            var s when s.StartsWith("gitgraph") => "git graph",
            var s when s.StartsWith("mindmap") => "mindmap",
            var s when s.StartsWith("timeline") => "timeline",
            var s when s.StartsWith("sankey") => "sankey",
            var s when s.StartsWith("xychart") => "XY chart",
            var s when s.StartsWith("block") => "block diagram",
            _ => firstLine.Split(' ').FirstOrDefault() ?? "unknown"
        };
    }

    [GeneratedRegex(@"!\[(.*?)\]\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"```mermaid\s*\n([\s\S]*?)```", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex MermaidBlockRegex();

    // Fix bold links: **[text](url)** -> [**text**](url)
    [GeneratedRegex(@"\*\*\[([^\]]+)\]\(([^)]+)\)\*\*", RegexOptions.Compiled)]
    private static partial Regex BoldLinkRegex();

    // Metadata extraction patterns
    [GeneratedRegex(@"<!--\s*category\s*--\s*(.+?)\s*-->", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CategoryRegex();

    [GeneratedRegex(@"<datetime[^>]*>([^<]+)</datetime>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DatetimeRegex();
}
