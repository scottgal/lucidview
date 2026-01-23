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
    private string _themeTextColor = "#e6edf3";      // Default to dark theme text
    private string _themeBackgroundColor = "#0d1117"; // Default to dark theme background
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
        // Use default colors for backwards compatibility
        _themeTextColor = isDark ? "#e6edf3" : "#333333";
        _themeBackgroundColor = isDark ? "#0d1117" : "#ffffff";
    }

    /// <summary>
    /// Set theme colors for accurate Mermaid diagram rendering
    /// </summary>
    public void SetThemeColors(bool isDark, string textColor, string backgroundColor)
    {
        _isDarkMode = isDark;
        _themeTextColor = textColor;
        _themeBackgroundColor = backgroundColor;
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
                // Try multiple preprocessing strategies until one works
                var svgContent = TryRenderMermaid(mermaidCode);

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
    /// Try to render mermaid code with multiple preprocessing strategies
    /// </summary>
    private static string TryRenderMermaid(string mermaidCode)
    {
        if (string.IsNullOrWhiteSpace(mermaidCode))
            throw new ArgumentException("Mermaid code is empty");

        var strategies = new List<Func<string, string>>
        {
            // Strategy 1: Full preprocessing (handles HTML entities, etc.)
            PreprocessMermaidCode,

            // Strategy 2: Strip indentation - Naiad parser is very sensitive to leading whitespace
            StripIndentation,

            // Strategy 3: Minimal preprocessing - just line endings
            code => code.Replace("\r\n", "\n").Replace("\r", "\n").Trim(),

            // Strategy 4: Strip all formatting aggressively
            StripMermaidFormatting,

            // Strategy 5: Raw code (maybe it's already valid)
            code => code.Trim()
        };

        Exception? lastException = null;
        var attemptedStrategies = new List<string>();

        foreach (var strategy in strategies)
        {
            try
            {
                var processedCode = strategy(mermaidCode);
                if (string.IsNullOrWhiteSpace(processedCode))
                {
                    attemptedStrategies.Add("empty result");
                    continue;
                }

                // Mermaid.Render can return null or throw - handle both
                string? svg = null;
                try
                {
                    svg = Mermaid.Render(processedCode);
                }
                catch (NullReferenceException)
                {
                    // Naiad library internal null reference - try next strategy
                    attemptedStrategies.Add("null reference in renderer");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(svg))
                    return svg;

                attemptedStrategies.Add("empty SVG");
            }
            catch (Exception ex)
            {
                lastException = ex;
                attemptedStrategies.Add(ex.Message.Length > 50 ? ex.Message[..50] + "..." : ex.Message);
            }
        }

        // All strategies failed
        var details = string.Join("; ", attemptedStrategies);
        throw lastException ?? new InvalidOperationException($"Failed to render mermaid diagram. Attempts: {details}");
    }

    /// <summary>
    /// Strip leading indentation from each line - Naiad parser is very strict about whitespace
    /// </summary>
    private static string StripIndentation(string code)
    {
        // Normalize line endings
        code = code.Replace("\r\n", "\n").Replace("\r", "\n");

        // Strip leading whitespace from each line while preserving content
        var lines = code.Split('\n')
            .Select(l => l.TrimStart())  // Only trim start, preserve trailing
            .ToList();

        return string.Join("\n", lines).Trim();
    }

    /// <summary>
    /// Aggressive stripping of formatting for fallback rendering
    /// </summary>
    private static string StripMermaidFormatting(string code)
    {
        // Normalize line endings
        code = code.Replace("\r\n", "\n").Replace("\r", "\n");

        // Remove all HTML
        code = Regex.Replace(code, @"<[^>]+>", "", RegexOptions.IgnoreCase);

        // Remove HTML entities
        code = code
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&nbsp;", " ");

        // Remove style/class definitions that might cause issues
        code = Regex.Replace(code, @":::[\w\s]+", "");
        code = Regex.Replace(code, @"class\s+\w+\s+[\w,]+", "");

        // Clean up lines
        var lines = code.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        return string.Join("\n", lines);
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

        // 2. Handle HTML line breaks in labels - convert to actual newlines or remove
        // Mermaid.js supports <br/> in labels but Naiad may not
        code = Regex.Replace(code, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);

        // 3. Remove any HTML tags that might be in labels (common in copied content)
        code = Regex.Replace(code, @"<[^>]+>", " ");

        // 4. Handle Windows line endings
        code = code.Replace("\r\n", "\n").Replace("\r", "\n");

        // 5. Remove any BOM or invisible characters
        code = code.Trim('\uFEFF', '\u200B', '\u200C', '\u200D');

        // 6. Normalize whitespace in labels - collapse multiple spaces
        code = Regex.Replace(code, @"[ \t]+", " ");

        // 7. Process line by line - preserve structure but clean up
        var lines = code.Split('\n');
        var normalizedLines = new List<string>();
        var foundDiagramType = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip empty lines at the start
            if (!foundDiagramType && string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Skip comment-only lines that might cause issues
            if (trimmed.StartsWith("%%") && trimmed.Length > 2 && !trimmed.StartsWith("%%{"))
            {
                // Keep directive comments like %%{init: ...}%%
                if (!trimmed.Contains("{"))
                    continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                // Don't add multiple consecutive empty lines
                if (normalizedLines.Count > 0 && !string.IsNullOrWhiteSpace(normalizedLines[^1]))
                    normalizedLines.Add("");
                continue;
            }

            if (!foundDiagramType)
            {
                // First non-empty line is the diagram type - ensure proper format
                // Handle variations like "flowchart LR", "graph TD", etc.
                normalizedLines.Add(trimmed);
                foundDiagramType = true;
            }
            else
            {
                // Content lines - keep original indentation style but normalize
                // Don't force 4-space indent as it can break some syntax
                normalizedLines.Add(trimmed);
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

            // Return SVG text element with theme-appropriate fill color and proper font
            // Use system sans-serif fonts that are widely available
            const string fontFamily = "Segoe UI, Arial, sans-serif";
            return
                $@"<text x=""{centerX}"" y=""{centerY}"" text-anchor=""middle"" dy=""0.35em"" fill=""{_themeTextColor}"" font-family=""{fontFamily}"" font-size=""14"">{SecurityElement.Escape(textContent)}</text>";
        });

        // Theme-aware SVG modifications
        if (_isDarkMode)
        {
            // Dark mode: Convert filled shapes to transparent (outlines only look better on dark)
            svgContent = svgContent.Replace("fill=\"#ECECFF\"", "fill=\"none\"");
            svgContent = svgContent.Replace("fill=\"#ffffde\"", "fill=\"none\"");
            svgContent = svgContent.Replace("fill=\"rgba(232,232,232,0.8)\"", "fill=\"none\"");

            // Replace dark text colors with theme text color
            svgContent = svgContent.Replace("fill=\"#333\"", $"fill=\"{_themeTextColor}\"");
            svgContent = svgContent.Replace("fill=\"#333333\"", $"fill=\"{_themeTextColor}\"");
            svgContent = svgContent.Replace("fill=\"#000\"", $"fill=\"{_themeTextColor}\"");
            svgContent = svgContent.Replace("fill=\"#000000\"", $"fill=\"{_themeTextColor}\"");
            svgContent = svgContent.Replace("fill=\"black\"", $"fill=\"{_themeTextColor}\"");

            // Fix stroke colors for visibility
            svgContent = svgContent.Replace("stroke=\"#333\"", $"stroke=\"{_themeTextColor}\"");
            svgContent = svgContent.Replace("stroke=\"#333333\"", $"stroke=\"{_themeTextColor}\"");
        }
        else
        {
            // Light mode: Keep light fills but ensure text is dark
            // Keep fills for light backgrounds: #ECECFF (light blue), #ffffde (light yellow)

            // Replace light text colors with theme text color (dark)
            svgContent = svgContent.Replace("fill=\"#fff\"", $"fill=\"{_themeTextColor}\"");
            svgContent = svgContent.Replace("fill=\"#ffffff\"", $"fill=\"{_themeTextColor}\"");
            svgContent = svgContent.Replace("fill=\"white\"", $"fill=\"{_themeTextColor}\"");

            // Ensure dark text stays dark for readability
            svgContent = svgContent.Replace("fill=\"#e6edf3\"", $"fill=\"{_themeTextColor}\"");
            svgContent = svgContent.Replace("fill=\"#c9d1d9\"", $"fill=\"{_themeTextColor}\"");
        }

        // General contrast fix: Replace any text fill color that would be unreadable
        // This catches edge cases where Mermaid outputs unexpected colors
        svgContent = ReplaceUnreadableTextColors(svgContent);

        // Replace Mermaid's default fonts with system fonts that are more reliable
        svgContent = Regex.Replace(svgContent,
            @"font-family\s*:\s*[""']?[^;""']+[""']?\s*;?",
            "font-family: 'Segoe UI', Arial, sans-serif;",
            RegexOptions.IgnoreCase);
        svgContent = Regex.Replace(svgContent,
            @"font-family\s*=\s*""[^""]+""",
            "font-family=\"Segoe UI, Arial, sans-serif\"",
            RegexOptions.IgnoreCase);

        return svgContent;
    }

    /// <summary>
    /// Replace text/fill colors that would be unreadable on the current theme background
    /// </summary>
    private string ReplaceUnreadableTextColors(string svgContent)
    {
        // 1. Fix fill attributes on text elements: <text fill="#color">
        svgContent = Regex.Replace(svgContent,
            @"(<text[^>]*)(fill\s*=\s*"")(#[0-9a-fA-F]{3,6})("")",
            match => FixColorAttribute(match, 3),
            RegexOptions.IgnoreCase);

        // 2. Fix fill in style attributes: style="fill: #color"
        svgContent = Regex.Replace(svgContent,
            @"(style\s*=\s*""[^""]*)(fill\s*:\s*)(#[0-9a-fA-F]{3,6})([^""]*"")",
            match => FixColorInStyle(match, 3),
            RegexOptions.IgnoreCase);

        // 3. Fix standalone fill attributes on other elements (spans, tspan, etc.)
        svgContent = Regex.Replace(svgContent,
            @"(<(?:tspan|span|g)[^>]*)(fill\s*=\s*"")(#[0-9a-fA-F]{3,6})("")",
            match => FixColorAttribute(match, 3),
            RegexOptions.IgnoreCase);

        // 4. Fix named colors that might be problematic
        if (_isDarkMode)
        {
            // Dark backgrounds: fix light text colors
            svgContent = Regex.Replace(svgContent, @"fill\s*=\s*""black""", $"fill=\"{_themeTextColor}\"", RegexOptions.IgnoreCase);
            svgContent = Regex.Replace(svgContent, @"fill\s*:\s*black", $"fill: {_themeTextColor}", RegexOptions.IgnoreCase);
        }
        else
        {
            // Light backgrounds: fix dark text colors
            svgContent = Regex.Replace(svgContent, @"fill\s*=\s*""white""", $"fill=\"{_themeTextColor}\"", RegexOptions.IgnoreCase);
            svgContent = Regex.Replace(svgContent, @"fill\s*:\s*white", $"fill: {_themeTextColor}", RegexOptions.IgnoreCase);
        }

        return svgContent;
    }

    private string FixColorAttribute(Match match, int colorGroupIndex)
    {
        var colorHex = match.Groups[colorGroupIndex].Value;
        if (IsLowContrast(colorHex, _themeBackgroundColor))
        {
            // Rebuild the match with fixed color
            var result = "";
            for (int i = 1; i < match.Groups.Count; i++)
            {
                result += i == colorGroupIndex ? _themeTextColor : match.Groups[i].Value;
            }
            return result;
        }
        return match.Value;
    }

    private string FixColorInStyle(Match match, int colorGroupIndex)
    {
        var colorHex = match.Groups[colorGroupIndex].Value;
        if (IsLowContrast(colorHex, _themeBackgroundColor))
        {
            var result = "";
            for (int i = 1; i < match.Groups.Count; i++)
            {
                result += i == colorGroupIndex ? _themeTextColor : match.Groups[i].Value;
            }
            return result;
        }
        return match.Value;
    }

    /// <summary>
    /// Check if two colors have low contrast (would be hard to read)
    /// Uses simplified relative luminance calculation
    /// </summary>
    private static bool IsLowContrast(string color1, string color2)
    {
        var lum1 = GetRelativeLuminance(color1);
        var lum2 = GetRelativeLuminance(color2);

        // Contrast ratio formula
        var lighter = Math.Max(lum1, lum2);
        var darker = Math.Min(lum1, lum2);
        var contrastRatio = (lighter + 0.05) / (darker + 0.05);

        // WCAG AA requires 4.5:1 for normal text, we use 3:1 as minimum readable
        return contrastRatio < 3.0;
    }

    /// <summary>
    /// Calculate relative luminance from hex color
    /// </summary>
    private static double GetRelativeLuminance(string hexColor)
    {
        try
        {
            // Normalize hex color
            var hex = hexColor.TrimStart('#');
            if (hex.Length == 3)
                hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";

            if (hex.Length != 6) return 0.5; // Default to mid luminance

            var r = Convert.ToInt32(hex[..2], 16) / 255.0;
            var g = Convert.ToInt32(hex[2..4], 16) / 255.0;
            var b = Convert.ToInt32(hex[4..6], 16) / 255.0;

            // sRGB to linear
            r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }
        catch
        {
            return 0.5; // Default to mid luminance on parse error
        }
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