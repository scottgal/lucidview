using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using MermaidSharp.Rendering;
using MermaidSharp.Rendering.Skins;

namespace MermaidSharp;

public static class Mermaid
{
    // Matches %%{init: { ... }}%% directives (single or multi-line)
    static readonly Regex InitDirectivePattern = new(
        @"%%\s*\{\s*init\s*:\s*(\{[\s\S]*?\})\s*\}%%",
        RegexCompat.Compiled | RegexOptions.IgnoreCase);
    // Naiad extension directives in Mermaid comments, ignored by mermaid.js.
    // Example:
    //   %% naiad: skinPack=daisyui, theme=dark
    //   %% naiad: {"skinPack":"material","theme":"forest"}
    static readonly Regex NaiadDirectivePattern = new(
        @"^\s*%%\s*naiad\s*:\s*(?<value>.+?)\s*$",
        RegexCompat.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static string Render(string input, RenderOptions? options = null)
    {
        var doc = RenderToDocument(input, options);
        return doc?.ToXml() ?? throw new MermaidException("Rendering returned no document");
    }

    /// <summary>
    /// Returns named built-in shape skin packs that are available from embedded archives.
    /// </summary>
    public static IReadOnlyList<string> GetBuiltInSkinPacks() =>
        ShapeSkinCatalog.GetBuiltInPackNames();

    /// <summary>
    /// Returns names of skin packs registered via <see cref="MermaidSkinPacks"/>.
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredSkinPacks() =>
        ShapeSkinCatalog.GetRegisteredPackNames();

    /// <summary>
    /// Returns all available skin packs (built-in + registered plugins).
    /// </summary>
    public static IReadOnlyList<string> GetAvailableSkinPacks() =>
        ShapeSkinCatalog.GetAvailablePackNames();

    /// <summary>
    /// Render mermaid input to a structured SvgDocument without serializing to XML.
    /// Returns the document object for direct consumption (e.g. native Avalonia rendering).
    /// </summary>
    public static SvgDocument? RenderToDocument(string input, RenderOptions? options = null)
    {
        options ??= RenderOptions.Default;

        SecurityValidator.ValidateInput(input, options);

        // Parse and apply directives before rendering.
        options = ApplyNaiadDirectives(input, ApplyInitDirectives(input, options));

        var diagramType = DetectDiagramType(input);

        return SecurityValidator.WithTimeout(() => diagramType switch
        {
            DiagramType.Pie => RenderPieDoc(input, options),
            DiagramType.Flowchart => RenderFlowchartDoc(input, options),
            DiagramType.Sequence => RenderSequenceDoc(input, options),
            DiagramType.Class => RenderClassDoc(input, options),
            DiagramType.State => RenderStateDoc(input, options),
            DiagramType.EntityRelationship => RenderEntityRelationshipDoc(input, options),
            DiagramType.GitGraph => RenderGitGraphDoc(input, options),
            DiagramType.Gantt => RenderGanttDoc(input, options),
            DiagramType.Mindmap => RenderMindmapDoc(input, options),
            DiagramType.Timeline => RenderTimelineDoc(input, options),
            DiagramType.UserJourney => RenderUserJourneyDoc(input, options),
            DiagramType.Quadrant => RenderQuadrantDoc(input, options),
            DiagramType.XYChart => RenderXYChartDoc(input, options),
            DiagramType.Sankey => RenderSankeyDoc(input, options),
            DiagramType.Block => RenderBlockDoc(input, options),
            DiagramType.Kanban => RenderKanbanDoc(input, options),
            DiagramType.Packet => RenderPacketDoc(input, options),
            DiagramType.C4Context => RenderC4Doc(input, options),
            DiagramType.C4Container => RenderC4Doc(input, options),
            DiagramType.C4Component => RenderC4Doc(input, options),
            DiagramType.C4Deployment => RenderC4Doc(input, options),
            DiagramType.Requirement => RenderRequirementDoc(input, options),
            DiagramType.Architecture => RenderArchitectureDoc(input, options),
            DiagramType.Radar => RenderRadarDoc(input, options),
            DiagramType.Treemap => RenderTreemapDoc(input, options),
            DiagramType.Bpmn => RenderBpmnDoc(input, options),
            _ => throw new MermaidException($"Unsupported diagram type: {diagramType}")
        }, options.RenderTimeout, "Diagram rendering");
    }

    /// <summary>
    /// Parse a flowchart and run layout without generating SVG.
    /// Returns positioned model objects for native rendering, or null if parsing fails.
    /// </summary>
    public static FlowchartLayoutResult? ParseAndLayoutFlowchart(string input, RenderOptions? options = null)
    {
        options ??= RenderOptions.Default;
        SecurityValidator.ValidateInput(input, options);
        options = ApplyNaiadDirectives(input, ApplyInitDirectives(input, options));

        var parser = new FlowchartParser();
        var result = parser.Parse(input);
        if (!result.Success) return null;

        var renderer = new FlowchartRenderer();
        return renderer.LayoutModel(result.Value, options);
    }

    public static DiagramType DetectDiagramType(string input)
    {
        // Skip %%{init}%% directives and %% comment lines to find the actual diagram type
        var firstLine = input.TrimStart();
        while (firstLine.StartsWith("%%"))
        {
            var newlineIdx = firstLine.IndexOf('\n');
            if (newlineIdx < 0) break;
            firstLine = firstLine[(newlineIdx + 1)..].TrimStart();
        }

        if (firstLine.StartsWith("pie", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Pie;
        if (firstLine.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("graph", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Flowchart;
        if (firstLine.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Sequence;
        if (firstLine.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Class;
        if (firstLine.StartsWith("stateDiagram", StringComparison.OrdinalIgnoreCase))
            return DiagramType.State;
        if (firstLine.StartsWith("erDiagram", StringComparison.OrdinalIgnoreCase))
            return DiagramType.EntityRelationship;
        if (firstLine.StartsWith("gantt", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Gantt;
        if (firstLine.StartsWith("gitGraph", StringComparison.OrdinalIgnoreCase))
            return DiagramType.GitGraph;
        if (firstLine.StartsWith("mindmap", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Mindmap;
        if (firstLine.StartsWith("timeline", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Timeline;
        if (firstLine.StartsWith("journey", StringComparison.OrdinalIgnoreCase))
            return DiagramType.UserJourney;
        if (firstLine.StartsWith("quadrantChart", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Quadrant;
        if (firstLine.StartsWith("xychart", StringComparison.OrdinalIgnoreCase))
            return DiagramType.XYChart;
        if (firstLine.StartsWith("sankey", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Sankey;
        if (firstLine.StartsWith("block", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Block;
        if (firstLine.StartsWith("packet", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Packet;
        if (firstLine.StartsWith("kanban", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Kanban;
        if (firstLine.StartsWith("architecture-beta", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("architecture", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Architecture;
        if (firstLine.StartsWith("C4Context", StringComparison.OrdinalIgnoreCase))
            return DiagramType.C4Context;
        if (firstLine.StartsWith("C4Container", StringComparison.OrdinalIgnoreCase))
            return DiagramType.C4Container;
        if (firstLine.StartsWith("C4Component", StringComparison.OrdinalIgnoreCase))
            return DiagramType.C4Component;
        if (firstLine.StartsWith("C4Deployment", StringComparison.OrdinalIgnoreCase))
            return DiagramType.C4Deployment;
        if (firstLine.StartsWith("requirementDiagram", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Requirement;
        if (firstLine.StartsWith("radar-beta", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("radar", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Radar;
        if (firstLine.StartsWith("treemap-beta", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("treemap", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Treemap;

        // BPMN XML detection: <?xml or <definitions or <bpmn:definitions
        if (firstLine.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("<definitions", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("<bpmn:definitions", StringComparison.OrdinalIgnoreCase))
            return DiagramType.Bpmn;

        throw new MermaidException($"Unknown diagram type in: {firstLine.Split('\n')[0]}");
    }

    /// <summary>
    /// Parse %%{init: {...}}%% directives and apply theme/themeVariables to RenderOptions.
    /// Returns a new RenderOptions if directives are found, otherwise returns the original.
    /// </summary>
    static RenderOptions ApplyInitDirectives(string input, RenderOptions options)
    {
        var match = InitDirectivePattern.Match(input);
        if (!match.Success)
            return options;

        try
        {
            var json = match.Groups[1].Value;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Clone options to avoid mutating shared instances
            var result = options.Clone();

            // Apply theme name
            if (root.TryGetProperty("theme", out var themeProp) &&
                themeProp.ValueKind == JsonValueKind.String)
            {
                result.Theme = themeProp.GetString() ?? result.Theme;
            }

            // Apply fontFamily
            if (root.TryGetProperty("fontFamily", out var fontProp) &&
                fontProp.ValueKind == JsonValueKind.String)
            {
                var val = fontProp.GetString();
                if (!string.IsNullOrEmpty(val))
                    result.FontFamily = SecurityValidator.SanitizeCss(val);
            }

            // Apply themeVariables as ThemeColorOverrides
            if (root.TryGetProperty("themeVariables", out var vars) &&
                vars.ValueKind == JsonValueKind.Object)
            {
                var overrides = result.ThemeColors ?? new ThemeColorOverrides();

                if (TryGetColor(vars, "primaryTextColor", out var textColor))
                    overrides.TextColor = textColor;
                if (TryGetColor(vars, "primaryColor", out var nodeFill))
                    overrides.NodeFill = nodeFill;
                if (TryGetColor(vars, "primaryBorderColor", out var nodeStroke))
                    overrides.NodeStroke = nodeStroke;
                if (TryGetColor(vars, "lineColor", out var edgeStroke))
                    overrides.EdgeStroke = edgeStroke;
                if (TryGetColor(vars, "mainBkg", out var mainBkg))
                    overrides.NodeFill = mainBkg;
                if (TryGetColor(vars, "nodeBorder", out var nodeBorder))
                    overrides.NodeStroke = nodeBorder;
                if (TryGetColor(vars, "clusterBkg", out var clusterBkg))
                    overrides.SubgraphFill = clusterBkg;
                if (TryGetColor(vars, "clusterBorder", out var clusterBorder))
                    overrides.SubgraphStroke = clusterBorder;
                if (TryGetColor(vars, "edgeLabelBackground", out var edgeLabelBg))
                    overrides.EdgeLabelBackground = edgeLabelBg;
                if (TryGetColor(vars, "background", out var bg))
                    overrides.BackgroundColor = bg;
                // Also support direct color names matching our ThemeColorOverrides
                if (TryGetColor(vars, "textColor", out var tc))
                    overrides.TextColor = tc;
                if (TryGetColor(vars, "nodeStroke", out var ns))
                    overrides.NodeStroke = ns;
                if (TryGetColor(vars, "edgeStroke", out var es))
                    overrides.EdgeStroke = es;

                result.ThemeColors = overrides;
            }

            return result;
        }
        catch (JsonException)
        {
            // If JSON is malformed, just ignore the directive
            return options;
        }
    }

    /// <summary>
    /// Parse Naiad directives from Mermaid comments:
    ///   %% naiad: skinPack=daisyui, theme=dark
    ///   %% naiad: {"skinPack":"material","theme":"forest"}
    /// Mermaid.js ignores these lines because they are standard %% comments.
    /// </summary>
    static RenderOptions ApplyNaiadDirectives(string input, RenderOptions options)
    {
        var matches = NaiadDirectivePattern.Matches(input);
        if (matches.Count == 0)
            return options;

        var result = options.Clone();

        foreach (Match match in matches)
        {
            var rawPayload = match.Groups["value"].Value.Trim();
            if (string.IsNullOrEmpty(rawPayload))
                continue;

            if (rawPayload.EndsWith("%%", StringComparison.Ordinal))
                rawPayload = rawPayload[..^2].Trim();

            ApplyNaiadDirectivePayload(rawPayload, result);
        }

        return result;
    }

    static void ApplyNaiadDirectivePayload(string payload, RenderOptions options)
    {
        if (payload.StartsWith('{'))
        {
            TryApplyJsonDirective(payload, options);
            return;
        }

        foreach (var token in payload.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eqIndex = token.IndexOf('=');
            if (eqIndex < 0)
                eqIndex = token.IndexOf(':');

            if (eqIndex < 0)
            {
                // Single token shorthand: %% naiad: daisyui
                ApplyNaiadOption("skinPack", token, options);
                continue;
            }

            var key = token[..eqIndex].Trim();
            var value = token[(eqIndex + 1)..].Trim().Trim('"');
            ApplyNaiadOption(key, value, options);
        }
    }

    static void TryApplyJsonDirective(string payload, RenderOptions options)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    ApplyNaiadOption(prop.Name, prop.Value.GetString() ?? "", options);
                }
                else if (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False)
                {
                    ApplyNaiadOption(prop.Name, prop.Value.GetBoolean() ? "true" : "false", options);
                }
                else if (prop.Value.ValueKind == JsonValueKind.Number)
                {
                    ApplyNaiadOption(prop.Name, prop.Value.GetRawText(), options);
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed Naiad extension directives.
        }
    }

    static void ApplyNaiadOption(string key, string value, RenderOptions options)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return;

        var normalizedKey = key.Trim().ToLowerInvariant()
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal);

        switch (normalizedKey)
        {
            case "theme":
                options.Theme = value;
                break;
            case "skin":
            case "skinpack":
            case "shapepack":
            case "shapeskin":
                options.SkinPack = value;
                break;
            case "fontfamily":
                options.FontFamily = SecurityValidator.SanitizeCss(value);
                break;
            case "fontsize":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fontSize) && fontSize > 0)
                    options.FontSize = fontSize;
                break;
            case "padding":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var padding) && padding >= 0)
                    options.Padding = padding;
                break;
            case "curvededges":
                if (bool.TryParse(value, out var curvedEdges))
                    options.CurvedEdges = curvedEdges;
                break;
            case "includeexternalresources":
                if (bool.TryParse(value, out var includeExternal))
                    options.IncludeExternalResources = includeExternal;
                break;
            case "shapes":
                ParseShapeDirective(value, options);
                break;

            // B1: Per-node size control
            case "minnodewidth":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var minW) && minW > 0)
                    options.MinNodeWidth = minW;
                break;
            case "minnodeheight":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var minH) && minH > 0)
                    options.MinNodeHeight = minH;
                break;
            case "nodesize":
                ParseNodeSizeDirective(value, options);
                break;

            // B2: Layout spacing control
            case "ranksep":
            case "rankseparation":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rankSep) && rankSep > 0)
                    options.RankSeparation = rankSep;
                break;
            case "nodesep":
            case "nodeseparation":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var nodeSep) && nodeSep > 0)
                    options.NodeSeparation = nodeSep;
                break;
            case "edgesep":
            case "edgeseparation":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var edgeSep) && edgeSep > 0)
                    options.EdgeSeparation = edgeSep;
                break;

            // B3: Edge curve style
            case "curve":
            case "curvestyle":
                options.CurveStyle = value.ToLowerInvariant() switch
                {
                    "linear" or "polyline" => CurveStyle.Linear,
                    "step" or "orthogonal" => CurveStyle.Step,
                    _ => CurveStyle.Basis
                };
                break;

            // B4: Equalize node sizes
            case "equalizenodes":
            case "equalizenodesizes":
                if (bool.TryParse(value, out var equalize))
                    options.EqualizeNodeSizes = equalize;
                break;
        }
    }

    /// <summary>
    /// Parse shape mapping directive: "nav=navbar, btn=button, search=searchbar"
    /// Maps node IDs to skin shape names without modifying the mermaid syntax.
    /// </summary>
    static void ParseShapeDirective(string value, RenderOptions options)
    {
        options.SkinShapeMap ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eqIdx = pair.IndexOf('=');
            if (eqIdx <= 0) continue;

            var nodeId = pair[..eqIdx].Trim();
            var shapeName = pair[(eqIdx + 1)..].Trim().Trim('"');
            if (!string.IsNullOrEmpty(nodeId) && !string.IsNullOrEmpty(shapeName))
                options.SkinShapeMap[nodeId] = shapeName;
        }
    }

    /// <summary>
    /// Parse per-node size directive: "A:200x60,B:150x40"
    /// Maps node IDs to explicit (Width, Height) dimensions.
    /// </summary>
    static void ParseNodeSizeDirective(string value, RenderOptions options)
    {
        options.NodeSizeMap ??= new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colonIdx = pair.IndexOf(':');
            if (colonIdx <= 0) continue;

            var nodeId = pair[..colonIdx].Trim();
            var sizeStr = pair[(colonIdx + 1)..].Trim();
            var xIdx = sizeStr.IndexOf('x', StringComparison.OrdinalIgnoreCase);
            if (xIdx <= 0) continue;

            if (double.TryParse(sizeStr[..xIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) &&
                double.TryParse(sizeStr[(xIdx + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var h) &&
                w > 0 && h > 0)
            {
                options.NodeSizeMap[nodeId] = (w, h);
            }
        }
    }

    /// <summary>
    /// Safely extract a color value from a JSON element, validating it looks like a CSS color.
    /// </summary>
    static bool TryGetColor(JsonElement parent, string property, out string color)
    {
        color = "";
        if (!parent.TryGetProperty(property, out var prop) ||
            prop.ValueKind != JsonValueKind.String)
            return false;

        var val = prop.GetString();
        if (string.IsNullOrWhiteSpace(val))
            return false;

        // Basic CSS color validation: hex, rgb(), rgba(), hsl(), named colors
        val = val.Trim();
        if (val.StartsWith('#') || val.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) ||
            val.StartsWith("hsl", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(val, @"^[a-zA-Z]+$"))
        {
            // Security: reject anything that could be CSS injection
            if (val.Contains('<') || val.Contains('>') || val.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
                val.Contains("expression", StringComparison.OrdinalIgnoreCase) || val.Contains(';'))
                return false;

            color = val;
            return true;
        }

        return false;
    }

    static SvgDocument RenderPieDoc(string input, RenderOptions options)
    {
        var parser = new PieParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse pie chart: {result.Error}");
        return new PieRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderFlowchartDoc(string input, RenderOptions options)
    {
        var parser = new FlowchartParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse flowchart: {result.Error}");
        return new FlowchartRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderSequenceDoc(string input, RenderOptions options)
    {
        var parser = new SequenceParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse sequence diagram: {result.Error}");
        return new SequenceRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderClassDoc(string input, RenderOptions options)
    {
        var parser = new ClassParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse class diagram: {result.Error}");
        return new ClassRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderStateDoc(string input, RenderOptions options)
    {
        var parser = new StateParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse state diagram: {result.Error}");
        return new StateRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderEntityRelationshipDoc(string input, RenderOptions options)
    {
        var parser = new ERParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse ER diagram: {result.Error}");
        return new ERRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderGitGraphDoc(string input, RenderOptions options)
    {
        var parser = new GitGraphParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse git graph: {result.Error}");
        return new GitGraphRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderGanttDoc(string input, RenderOptions options)
    {
        var parser = new GanttParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse gantt chart: {result.Error}");
        return new GanttRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderMindmapDoc(string input, RenderOptions options)
    {
        var parser = new MindmapParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse mindmap: {result.Error}");
        return new MindmapRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderTimelineDoc(string input, RenderOptions options)
    {
        var parser = new TimelineParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse timeline: {result.Error}");
        return new TimelineRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderUserJourneyDoc(string input, RenderOptions options)
    {
        var parser = new UserJourneyParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse user journey: {result.Error}");
        return new UserJourneyRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderQuadrantDoc(string input, RenderOptions options)
    {
        var parser = new QuadrantParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse quadrant chart: {result.Error}");
        return new QuadrantRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderXYChartDoc(string input, RenderOptions options)
    {
        var parser = new XYChartParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse XY chart: {result.Error}");
        return new XYChartRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderSankeyDoc(string input, RenderOptions options)
    {
        var parser = new SankeyParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse Sankey diagram: {result.Error}");
        return new SankeyRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderBlockDoc(string input, RenderOptions options)
    {
        var parser = new BlockParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse block diagram: {result.Error}");
        return new BlockRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderKanbanDoc(string input, RenderOptions options)
    {
        var parser = new KanbanParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse kanban board: {result.Error}");
        return new KanbanRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderPacketDoc(string input, RenderOptions options)
    {
        var parser = new PacketParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse packet diagram: {result.Error}");
        return new PacketRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderC4Doc(string input, RenderOptions options)
    {
        var parser = new C4Parser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse C4 diagram: {result.Error}");
        return new C4Renderer().Render(result.Value, options);
    }

    static SvgDocument RenderRequirementDoc(string input, RenderOptions options)
    {
        var parser = new RequirementParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse requirement diagram: {result.Error}");
        return new RequirementRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderArchitectureDoc(string input, RenderOptions options)
    {
        var parser = new ArchitectureParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse architecture diagram: {result.Error}");
        return new ArchitectureRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderRadarDoc(string input, RenderOptions options)
    {
        var parser = new RadarParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse radar diagram: {result.Error}");
        return new RadarRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderTreemapDoc(string input, RenderOptions options)
    {
        var parser = new TreemapParser();
        var result = parser.Parse(input);
        if (!result.Success)
            throw new MermaidParseException($"Failed to parse treemap diagram: {result.Error}");
        return new TreemapRenderer().Render(result.Value, options);
    }

    static SvgDocument RenderBpmnDoc(string input, RenderOptions options)
    {
        var model = BpmnParser.Parse(input);
        return new FlowchartRenderer().Render(model, options);
    }
}

public class MermaidException(string message) : Exception(message);

public class MermaidParseException : MermaidException
{
    public MermaidParseException(string message) : base(message) { }
}
