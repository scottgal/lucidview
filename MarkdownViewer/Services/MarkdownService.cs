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
    private ImageCacheService? _imageCacheService;

    private int _mermaidCounter;

    public MarkdownService()
    {
        TempDirectory = Path.Combine(Path.GetTempPath(), "lucidview-mermaid");
        Directory.CreateDirectory(TempDirectory);
    }

    public string TempDirectory { get; }

    /// <summary>
    /// Set the image cache service for caching remote images
    /// </summary>
    public void SetImageCacheService(ImageCacheService? cacheService)
    {
        _imageCacheService = cacheService;
    }

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

        // Convert HTML img tags to markdown syntax
        content = ProcessHtmlImageTags(content);

        // Process relative image paths and cache remote images
        content = ProcessImagePaths(content);

        // Process mermaid code blocks (placeholder for now)
        content = ProcessMermaidBlocks(content);

        return content.Trim();
    }

    /// <summary>
    /// Extract all image URLs from markdown content for pre-caching.
    /// Resolves relative paths to absolute URLs when base URL is set.
    /// </summary>
    public List<string> ExtractImageUrls(string content)
    {
        var urls = new List<string>();

        // Extract from markdown syntax: ![alt](url)
        foreach (Match match in ImageRegex().Matches(content))
        {
            var url = match.Groups[2].Value;
            var resolved = ResolveImageUrl(url);
            if (resolved != null)
                urls.Add(resolved);
        }

        // Extract from HTML img tags: <img src="url">
        foreach (Match match in HtmlImgRegex().Matches(content))
        {
            var url = match.Groups[1].Value;
            var resolved = ResolveImageUrl(url);
            if (resolved != null)
                urls.Add(resolved);
        }

        return urls.Distinct().ToList();
    }

    /// <summary>
    /// Resolve an image path to an absolute URL for caching.
    /// Returns null for local file paths (no caching needed).
    /// </summary>
    private string? ResolveImageUrl(string path)
    {
        // Already absolute URL
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        // Skip absolute file paths and data URIs
        if (Path.IsPathRooted(path) || path.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Resolve relative path against base URL (for remote markdown files)
        if (!string.IsNullOrEmpty(_baseUrl))
        {
            return ResolveRelativeUrl(_baseUrl, path);
        }

        // Local file - no URL caching needed
        return null;
    }

    /// <summary>
    /// Properly resolve a relative URL against a base URL.
    /// Handles ../, ./, and absolute paths starting with /
    /// </summary>
    private static string ResolveRelativeUrl(string baseUrl, string relativePath)
    {
        try
        {
            // Handle paths starting with / (absolute from host root)
            if (relativePath.StartsWith("/"))
            {
                var baseUri = new Uri(baseUrl);
                return $"{baseUri.Scheme}://{baseUri.Host}{relativePath}";
            }

            // Handle ../ and ./ relative paths
            var cleanBase = baseUrl.TrimEnd('/');
            var cleanPath = relativePath;

            // Process ../ segments
            while (cleanPath.StartsWith("../"))
            {
                cleanPath = cleanPath[3..]; // Remove ../
                var lastSlash = cleanBase.LastIndexOf('/');
                if (lastSlash > cleanBase.IndexOf("://") + 2) // Don't go past the host
                {
                    cleanBase = cleanBase[..lastSlash];
                }
            }

            // Remove leading ./
            if (cleanPath.StartsWith("./"))
            {
                cleanPath = cleanPath[2..];
            }

            return $"{cleanBase}/{cleanPath}";
        }
        catch
        {
            // Fallback: simple concatenation
            return $"{baseUrl.TrimEnd('/')}/{relativePath.TrimStart('.', '/')}";
        }
    }

    /// <summary>
    /// Convert HTML img tags to markdown image syntax
    /// Handles: <img src="url" width="300" height="200" alt="text">
    /// </summary>
    private string ProcessHtmlImageTags(string content)
    {
        return HtmlImgRegex().Replace(content, match =>
        {
            var fullTag = match.Value;
            var src = match.Groups[1].Value;

            // Extract alt text if present
            var altMatch = HtmlImgAltRegex().Match(fullTag);
            var alt = altMatch.Success ? altMatch.Groups[1].Value : "Image";

            // Convert to markdown syntax
            return $"![{alt}]({src})";
        });
    }

    private string ProcessImagePaths(string content)
    {
        var imageRegex = ImageRegex();

        return imageRegex.Replace(content, match =>
        {
            var alt = match.Groups[1].Value;
            var path = match.Groups[2].Value;

            // Skip data URIs
            if (path.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return match.Value;

            // Check if remote URL - try to use cached version
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessRemoteImage(alt, path);
            }

            // Already absolute file path - convert to file URI
            if (Path.IsPathRooted(path))
            {
                try
                {
                    var fileUri = new Uri(path).AbsoluteUri;
                    return $"![{alt}]({fileUri})";
                }
                catch
                {
                    return match.Value;
                }
            }

            // Resolve relative path
            if (!string.IsNullOrEmpty(_baseUrl))
            {
                // URL-based resolution - resolve and try cache
                var resolvedUrl = ResolveRelativeUrl(_baseUrl, path);
                return ProcessRemoteImage(alt, resolvedUrl);
            }

            if (!string.IsNullOrEmpty(_basePath))
            {
                // File-based resolution
                try
                {
                    var resolvedPath = Path.GetFullPath(Path.Combine(_basePath, path));
                    var fileUri = new Uri(resolvedPath).AbsoluteUri;
                    return $"![{alt}]({fileUri})";
                }
                catch
                {
                    return match.Value;
                }
            }

            return match.Value;
        });
    }

    /// <summary>
    /// Process a remote image URL - use cached version if available
    /// </summary>
    private string ProcessRemoteImage(string alt, string url)
    {
        // Use cached path if available
        var cachedPath = _imageCacheService?.GetCachedPath(url);
        if (cachedPath != null)
        {
            try
            {
                var fileUri = new Uri(cachedPath).AbsoluteUri;
                return $"![{alt}]({fileUri})";
            }
            catch { }
        }
        // Return the resolved URL (will be loaded directly by renderer)
        return $"![{alt}]({url})";
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
                // Preprocess mermaid code to match what mermaid.js handles
                var processedCode = PreprocessMermaidCode(mermaidCode);

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

    /// <summary>
    /// Preprocess mermaid code to handle various formats that mermaid.js supports
    /// </summary>
    private static string PreprocessMermaidCode(string mermaidCode)
    {
        var code = mermaidCode;

        // 1. Handle HTML entities
        code = code
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&nbsp;", " ");

        // 2. Handle HTML line breaks (mermaid.js allows these in labels)
        code = code
            .Replace("<br/>", "\\n")
            .Replace("<br>", "\\n")
            .Replace("<br />", "\\n");

        // 3. Handle Windows line endings
        code = code.Replace("\r\n", "\n").Replace("\r", "\n");

        // 4. Normalize indentation for Naiad parser
        var lines = code.Split('\n');
        var normalizedLines = new List<string>();
        var foundDiagramType = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            // Skip empty lines at the start
            if (!foundDiagramType && string.IsNullOrWhiteSpace(trimmed))
                continue;

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

        return string.Join("\n", normalizedLines);
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

    // HTML img tag patterns for GitHub-compatible content
    [GeneratedRegex(@"<img\s+[^>]*src=[""']([^""']+)[""'][^>]*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HtmlImgRegex();

    [GeneratedRegex(@"alt=[""']([^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HtmlImgAltRegex();

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