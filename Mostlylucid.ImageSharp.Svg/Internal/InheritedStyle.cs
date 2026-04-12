namespace Mostlylucid.ImageSharp.Svg.Internal;

/// <summary>
/// SVG inheritable presentation attributes that cascade down through
/// <c>&lt;g&gt;</c> ancestors. The renderer threads an instance of this
/// struct through draw calls so leaf elements (<c>&lt;text&gt;</c>,
/// <c>&lt;rect&gt;</c>, …) see the merged values without walking back up
/// the AST. The wire spec for which attributes inherit lives in
/// <see href="https://www.w3.org/TR/SVG11/propidx.html"/>; we honour the
/// subset that real-world SVGs (shields, mermaid) actually depend on.
/// </summary>
internal readonly struct InheritedStyle
{
    public string? FontFamily { get; init; }
    public double? FontSize { get; init; }
    public string? FontWeight { get; init; }
    public string? Fill { get; init; }
    public string? Stroke { get; init; }
    public double? StrokeWidth { get; init; }
    public string? TextAnchor { get; init; }
    public double Opacity { get; init; }

    /// <summary>
    /// Per-fill alpha multiplier from the <c>fill-opacity</c> attribute.
    /// SVG semantically separates this from <c>opacity</c> — opacity dims
    /// the entire element including stroke + children, fill-opacity only
    /// dims the fill paint. Shields rely on this for their drop-shadow
    /// text trick (a dark text at <c>fill-opacity=".3"</c> overlaid by a
    /// solid white text); without it the shadow renders as solid black.
    /// </summary>
    public double FillOpacity { get; init; }

    /// <summary>Per-stroke alpha multiplier from <c>stroke-opacity</c>.</summary>
    public double StrokeOpacity { get; init; }

    public static InheritedStyle Default => new()
    {
        Opacity = 1d,
        FillOpacity = 1d,
        StrokeOpacity = 1d,
    };

    public InheritedStyle Merge(SvgNode node, SvgCssRules? cssRules = null)
    {
        // Allocation fast-path: a node with no `style=""` and no `class=""`
        // (or no CSS rules to match against) walks the cascade by checking
        // ONLY inline attributes — no dict creation, no parsing. This is
        // the common case for shields' inline-attribute-only elements.
        var hasStyle = node.Attributes.ContainsKey("style");
        var hasClass = !cssRules?.IsEmpty == true && node.Attributes.ContainsKey("class");

        if (!hasStyle && !hasClass)
        {
            return MergeFromAttributesOnly(node);
        }

        // Slow path: parse the style attribute and walk class rules.
        var inlineStyle = hasStyle
            ? SvgValueParser.ParseStyle(node.Get("style"))
            : null;

        Dictionary<string, string>? classProps = null;
        if (hasClass)
        {
            var classAttr = node.Get("class")!;
            foreach (var token in classAttr.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var rules = cssRules!.GetClassProperties(token);
                if (rules == null) continue;
                classProps ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (k, v) in rules) classProps[k] = v;
            }
        }

        var combinedOpacity      = MultiplyOpacity(Opacity,      Pick("opacity",        node, inlineStyle, classProps));
        var combinedFillOpacity  = MultiplyOpacity(FillOpacity,  Pick("fill-opacity",   node, inlineStyle, classProps));
        var combinedStrokeOpacity= MultiplyOpacity(StrokeOpacity, Pick("stroke-opacity", node, inlineStyle, classProps));

        return new InheritedStyle
        {
            FontFamily    = Pick("font-family", node, inlineStyle, classProps) ?? FontFamily,
            FontSize      = SvgValueParser.ParseNullableNumber(Pick("font-size", node, inlineStyle, classProps)) ?? FontSize,
            FontWeight    = Pick("font-weight", node, inlineStyle, classProps) ?? FontWeight,
            Fill          = Pick("fill", node, inlineStyle, classProps) ?? Fill,
            Stroke        = Pick("stroke", node, inlineStyle, classProps) ?? Stroke,
            StrokeWidth   = SvgValueParser.ParseNullableNumber(Pick("stroke-width", node, inlineStyle, classProps)) ?? StrokeWidth,
            TextAnchor    = Pick("text-anchor", node, inlineStyle, classProps) ?? TextAnchor,
            Opacity       = combinedOpacity,
            FillOpacity   = combinedFillOpacity,
            StrokeOpacity = combinedStrokeOpacity,
        };
    }

    /// <summary>
    /// Fast-path merge for nodes that only carry inline attributes — no
    /// <c>style=""</c>, no class lookup. Skips every dictionary
    /// allocation, which is the dominant per-render allocation cost on
    /// shield-style content.
    /// </summary>
    private InheritedStyle MergeFromAttributesOnly(SvgNode node)
    {
        return new InheritedStyle
        {
            FontFamily    = node.Get("font-family") ?? FontFamily,
            FontSize      = SvgValueParser.ParseNullableNumber(node.Get("font-size")) ?? FontSize,
            FontWeight    = node.Get("font-weight") ?? FontWeight,
            Fill          = node.Get("fill") ?? Fill,
            Stroke        = node.Get("stroke") ?? Stroke,
            StrokeWidth   = SvgValueParser.ParseNullableNumber(node.Get("stroke-width")) ?? StrokeWidth,
            TextAnchor    = node.Get("text-anchor") ?? TextAnchor,
            Opacity       = MultiplyOpacity(Opacity, node.Get("opacity")),
            FillOpacity   = MultiplyOpacity(FillOpacity, node.Get("fill-opacity")),
            StrokeOpacity = MultiplyOpacity(StrokeOpacity, node.Get("stroke-opacity")),
        };
    }

    private static double MultiplyOpacity(double parent, string? raw)
    {
        var own = SvgValueParser.ParseNullableNumber(raw);
        return own.HasValue ? parent * own.Value : parent;
    }

    private static string? Pick(
        string property,
        SvgNode node,
        IReadOnlyDictionary<string, string>? inlineStyle,
        IReadOnlyDictionary<string, string>? classProps)
    {
        var inlineAttr = node.Get(property);
        if (inlineAttr != null) return inlineAttr;
        if (inlineStyle != null && inlineStyle.TryGetValue(property, out var s)) return s;
        if (classProps != null && classProps.TryGetValue(property, out var c)) return c;
        return null;
    }
}
