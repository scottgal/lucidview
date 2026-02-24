using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using MarkdownViewer.Models;
using MermaidSharp;
using MermaidSharp.Diagrams.Flowchart;
using MermaidSharp.Rendering;
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
    private string _themeNodeFill = "#1c2333";
    private string _themeNodeStroke = "#7c6bbd";
    private string _themeEdgeStroke = "#c9d1d9";
    private string _themeEdgeLabelBackground = "rgba(13,17,23,0.85)";
    private string? _themeSubgraphFill;
    private string? _themeSubgraphStroke;
    private ImageCacheService? _imageCacheService;
    private MermaidCacheService? _mermaidCache;

    private int _mermaidCounter;
    private CancellationTokenSource? _renderCts;

    /// <summary>
    /// Maps rendered PNG file paths to their original mermaid source code.
    /// Used for right-click "Save As" SVG/PNG export.
    /// </summary>
    private readonly Dictionary<string, string> _mermaidSourceMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps flowchart placeholder keys (e.g. "flowchart-0") to their native layout results.
    /// Used by FlowchartCanvas to render flowcharts as native Avalonia controls instead of PNGs.
    /// </summary>
    private readonly Dictionary<string, FlowchartLayoutResult> _flowchartLayouts = new(StringComparer.Ordinal);
    private int _flowchartCounter;

    /// <summary>
    /// Maps diagram placeholder keys (e.g. "diagram-0") to their SvgDocument for native rendering.
    /// Used by DiagramCanvas to render non-flowchart diagrams as native Avalonia controls instead of PNGs.
    /// </summary>
    private readonly Dictionary<string, SvgDocument> _diagramDocuments = new(StringComparer.Ordinal);
    private int _diagramCounter;

    /// <summary>
    /// Get all mermaid diagrams in the current document (PNG path → mermaid code).
    /// </summary>
    public IReadOnlyDictionary<string, string> MermaidDiagrams => _mermaidSourceMap;

    /// <summary>
    /// Look up mermaid source code by image path (handles path separator differences).
    /// </summary>
    public string? GetMermaidSourceByImagePath(string imagePath)
    {
        var normalized = imagePath.Replace("/", "\\");
        foreach (var (key, value) in _mermaidSourceMap)
        {
            if (key.Replace("/", "\\").Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return null;
    }

    /// <summary>
    /// Get all flowchart layouts for native rendering (placeholder key → layout).
    /// </summary>
    public IReadOnlyDictionary<string, FlowchartLayoutResult> FlowchartLayouts => _flowchartLayouts;

    /// <summary>
    /// Get all diagram documents for native rendering (placeholder key → SvgDocument).
    /// </summary>
    public IReadOnlyDictionary<string, SvgDocument> DiagramDocuments => _diagramDocuments;

    /// <summary>
    /// Maps C4 element IDs to target diagram keys for zoom/drill-down navigation.
    /// Built by analyzing cross-diagram element↔boundary ID matches.
    /// </summary>
    private readonly Dictionary<string, string> _c4ZoomTargets = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> C4ZoomTargets => _c4ZoomTargets;

    /// <summary>
    /// Look up a native flowchart layout by its placeholder key.
    /// </summary>
    public FlowchartLayoutResult? GetFlowchartLayout(string key)
    {
        return _flowchartLayouts.GetValueOrDefault(key);
    }

    public MarkdownService()
    {
        TempDirectory = Path.Combine(Path.GetTempPath(), "lucidview-mermaid");
        Directory.CreateDirectory(TempDirectory);
        _mermaidCache = new MermaidCacheService();
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
        var dt = isDark ? DiagramTheme.Dark : DiagramTheme.Light;
        _themeNodeFill = dt.PrimaryFill;
        _themeNodeStroke = dt.PrimaryStroke;
        _themeEdgeStroke = dt.AxisLine;
        _themeEdgeLabelBackground = dt.LabelBackground;
        _themeSubgraphFill = null;
        _themeSubgraphStroke = null;
    }

    /// <summary>
    /// Set theme colors for accurate Mermaid diagram rendering.
    /// Uses the full ThemeDefinition to derive diagram-specific colors (node fill, stroke, etc.)
    /// so diagrams visually match the active lucidVIEW theme.
    /// </summary>
    public void SetThemeColors(ThemeDefinition themeDef)
    {
        var isDark = IsDarkColor(themeDef.Background, true);
        _isDarkMode = isDark;
        _themeTextColor = themeDef.Text;
        _themeBackgroundColor = themeDef.Background;
        _themeNodeFill = themeDef.Surface;
        _themeNodeStroke = themeDef.Accent;
        _themeEdgeStroke = themeDef.TextSecondary;
        _themeEdgeLabelBackground = isDark
            ? HexToRgba(themeDef.BackgroundSecondary, 0.85)
            : HexToRgba(themeDef.Background, 0.85);
        _themeSubgraphFill = HexToRgba(themeDef.BackgroundTertiary, 0.5);
        _themeSubgraphStroke = themeDef.Border;
    }

    /// <summary>
    /// Legacy overload for callers that only have basic color info.
    /// </summary>
    public void SetThemeColors(bool isDark, string textColor, string backgroundColor)
    {
        _isDarkMode = IsDarkColor(backgroundColor, isDark);
        _themeTextColor = textColor;
        _themeBackgroundColor = backgroundColor;
        var dt = _isDarkMode ? DiagramTheme.Dark : DiagramTheme.Light;
        _themeNodeFill = dt.PrimaryFill;
        _themeNodeStroke = dt.PrimaryStroke;
        _themeEdgeStroke = dt.AxisLine;
        _themeEdgeLabelBackground = dt.LabelBackground;
        _themeSubgraphFill = null;
        _themeSubgraphStroke = null;
    }

    static string HexToRgba(string hex, double alpha)
    {
        if (string.IsNullOrEmpty(hex) || hex[0] != '#' || hex.Length < 7) return hex;
        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);
        return $"rgba({r},{g},{b},{alpha:0.##})";
    }

    RenderOptions CreateRenderOptions()
    {
        var isDarkTheme = IsDarkColor(_themeBackgroundColor, _isDarkMode);

        return new RenderOptions
        {
            Theme = isDarkTheme ? "dark" : "default",
            ThemeColors = new ThemeColorOverrides
            {
                TextColor = _themeTextColor,
                BackgroundColor = _themeBackgroundColor,
                NodeFill = _themeNodeFill,
                NodeStroke = _themeNodeStroke,
                EdgeStroke = _themeEdgeStroke,
                EdgeLabelBackground = _themeEdgeLabelBackground,
                SubgraphFill = _themeSubgraphFill,
                SubgraphStroke = _themeSubgraphStroke
            },
            // Desktop host allows skin packs from local folders/archives. Mermaid input
            // can keep paths relative to the current markdown file directory.
            AllowFileSystemSkinPacks = true,
            SkinPackBaseDirectory = _basePath
        };
    }

    static bool IsDarkColor(string? color, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(color))
            return fallback;

        try
        {
            var parsed = SKColor.Parse(color);
            var luminance = 0.299 * parsed.Red + 0.587 * parsed.Green + 0.114 * parsed.Blue;
            return luminance < 128;
        }
        catch
        {
            return fallback;
        }
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
    public static DocumentMetadata ExtractMetadata(string content)
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

    /// <summary>
    /// Phase 1: Fast synchronous processing. Text transforms + cached mermaid diagrams.
    /// Returns processed content with placeholders for uncached diagrams.
    /// </summary>
    public (string Content, List<MermaidWorkItem> PendingDiagrams) ProcessMarkdownFast(string content)
    {
        // Remove metadata tags from rendered content (they'll be shown separately)
        content = CategoryRegex().Replace(content, "");
        content = DatetimeRegex().Replace(content, "");

        // Fix bold links: **[text](url)** -> [**text**](url)
        content = BoldLinkRegex().Replace(content, "[**$1**]($2)");

        // Convert HTML img tags to markdown syntax
        content = ProcessHtmlImageTags(content);

        // Collapse consecutive image-only lines into a single line (for inline badge rendering)
        content = CollapseConsecutiveImages(content);

        // Process relative image paths and cache remote images
        content = ProcessImagePaths(content);

        // Process mermaid: use cache hits immediately, collect misses
        var (processed, pending) = ProcessMermaidBlocksTwoPhase(content);

        return (processed.Trim(), pending);
    }

    /// <summary>
    /// Legacy synchronous API - renders everything inline. Used by print path.
    /// </summary>
    public string ProcessMarkdown(string content)
    {
        var (fast, pending) = ProcessMarkdownFast(content);

        // Render any uncached diagrams synchronously
        foreach (var item in pending)
        {
            try
            {
                var pngPath = RenderMermaidToPng(item.MermaidCode);
                _mermaidSourceMap[pngPath] = item.MermaidCode;
                var markdownPath = pngPath.Replace("\\", "/");
                fast = fast.Replace(item.Placeholder, $"\n\n![Mermaid Diagram]({markdownPath})\n\n");
            }
            catch (Exception ex)
            {
                fast = fast.Replace(item.Placeholder, FormatMermaidError(item.MermaidCode, ex));
            }
        }

        return fast;
    }

    /// <summary>
    /// Phase 2: Render uncached mermaid diagrams in parallel on background threads.
    /// Returns a map of placeholder → markdown replacement.
    /// </summary>
    public async Task<Dictionary<string, string>> RenderPendingDiagramsAsync(
        List<MermaidWorkItem> pending, CancellationToken ct)
    {
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        if (pending.Count == 0) return results;

        // Limit parallelism to avoid memory pressure from large SVG rasterization
        var semaphore = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount / 2));

        var tasks = pending.Select(async item =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();

                // Naiad rendering is CPU-bound - run on thread pool
                var pngPath = await Task.Run(() => RenderMermaidToPng(item.MermaidCode), ct);
                _mermaidSourceMap[pngPath] = item.MermaidCode;
                var markdownPath = pngPath.Replace("\\", "/");
                return (item.Placeholder, Replacement: $"\n\n![Mermaid Diagram]({markdownPath})\n\n");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (item.Placeholder, Replacement: FormatMermaidError(item.MermaidCode, ex));
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var completed = await Task.WhenAll(tasks);
        foreach (var (placeholder, replacement) in completed)
        {
            results[placeholder] = replacement;
        }

        return results;
    }

    /// <summary>
    /// Cancel any in-flight background diagram renders.
    /// </summary>
    public CancellationToken BeginNewRenderBatch()
    {
        _renderCts?.Cancel();
        _renderCts = new CancellationTokenSource();
        return _renderCts.Token;
    }

    /// <summary>
    /// Invalidate mermaid cache (e.g. on theme change).
    /// </summary>
    public void InvalidateMermaidCache()
    {
        _mermaidCache?.InvalidateAll();
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
            if (relativePath.StartsWith('/'))
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
    private static string ProcessHtmlImageTags(string content)
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

    /// <summary>
    /// Two-phase mermaid processing: cache hits are inlined immediately,
    /// cache misses produce placeholder tokens collected for async rendering.
    /// </summary>
    /// <summary>
    /// Marker prefix used in markdown text for flowcharts that will be rendered natively.
    /// LiveMarkdown renders this as a Run inside a MarkdownTextBlock that we find
    /// in the visual tree and replace with FlowchartCanvas.
    /// </summary>
    internal const string FlowchartMarkerPrefix = "FLOWCHART:";
    internal const string DiagramMarkerPrefix = "DIAGRAM:";

    private (string Content, List<MermaidWorkItem> Pending) ProcessMermaidBlocksTwoPhase(string content)
    {
        _mermaidCounter = 0;
        _flowchartCounter = 0;
        _diagramCounter = 0;
        _mermaidSourceMap.Clear();
        _flowchartLayouts.Clear();
        _diagramDocuments.Clear();
        _c4ZoomTargets.Clear();
        var mermaidRegex = MermaidBlockRegex();
        var matches = mermaidRegex.Matches(content);
        var pending = new List<MermaidWorkItem>();
        var rewritten = new StringBuilder(content.Length + 256);
        var cursor = 0;

        foreach (Match match in matches)
        {
            rewritten.Append(content, cursor, match.Index - cursor);
            var mermaidCode = match.Groups[1].Value.Trim();

            // Try native flowchart rendering first (no PNG needed)
            var nativeLayout = TryComputeFlowchartLayout(mermaidCode);
            if (nativeLayout is not null)
            {
                var flowchartKey = $"flowchart-{_flowchartCounter++}";
                _flowchartLayouts[flowchartKey] = nativeLayout;
                _mermaidSourceMap[flowchartKey] = mermaidCode;

                // Insert a text marker. LiveMarkdown renders it as a Run inside a
                // MarkdownTextBlock. We find it in the visual tree and replace with FlowchartCanvas.
                rewritten.Append($"\n\n{FlowchartMarkerPrefix}{flowchartKey}\n\n");
                cursor = match.Index + match.Length;
                continue;
            }

            // Non-flowcharts: render to in-memory SvgDocument and replace with a native
            // DiagramCanvas marker (no temp SVG file write needed).
            var nativeDoc = TryRenderToDocument(mermaidCode);
            if (nativeDoc is not null)
            {
                var diagramKey = $"diagram-{_diagramCounter++}";
                _diagramDocuments[diagramKey] = nativeDoc;
                _mermaidSourceMap[diagramKey] = mermaidCode;
                rewritten.Append($"\n\n{DiagramMarkerPrefix}{diagramKey}\n\n");
                cursor = match.Index + match.Length;
                continue;
            }

            // Fallback path for diagrams that cannot be represented natively yet.
            var svgPath = TryRenderMermaidToSvgFile(mermaidCode);
            if (svgPath is not null)
            {
                _mermaidSourceMap[svgPath] = mermaidCode;
                var markdownPath = svgPath.Replace("\\", "/");
                rewritten.Append($"\n\n![Mermaid Diagram]({markdownPath})\n\n");
                cursor = match.Index + match.Length;
                continue;
            }

            // Fallback: PNG pipeline (always render fresh - fast enough without caching)
            {
                var placeholder = $"<!--mermaid-pending-{_mermaidCounter++}-->";
                rewritten.Append(placeholder);
                pending.Add(new MermaidWorkItem(placeholder, mermaidCode, null));
            }

            cursor = match.Index + match.Length;
        }

        rewritten.Append(content, cursor, content.Length - cursor);
        content = rewritten.ToString();

        // Process BPMN blocks (```bpmn ... ```) as SVG images.
        var bpmnRegex = BpmnBlockRegex();
        var bpmnMatches = bpmnRegex.Matches(content);
        if (bpmnMatches.Count > 0)
        {
            var bpmnOutput = new StringBuilder(content.Length + 256);
            cursor = 0;

            foreach (Match match in bpmnMatches)
            {
                bpmnOutput.Append(content, cursor, match.Index - cursor);
                var bpmnCode = match.Groups[1].Value.Trim();
                var replacement = match.Value;

                try
                {
                    var svg = Mermaid.Render(bpmnCode, CreateRenderOptions());
                    svg = PostProcessSvg(svg);
                    if (!string.IsNullOrWhiteSpace(svg))
                    {
                        var svgPath = WriteTempSvg(svg);
                        _mermaidSourceMap[svgPath] = bpmnCode;
                        var markdownPath = svgPath.Replace("\\", "/");
                        replacement = $"\n\n![Mermaid Diagram]({markdownPath})\n\n";
                    }
                }
                catch
                {
                    // BPMN parse failure - keep original code block
                }

                bpmnOutput.Append(replacement);
                cursor = match.Index + match.Length;
            }

            bpmnOutput.Append(content, cursor, content.Length - cursor);
            content = bpmnOutput.ToString();
        }

        BuildC4ZoomIndex();

        return (content, pending);
    }

    /// <summary>
    /// Build cross-diagram zoom targets by matching C4 element IDs to boundary IDs.
    /// Convention: if a C4Context has element "myapp" and a C4Container has boundary "myapp",
    /// clicking "myapp" in Context should scroll to the Container diagram.
    /// Explicit $link values override convention-based matches.
    /// </summary>
    private void BuildC4ZoomIndex()
    {
        _c4ZoomTargets.Clear();

        // Collect C4 diagrams with their metadata
        var c4Diagrams = new List<(string Key, SvgDocument Doc)>();
        foreach (var (key, doc) in _diagramDocuments)
        {
            if (doc.Metadata.ContainsKey("c4Type"))
                c4Diagrams.Add((key, doc));
        }

        if (c4Diagrams.Count < 2) return;

        // Build boundary→diagram key lookup: which diagram contains boundary "X"?
        var boundaryToDiagram = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, doc) in c4Diagrams)
        {
            foreach (var (metaKey, _) in doc.Metadata)
            {
                if (metaKey.StartsWith("boundary:", StringComparison.OrdinalIgnoreCase))
                {
                    var boundaryId = metaKey["boundary:".Length..];
                    boundaryToDiagram[boundaryId] = key;
                }
            }
        }

        // Match element IDs in one diagram to boundary IDs in another
        foreach (var (key, doc) in c4Diagrams)
        {
            foreach (var elementId in doc.HitRegions.Keys)
            {
                // Convention-based: element ID matches a boundary ID in another diagram
                if (boundaryToDiagram.TryGetValue(elementId, out var targetKey) && targetKey != key)
                {
                    _c4ZoomTargets[elementId] = targetKey;
                }
            }

            // Explicit $link overrides
            foreach (var (metaKey, metaValue) in doc.Metadata)
            {
                if (metaKey.StartsWith("link:", StringComparison.OrdinalIgnoreCase))
                {
                    var elementId = metaKey["link:".Length..];
                    _c4ZoomTargets[elementId] = metaValue;
                }
            }
        }
    }

    /// <summary>
    /// If the mermaid code is a flowchart, compute a native layout and return it.
    /// Returns null for non-flowchart diagrams or on parse/layout failure.
    /// </summary>
    private FlowchartLayoutResult? TryComputeFlowchartLayout(string mermaidCode)
    {
        try
        {
            var diagramType = Mermaid.DetectDiagramType(mermaidCode);
            if (diagramType != DiagramType.Flowchart) return null;

            var renderOptions = CreateRenderOptions();
            // Try progressively more tolerant preprocessing before giving up.
            var attempts = new[]
            {
                mermaidCode.Trim(),
                StripIndentation(mermaidCode),
                PreprocessMermaidCode(mermaidCode)
            };

            foreach (var attempt in attempts)
            {
                if (string.IsNullOrWhiteSpace(attempt))
                    continue;

                var layout = Mermaid.ParseAndLayoutFlowchart(attempt, renderOptions);
                if (layout is not null)
                    return layout;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Try to render mermaid code to a structured SvgDocument for native Avalonia rendering.
    /// Returns null on any failure (parsing, rendering, unsupported type).
    /// </summary>
    private SvgDocument? TryRenderToDocument(string mermaidCode)
    {
        try
        {
            var renderOptions = CreateRenderOptions();

            return Mermaid.RenderToDocument(mermaidCode, renderOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Try to render a Mermaid diagram to SVG and persist it as a temp file.
    /// Returns the SVG file path on success, otherwise null.
    /// </summary>
    private string? TryRenderMermaidToSvgFile(string mermaidCode)
    {
        try
        {
            var svgContent = TryRenderMermaid(mermaidCode);
            svgContent = PostProcessSvg(svgContent);
            return WriteTempSvg(svgContent);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Render mermaid code to a PNG file. Returns the file path.
    /// Thread-safe - can be called from multiple threads concurrently.
    /// </summary>
    private string RenderMermaidToPng(string mermaidCode)
    {
        // Capture theme state for thread safety
        var isDark = _isDarkMode;
        var textColor = _themeTextColor;
        var bgColor = _themeBackgroundColor;

        var svgContent = TryRenderMermaid(mermaidCode);
        svgContent = PostProcessSvg(svgContent);

        // Rasterize to PNG
        using var svg = new SKSvg();
        svg.FromSvg(svgContent);
        if (svg.Picture == null)
            throw new InvalidOperationException("SVG produced null picture");

        var bounds = svg.Picture.CullRect;
        var scale = 2f;
        var width = (int)(bounds.Width * scale);
        var height = (int)(bounds.Height * scale);

        byte[] pngData;
        using (var bitmap = new SKBitmap(width, height))
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.DrawPicture(svg.Picture);

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            pngData = data.ToArray();
        }

        return WriteTempPng(pngData);
    }

    /// <summary>
    /// Render mermaid code to SVG string. Public API for export.
    /// </summary>
    public string ExportMermaidToSvg(string mermaidCode)
    {
        var svgContent = TryRenderMermaid(mermaidCode);
        return PostProcessSvg(svgContent);
    }

    /// <summary>
    /// Render mermaid code to PNG bytes. Public API for export.
    /// </summary>
    public byte[] ExportMermaidToPngBytes(string mermaidCode, float scale = 2f)
    {
        var svgContent = TryRenderMermaid(mermaidCode);
        svgContent = PostProcessSvg(svgContent);

        using var svg = new SKSvg();
        svg.FromSvg(svgContent);
        if (svg.Picture == null)
            throw new InvalidOperationException("SVG produced null picture");

        var bounds = svg.Picture.CullRect;
        var width = (int)(bounds.Width * scale);
        var height = (int)(bounds.Height * scale);

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.DrawPicture(svg.Picture);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private string WriteTempPng(byte[] pngData)
    {
        var path = Path.Combine(TempDirectory, $"diagram_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, pngData);
        return path;
    }

    private string WriteTempSvg(string svgContent)
    {
        var path = Path.Combine(TempDirectory, $"diagram_{Guid.NewGuid():N}.svg");
        File.WriteAllText(path, svgContent);
        return path;
    }

    private static string FormatMermaidError(string mermaidCode, Exception ex)
    {
        var diagramType = DetectMermaidDiagramType(mermaidCode);
        var isParseError = ex.Message.Contains("parse", StringComparison.OrdinalIgnoreCase) ||
                           ex.Message.Contains("unexpected", StringComparison.OrdinalIgnoreCase);

        var errorHeader = isParseError
            ? $"Mermaid parse error in '{diagramType}' diagram"
            : $"Cannot render '{diagramType}' diagram";

        return $"""
               > **{errorHeader}**
               >
               > {ex.Message}

               ```mermaid
               {mermaidCode}
               ```
               """;
    }

    /// <summary>
    /// Work item for a mermaid diagram that needs rendering.
    /// </summary>
    public record MermaidWorkItem(string Placeholder, string MermaidCode, string? CacheKey);

    /// <summary>
    /// Try to render mermaid code with multiple preprocessing strategies
    /// </summary>
    private string TryRenderMermaid(string mermaidCode)
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
                    var renderOptions = CreateRenderOptions();
                    svg = Mermaid.Render(processedCode, renderOptions);
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

        // Remove simple HTML tags while preserving plain comparison operators like "< 80%".
        code = Regex.Replace(code, @"</?[A-Za-z][^>\r\n]*>", "", RegexOptions.IgnoreCase);

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

        // 3. Remove simple HTML tags that Mermaid doesn't support natively.
        // Keep plain text comparisons like "< 80%" intact.
        code = Regex.Replace(code, @"</?[A-Za-z][^>\r\n]*>", " ", RegexOptions.IgnoreCase);

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
                if (!trimmed.Contains('{'))
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
            var s when s.StartsWith("classdiagram") => "class diagram",
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

    /// <summary>
    /// Collapse consecutive lines that contain only image/badge markdown into a single line.
    /// This makes shields.io badges and similar image sequences render inline instead of as blocks.
    /// Handles both plain images ![alt](url) and linked images [![alt](img)](link).
    /// </summary>
    private static string CollapseConsecutiveImages(string content)
    {
        var lines = content.Split('\n');
        var result = new List<string>();
        var imageRun = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (IsImageOnlyLine(trimmed))
            {
                imageRun.Add(trimmed);
            }
            else
            {
                if (imageRun.Count > 1)
                {
                    // Multiple consecutive image lines → join as single line
                    result.Add(string.Join(" ", imageRun));
                }
                else if (imageRun.Count == 1)
                {
                    // Single image line → keep as-is
                    result.Add(imageRun[0]);
                }
                imageRun.Clear();
                result.Add(lines[i]);
            }
        }

        // Flush any trailing image run
        if (imageRun.Count > 1)
            result.Add(string.Join(" ", imageRun));
        else if (imageRun.Count == 1)
            result.Add(imageRun[0]);

        return string.Join("\n", result);
    }

    /// <summary>
    /// Check if a line contains only an image or linked image (badge pattern).
    /// Matches: ![alt](url)  or  [![alt](img)](link)
    /// </summary>
    private static bool IsImageOnlyLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        return ImageOnlyLineRegex().IsMatch(line);
    }

    // Matches a line that is entirely a markdown image or a linked image (badge)
    [GeneratedRegex(@"^(\[!\[[^\]]*\]\([^)]+\)\]\([^)]+\)|!\[[^\]]*\]\([^)]+\))$", RegexOptions.Compiled)]
    private static partial Regex ImageOnlyLineRegex();

    [GeneratedRegex(@"!\[(.*?)\]\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"```mermaid\s*\n([\s\S]*?)```", RegexOptions.Multiline | RegexOptions.Compiled)]
    internal static partial Regex MermaidBlockRegex();

    internal static IEnumerable<(string FullMatch, string MermaidCode)> FindMermaidBlocks(string content)
    {
        foreach (Match match in MermaidBlockRegex().Matches(content))
            yield return (match.Value, match.Groups[1].Value.Trim());
    }

    [GeneratedRegex(@"```bpmn\s*\n([\s\S]*?)```", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex BpmnBlockRegex();

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
    /// Post-process SVG for Avalonia compatibility.
    /// Since Naiad now generates native SVG text elements (no foreignObject) and
    /// supports theme color overrides, this is much simpler than before.
    /// Handles remaining foreignObject from other diagram types and font cleanup.
    /// </summary>
    private string PostProcessSvg(string svgContent)
    {
        // Convert any remaining foreignObject elements (from non-flowchart diagram types)
        var foreignObjectRegex = ForeignObjectRegex();
        svgContent = foreignObjectRegex.Replace(svgContent, match =>
        {
            var x = match.Groups["x"].Value;
            var y = match.Groups["y"].Value;
            var width = match.Groups["width"].Value;
            var height = match.Groups["height"].Value;
            var innerHtml = match.Groups["content"].Value;

            var textContent = ExtractTextFromHtml(innerHtml);
            if (string.IsNullOrWhiteSpace(textContent)) return "";

            var centerX = double.TryParse(x, out var xVal) ? xVal : 0;
            var centerY = double.TryParse(y, out var yVal) ? yVal : 0;
            var w = double.TryParse(width, out var wVal) ? wVal : 0;
            var h = double.TryParse(height, out var hVal) ? hVal : 0;
            centerX += w / 2;
            centerY += h / 2 + 5;

            const string fontFamily = "Segoe UI, Arial, sans-serif";
            return
                $@"<text x=""{centerX}"" y=""{centerY}"" text-anchor=""middle"" dy=""0.35em"" fill=""{_themeTextColor}"" font-family=""{fontFamily}"" font-size=""14"">{SecurityElement.Escape(textContent)}</text>";
        });

        // Replace Mermaid's default fonts with system fonts
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
