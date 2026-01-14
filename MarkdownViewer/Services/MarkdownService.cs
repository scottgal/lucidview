using System.Security;
using System.Text.RegularExpressions;
using MarkdownViewer.Models;
using MermaidSharp;
using SkiaSharp;
using Svg.Skia;

namespace MarkdownViewer.Services;

public partial class MarkdownService
{
    private string? _basePath;
    private string? _baseUrl;
    private bool _isDarkMode = true;

    private int _mermaidCounter;

    public MarkdownService()
    {
        TempDirectory = Path.Combine(Path.GetTempPath(), "lucidview-mermaid");
        Directory.CreateDirectory(TempDirectory);
    }

    public string TempDirectory { get; }

    public void SetDarkMode(bool isDark)
    {
        _isDarkMode = isDark;
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
    ///     Extract metadata from markdown content (categories, publication date)
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
            metadata.PublicationDate = pubDate;

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
                return match.Value;

            // Already absolute file path - leave as-is
            if (Path.IsPathRooted(path)) return match.Value;

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

    private string ProcessMermaidBlocks(string content)
    {
        _mermaidCounter = 0;
        var mermaidRegex = MermaidBlockRegex();
        var matches = mermaidRegex.Matches(content);

        foreach (Match match in matches)
        {
            var mermaidCode = match.Groups[1].Value.Trim();
            var diagramType = DetectMermaidDiagramType(mermaidCode);
            string replacement;

            try
            {
                // Preprocess: strip HTML tags that Naiad can't handle
                var processedCode = mermaidCode
                    .Replace("<br/>", " ")
                    .Replace("<br>", " ")
                    .Replace("<br />", " ");

                // Ensure proper indentation for Naiad parser
                // Split into lines, find first non-empty line (diagram type), indent the rest
                var lines = processedCode.Split('\n');
                var normalizedLines = new List<string>();
                var foundDiagramType = false;

                foreach (var line in lines)
                {
                    var trimmed = line.TrimStart();
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        normalizedLines.Add("");
                        continue;
                    }

                    if (!foundDiagramType)
                    {
                        // First non-empty line is the diagram type - no indent
                        normalizedLines.Add(trimmed);
                        foundDiagramType = true;
                    }
                    else
                    {
                        // Content lines get consistent 4-space indent
                        normalizedLines.Add("    " + trimmed);
                    }
                }

                processedCode = string.Join("\n", normalizedLines);

                // Render mermaid to SVG using Naiad
                var svgContent = Mermaid.Render(processedCode);

                // Post-process SVG: convert foreignObject to text elements
                // Avalonia's SVG renderer doesn't support foreignObject (HTML in SVG)
                svgContent = ConvertForeignObjectToText(svgContent);

                // Render SVG to PNG using SkiaSharp (handles text better than Svg.Skia control)
                var filename = $"diagram_{_mermaidCounter++}.png";
                var pngPath = Path.Combine(TempDirectory, filename);

                using var svg = new SKSvg();
                svg.FromSvg(svgContent);
                if (svg.Picture != null)
                {
                    var bounds = svg.Picture.CullRect;
                    var scale = 2f; // 2x for crisp rendering
                    var width = (int)(bounds.Width * scale);
                    var height = (int)(bounds.Height * scale);

                    using var bitmap = new SKBitmap(width, height);
                    using var canvas = new SKCanvas(bitmap);
                    canvas.Clear(SKColors.Transparent);
                    canvas.Scale(scale);
                    canvas.DrawPicture(svg.Picture);

                    using var image = SKImage.FromBitmap(bitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    using var stream = File.OpenWrite(pngPath);
                    data.SaveTo(stream);
                }

                // Use full path with forward slashes for markdown compatibility
                var markdownPath = pngPath.Replace("\\", "/");
                replacement = $"\n\n![Mermaid Diagram]({markdownPath})\n\n";
            }
            catch (Exception ex)
            {
                // Determine if it's a parse error or unsupported type
                var isParseError = ex.Message.Contains("parse", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("unexpected", StringComparison.OrdinalIgnoreCase);

                var errorHeader = isParseError
                    ? $"Mermaid parse error in '{diagramType}' diagram"
                    : $"Cannot render '{diagramType}' diagram";

                // On error, show syntax-highlighted mermaid code with warning
                replacement = $"""
                               > ⚠️ **{errorHeader}**
                               >
                               > {ex.Message}
                               >
                               > *Note: Complex features like `<br/>` in labels or nested subgraphs may not be supported.*

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

    /// <summary>
    ///     Convert foreignObject elements to SVG text elements for Avalonia compatibility
    ///     Also convert filled shapes to stroked outlines for dark mode
    /// </summary>
    private string ConvertForeignObjectToText(string svgContent)
    {
        // Replace foreignObject with text elements
        var foreignObjectRegex = ForeignObjectRegex();

        svgContent = foreignObjectRegex.Replace(svgContent, match =>
        {
            var x = match.Groups["x"].Value;
            var y = match.Groups["y"].Value;
            var width = match.Groups["width"].Value;
            var height = match.Groups["height"].Value;
            var innerHtml = match.Groups["content"].Value;

            // Extract text from the HTML content (look for <p> tags or plain text)
            var textContent = ExtractTextFromHtml(innerHtml);
            if (string.IsNullOrWhiteSpace(textContent)) return "";

            // Calculate center position for text
            var centerX = double.TryParse(x, out var xVal) ? xVal : 0;
            var centerY = double.TryParse(y, out var yVal) ? yVal : 0;
            var w = double.TryParse(width, out var wVal) ? wVal : 0;
            var h = double.TryParse(height, out var hVal) ? hVal : 0;

            centerX += w / 2;
            centerY += h / 2 + 5; // +5 for baseline adjustment

            // Return SVG text element with theme-appropriate fill color
            var textColor = _isDarkMode ? "#e6edf3" : "#333333";
            return
                $@"<text x=""{centerX}"" y=""{centerY}"" text-anchor=""middle"" dy=""0.35em"" fill=""{textColor}"" font-size=""14"">{SecurityElement.Escape(textContent)}</text>";
        });

        // Convert filled shapes to stroked outlines for dark mode compatibility
        // Change node fills to transparent with colored stroke
        svgContent = svgContent.Replace("fill=\"#ECECFF\"", "fill=\"none\"");
        svgContent = svgContent.Replace("fill=\"#ffffde\"", "fill=\"none\""); // cluster fill

        // Remove edge label backgrounds (gray boxes)
        svgContent = svgContent.Replace("fill=\"rgba(232,232,232,0.8)\"", "fill=\"none\"");

        return svgContent;
    }

    private static string ExtractTextFromHtml(string html)
    {
        // Simple extraction: find text inside <p> tags or spans
        var pMatch = Regex.Match(html, @"<p>([^<]*)</p>", RegexOptions.Singleline);
        if (pMatch.Success) return pMatch.Groups[1].Value.Trim();

        // Try to find any text content
        var textMatch = Regex.Match(html, @">([^<]+)<", RegexOptions.Singleline);
        if (textMatch.Success) return textMatch.Groups[1].Value.Trim();

        return "";
    }

    [GeneratedRegex(
        @"<foreignObject\s+x=""(?<x>[^""]+)""\s+y=""(?<y>[^""]+)""\s+width=""(?<width>[^""]+)""\s+height=""(?<height>[^""]+)""[^>]*>(?<content>[\s\S]*?)</foreignObject>",
        RegexOptions.Compiled)]
    private static partial Regex ForeignObjectRegex();
}