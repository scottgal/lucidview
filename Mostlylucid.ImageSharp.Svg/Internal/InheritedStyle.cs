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

    public static InheritedStyle Default => new() { Opacity = 1d };

    public InheritedStyle Merge(SvgNode node, SvgCssRules? cssRules = null)
    {
        // The cascade for a single property is:
        //   1. inline attribute on the element            (highest)
        //   2. inline `style=""` declaration on the element
        //   3. matching CSS class rules (if any)
        //   4. value inherited from the parent             (lowest)
        var inlineStyle = SvgValueParser.ParseStyle(node.Get("style"));

        // Pull in every matching class rule. Multiple classes merge in
        // declaration order (later overrides earlier) — same as the browser
        // cascade for equal-specificity selectors.
        Dictionary<string, string>? classProps = null;
        if (cssRules is { IsEmpty: false })
        {
            var classAttr = node.Get("class");
            if (!string.IsNullOrEmpty(classAttr))
            {
                foreach (var token in classAttr.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    var rules = cssRules.GetClassProperties(token);
                    if (rules == null) continue;
                    classProps ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (k, v) in rules) classProps[k] = v;
                }
            }
        }

        var rawOpacity = Pick("opacity", node, inlineStyle, classProps);
        var ownOpacity = SvgValueParser.ParseNullableNumber(rawOpacity);
        var combinedOpacity = ownOpacity.HasValue
            ? Opacity * ownOpacity.Value
            : Opacity;

        return new InheritedStyle
        {
            FontFamily  = Pick("font-family", node, inlineStyle, classProps) ?? FontFamily,
            FontSize    = SvgValueParser.ParseNullableNumber(Pick("font-size", node, inlineStyle, classProps)) ?? FontSize,
            FontWeight  = Pick("font-weight", node, inlineStyle, classProps) ?? FontWeight,
            Fill        = Pick("fill", node, inlineStyle, classProps) ?? Fill,
            Stroke      = Pick("stroke", node, inlineStyle, classProps) ?? Stroke,
            StrokeWidth = SvgValueParser.ParseNullableNumber(Pick("stroke-width", node, inlineStyle, classProps)) ?? StrokeWidth,
            TextAnchor  = Pick("text-anchor", node, inlineStyle, classProps) ?? TextAnchor,
            Opacity     = combinedOpacity,
        };
    }

    private static string? Pick(
        string property,
        SvgNode node,
        IReadOnlyDictionary<string, string> inlineStyle,
        IReadOnlyDictionary<string, string>? classProps)
    {
        var inlineAttr = node.Get(property);
        if (inlineAttr != null) return inlineAttr;
        if (inlineStyle.TryGetValue(property, out var s)) return s;
        if (classProps != null && classProps.TryGetValue(property, out var c)) return c;
        return null;
    }
}
